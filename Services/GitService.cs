using System.Diagnostics;
using System.Text;

namespace ClineWin
{
    /// <summary>Un file modificato. Staged: la modifica è nell'indice (pronta per il commit).</summary>
    public sealed record GitChange(string Path, string Status, bool Staged)
    {
        /// <summary>Lettera mostrata accanto al file: M modificato, A aggiunto, D eliminato, R rinominato, U nuovo (non tracciato).</summary>
        public string Letter => Status switch
        {
            "untracked" => "U",
            "added" => "A",
            "deleted" => "D",
            "renamed" => "R",
            "conflict" => "!",
            _ => "M",
        };

        public string Description => Status switch
        {
            "untracked" => "nuovo",
            "added" => "aggiunto",
            "deleted" => "eliminato",
            "renamed" => "rinominato",
            "conflict" => "in conflitto",
            _ => "modificato",
        };
    }

    public sealed record GitStatus(string Branch, IReadOnlyList<GitChange> Changes, int Ahead, int Behind);

    /// <summary>Errore di git, con il messaggio da mostrare.</summary>
    public sealed class GitException(string message) : Exception(message);

    /// <summary>
    /// Operazioni git sul progetto, eseguite con il git installato sul computer.
    /// Tutti i percorsi sono relativi alla radice del repository.
    /// </summary>
    public static class GitService
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

        /// <summary>Radice del repository che contiene la cartella, oppure null se non è un repository (o git manca).</summary>
        public static async Task<string?> FindRootAsync(string folder)
        {
            try
            {
                var (code, output, _) = await RunAsync(folder, "rev-parse", "--show-toplevel");
                return code == 0 ? Path.GetFullPath(output.Trim()) : null;
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or GitException)
            {
                return null;
            }
        }

        public static async Task<GitStatus> StatusAsync(string root)
        {
            var (code, output, error) = await RunAsync(root, "status", "--porcelain=v1", "-z", "--branch", "--untracked-files=all");
            if (code != 0) throw new GitException(error.Trim());

            string branch = "";
            int ahead = 0, behind = 0;
            var changes = new List<GitChange>();
            var entries = output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < entries.Length; i++)
            {
                string entry = entries[i];
                if (entry.StartsWith("## ", StringComparison.Ordinal))
                {
                    // "## main...origin/main [ahead 1, behind 2]" oppure "## No commits yet on main"
                    string head = entry[3..];
                    if (head.StartsWith("No commits yet on ", StringComparison.Ordinal)) head = head["No commits yet on ".Length..];
                    branch = head.Split("...")[0].Split(' ')[0];
                    ahead = ParseCount(head, "ahead ");
                    behind = ParseCount(head, "behind ");
                    continue;
                }
                if (entry.Length < 4) continue;
                char x = entry[0], y = entry[1];
                string path = entry[3..];
                // Per le rinomine, -z mette il percorso di origine nella voce successiva.
                if (x == 'R' || y == 'R') i++;

                if (x == '?' && y == '?') { changes.Add(new GitChange(path, "untracked", false)); continue; }
                if (x == 'U' || y == 'U' || (x == 'A' && y == 'A') || (x == 'D' && y == 'D')) { changes.Add(new GitChange(path, "conflict", false)); continue; }
                if (x != ' ') changes.Add(new GitChange(path, StatusName(x), true));
                if (y != ' ') changes.Add(new GitChange(path, StatusName(y), false));
            }
            return new GitStatus(branch, changes, ahead, behind);
        }

        private static string StatusName(char code) => code switch
        {
            'A' => "added",
            'D' => "deleted",
            'R' => "renamed",
            _ => "modified",
        };

        private static int ParseCount(string text, string label)
        {
            int index = text.IndexOf(label, StringComparison.Ordinal);
            if (index < 0) return 0;
            var digits = new string(text[(index + label.Length)..].TakeWhile(char.IsDigit).ToArray());
            return int.TryParse(digits, out int n) ? n : 0;
        }

        /// <summary>
        /// Testo prima e dopo di una modifica, per il diff. Preparata: HEAD → indice.
        /// Non preparata: indice → file sul disco (per un file nuovo, prima è vuoto).
        /// </summary>
        public static async Task<(string Before, string After)> DiffTextsAsync(string root, GitChange change)
        {
            string disk = Path.Combine(root, change.Path.Replace('/', Path.DirectorySeparatorChar));
            if (change.Staged)
            {
                string before = change.Status == "added" ? "" : await ShowAsync(root, $"HEAD:{change.Path}");
                string after = change.Status == "deleted" ? "" : await ShowAsync(root, $":{change.Path}");
                return (before, after);
            }
            string original = change.Status == "untracked" ? "" : await ShowAsync(root, $":{change.Path}");
            string current = File.Exists(disk) ? await File.ReadAllTextAsync(disk) : "";
            return (original, current);
        }

        private static async Task<string> ShowAsync(string root, string spec)
        {
            var (code, output, _) = await RunAsync(root, "show", spec);
            return code == 0 ? output : "";
        }

        public static Task StageAsync(string root, string path) => CheckAsync(root, "add", "--", path);

        public static Task UnstageAsync(string root, string path) => CheckAsync(root, "restore", "--staged", "--", path);

        /// <summary>Annulla le modifiche non preparate: ripristina il file dall'indice, oppure elimina un file nuovo.</summary>
        public static async Task DiscardAsync(string root, GitChange change)
        {
            if (change.Status == "untracked")
            {
                string full = Path.GetFullPath(Path.Combine(root, change.Path));
                if (!full.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase)) throw new GitException("Percorso fuori dal repository.");
                File.Delete(full);
                return;
            }
            await CheckAsync(root, "restore", "--", change.Path);
        }

        public static async Task CommitAsync(string root, string message) => await CheckAsync(root, "commit", "-m", message);

        public static async Task<IReadOnlyList<string>> BranchesAsync(string root)
        {
            var (code, output, error) = await RunAsync(root, "branch", "--format=%(refname:short)");
            if (code != 0) throw new GitException(error.Trim());
            return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        public static Task CheckoutAsync(string root, string branch) => CheckAsync(root, "switch", branch);

        public static Task CreateBranchAsync(string root, string branch) => CheckAsync(root, "switch", "-c", branch);

        /// <summary>Esegue git e lancia GitException con il messaggio di git se fallisce.</summary>
        private static async Task CheckAsync(string root, params string[] args)
        {
            var (code, output, error) = await RunAsync(root, args);
            if (code != 0)
            {
                string message = (error.Length > 0 ? error : output).Trim();
                throw new GitException(message.Length > 0 ? message : $"git {args[0]} non è riuscito (codice {code}).");
            }
        }

        private static async Task<(int Code, string Output, string Error)> RunAsync(string folder, params string[] args)
        {
            var start = new ProcessStartInfo("git")
            {
                WorkingDirectory = folder,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            // Percorsi con accenti senza "\303\250", e nessun pager o editor interattivo.
            start.ArgumentList.Add("-c");
            start.ArgumentList.Add("core.quotepath=false");
            foreach (var arg in args) start.ArgumentList.Add(arg);
            start.Environment["GIT_TERMINAL_PROMPT"] = "0";
            start.Environment["GIT_PAGER"] = "cat";

            using var process = Process.Start(start) ?? throw new GitException("Impossibile avviare git.");
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            using var cts = new CancellationTokenSource(Timeout);
            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                throw new GitException("git non ha risposto entro 30 secondi.");
            }
            return (process.ExitCode, await outputTask, await errorTask);
        }
    }
}
