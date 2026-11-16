using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ClineWin
{
    /// <summary>Come presentare una chiamata a uno strumento all'utente.</summary>
    public sealed record ToolPreview(
        string Glyph,
        string Summary,
        ToolCategory Category,
        string? Detail = null,
        string? DetailLanguage = null,
        FileDiff? Diff = null);

    /// <summary>Modifica a un file da mostrare come diff.</summary>
    public sealed record FileDiff(string Path, DiffResult Result, bool NewFile);

    /// <summary>
    /// Strumenti che il modello può usare sul progetto aperto: leggere ed elencare file,
    /// cercare nel codice, scrivere o modificare file ed eseguire comandi PowerShell.
    /// Tutti i percorsi sono relativi alla cartella del progetto e non possono uscirne.
    /// Ogni azione ha una categoria (<see cref="ToolCategory"/>): l'approvazione dipende da
    /// <see cref="AutoApproval"/> (di default solo la lettura è automatica).
    /// </summary>
    public sealed class AgentTools(string projectRoot)
    {
        private const int MaxReadChars = 200_000;
        private const int MaxOutputChars = 30_000;
        private const int MaxListEntries = 400;
        private const int MaxSearchResults = 200;
        private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(2);

        // Cartelle che non si elencano né si cercano: pesanti e quasi mai utili.
        private static readonly HashSet<string> SkippedDirs = new(StringComparer.OrdinalIgnoreCase)
        {
            ".git", "node_modules", "bin", "obj", ".vs", ".idea", "dist", "build", "out", "target", "__pycache__", ".venv", "venv"
        };

        public string Root { get; } = Path.GetFullPath(projectRoot);

        public const string PlanModeRefusal =
            "Errore: in modalità Piano non si possono modificare file né eseguire comandi. Proponi il piano; l'utente passerà ad Agisci per eseguirlo.";

        /// <summary>
        /// Modalità Piano: il modello vede solo gli strumenti di lettura e le istruzioni
        /// gli chiedono di proporre un piano; scritture e comandi vengono comunque rifiutati.
        /// </summary>
        public bool PlanMode { get; init; }

        /// <summary>Definizione degli strumenti disponibili nella modalità corrente (formato OpenAI "tools").</summary>
        public string Definitions => PlanMode ? $"[{ReadDefinitions}]" : $"[{ReadDefinitions},{WriteDefinitions}]";

        private const string ReadDefinitions = """
          {"type":"function","function":{"name":"read_file","description":"Legge un file di testo del progetto e ne restituisce il contenuto.","parameters":{"type":"object","properties":{"path":{"type":"string","description":"Percorso relativo alla cartella del progetto"}},"required":["path"]}}},
          {"type":"function","function":{"name":"list_files","description":"Elenca file e cartelle. Salta .git, node_modules, bin, obj e simili.","parameters":{"type":"object","properties":{"path":{"type":"string","description":"Cartella relativa al progetto; '.' per la radice"},"recursive":{"type":"boolean","description":"Se true elenca anche le sottocartelle"}},"required":["path"]}}},
          {"type":"function","function":{"name":"search_files","description":"Cerca un'espressione regolare (.NET) nei file di testo di una cartella. Restituisce percorso, riga e testo di ogni corrispondenza.","parameters":{"type":"object","properties":{"path":{"type":"string","description":"Cartella relativa al progetto"},"regex":{"type":"string","description":"Espressione regolare da cercare"},"file_pattern":{"type":"string","description":"Filtro sui nomi dei file, es. *.cs (facoltativo)"}},"required":["path","regex"]}}}
        """;

        private const string WriteDefinitions = """
          {"type":"function","function":{"name":"write_to_file","description":"Crea un file o lo sostituisce interamente con il contenuto dato. Crea le cartelle mancanti. Richiede l'approvazione dell'utente.","parameters":{"type":"object","properties":{"path":{"type":"string","description":"Percorso relativo al progetto"},"content":{"type":"string","description":"Contenuto completo del file"}},"required":["path","content"]}}},
          {"type":"function","function":{"name":"replace_in_file","description":"Sostituisce in un file un brano di testo esatto, che deve comparire una sola volta. Per modifiche mirate a file esistenti. Richiede l'approvazione dell'utente.","parameters":{"type":"object","properties":{"path":{"type":"string","description":"Percorso relativo al progetto"},"old_text":{"type":"string","description":"Testo esistente da sostituire, identico carattere per carattere"},"new_text":{"type":"string","description":"Nuovo testo"}},"required":["path","old_text","new_text"]}}},
          {"type":"function","function":{"name":"execute_command","description":"Esegue un comando PowerShell nella cartella del progetto e restituisce l'output (timeout 2 minuti). Richiede l'approvazione dell'utente.","parameters":{"type":"object","properties":{"command":{"type":"string","description":"Comando PowerShell"}},"required":["command"]}}}
        """;

        /// <summary>Istruzioni di sistema per il modello.</summary>
        public string SystemPrompt => $"""
            Sei Cline, un assistente di programmazione che lavora sul progetto dell'utente in un'app Windows.
            Cartella del progetto: {Root}
            Sistema: Windows; i comandi vengono eseguiti con PowerShell.
            Rispondi nella lingua dell'utente.

            {(PlanMode ? PlanInstructions : ActInstructions)}
            """;

        private const string PlanInstructions = """
            MODALITÀ PIANO. Non puoi modificare file né eseguire comandi: hai solo read_file, list_files e search_files.
            Esplora il progetto quanto serve per capire la richiesta, poi rispondi con un piano concreto:
            quali file cambierai, cosa farai in ciascuno e in che ordine, e i dubbi da chiarire.
            Se mancano informazioni, fai domande all'utente. Non scrivere il codice completo delle modifiche.
            Quando il piano va bene, l'utente passerà alla modalità Agisci per eseguirlo.
            """;

        private const string ActInstructions = """
            MODALITÀ AGISCI. Esegui la richiesta (se prima è stato concordato un piano, seguilo).
            Ora hai anche gli strumenti per modificare file ed eseguire comandi, anche se nei messaggi precedenti
            (in modalità Piano) non li avevi: usali tu, non dare istruzioni all'utente per farlo a mano.
            Usa gli strumenti per esplorare il codice prima di modificarlo: list_files e search_files per orientarti,
            read_file prima di cambiare un file. Per modifiche mirate preferisci replace_in_file a write_to_file.
            Scritture e comandi possono richiedere l'approvazione dell'utente: se ne rifiuta uno,
            non riprovare la stessa azione e chiedi come procedere.
            Quando hai finito, riassumi brevemente cosa hai fatto.
            """;

        /// <summary>
        /// Riepilogo da mostrare e categoria (per l'approvazione).
        /// Con <paramref name="live"/> le modifiche ai file vengono confrontate con il file su disco,
        /// per un diff con i numeri di riga veri; riaprendo una sessione salvata (live = false) il file
        /// è già cambiato, quindi si confrontano solo il testo vecchio e quello nuovo della richiesta.
        /// </summary>
        public ToolPreview Describe(ToolCall call, bool live = true)
        {
            var args = ParseArgs(call.Arguments);
            string path = Arg(args, "path") ?? ".";
            if (call.Name is "write_to_file" or "replace_in_file" && EditDiff(call.Name, path, args, live) is { } diff)
            {
                string verb = call.Name == "replace_in_file" ? "Modifica di "
                    : !live ? "Scrittura di "
                    : diff.NewFile ? "Creazione di " : "Sovrascrittura di ";
                return new ToolPreview("", verb + path, ToolCategory.Edit, Diff: diff);
            }
            return call.Name switch
            {
                "read_file" => new("", $"Lettura di {path}", ToolCategory.Read),
                "list_files" => new("", $"Elenco di {path}" + (ArgBool(args, "recursive") ? " (con sottocartelle)" : ""), ToolCategory.Read),
                "search_files" => new("", $"Ricerca di “{Arg(args, "regex")}” in {path}", ToolCategory.Read),
                "write_to_file" => new("", (File.Exists(SafePath(path) ?? "") ? "Sovrascrittura di " : "Creazione di ") + path, ToolCategory.Edit,
                    Arg(args, "content") ?? "", LanguageOf(path)),
                "replace_in_file" => new("", $"Modifica di {path}", ToolCategory.Edit,
                    $"--- testo attuale\n{Arg(args, "old_text")}\n+++ nuovo testo\n{Arg(args, "new_text")}", "diff"),
                "execute_command" => new("", "Esecuzione di un comando", ToolCategory.Command, Arg(args, "command") ?? "", "powershell"),
                // Uno strumento sconosciuto non fa nulla (restituisce un errore): niente da approvare.
                _ => new("", $"Strumento sconosciuto: {call.Name}", ToolCategory.Read),
            };
        }

        /// <summary>Diff della modifica richiesta; null se gli argomenti non bastano (si ripiega sul testo semplice).</summary>
        private FileDiff? EditDiff(string tool, string path, JsonElement args, bool live)
        {
            try
            {
                string? full = SafePath(path);
                string? current = live && full != null && File.Exists(full) ? File.ReadAllText(full).Replace("\r\n", "\n") : null;

                if (tool == "write_to_file")
                {
                    string content = Arg(args, "content") ?? "";
                    // Riaprendo una sessione il contenuto precedente non si conosce più: si mostra il file scritto,
                    // senza dire "nuovo file" (potrebbe essere stato una sovrascrittura).
                    bool newFile = live && current == null;
                    return new FileDiff(path, LineDiff.Compute(current ?? "", content), newFile);
                }

                string? oldText = Arg(args, "old_text")?.Replace("\r\n", "\n");
                string newText = (Arg(args, "new_text") ?? "").Replace("\r\n", "\n");
                if (string.IsNullOrEmpty(oldText)) return null;

                // Se il testo da sostituire compare una sola volta nel file, il diff è sul file intero
                // (numeri di riga veri e contesto intorno); altrimenti solo tra i due brani.
                if (current != null)
                {
                    int first = current.IndexOf(oldText, StringComparison.Ordinal);
                    if (first >= 0 && current.IndexOf(oldText, first + oldText.Length, StringComparison.Ordinal) < 0)
                    {
                        string updated = string.Concat(current.AsSpan(0, first), newText, current.AsSpan(first + oldText.Length));
                        return new FileDiff(path, LineDiff.Compute(current, updated), false);
                    }
                }
                return new FileDiff(path, LineDiff.Compute(oldText, newText), false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>Esegue lo strumento e restituisce il testo da rimandare al modello.</summary>
        public async Task<string> ExecuteAsync(ToolCall call, CancellationToken ct)
        {
            JsonElement args;
            try
            {
                args = ParseArgs(call.Arguments);
            }
            catch (JsonException)
            {
                return "Errore: argomenti non validi (JSON malformato).";
            }

            // In Piano gli strumenti di scrittura non sono offerti, ma un modello potrebbe chiamarli lo stesso.
            if (PlanMode && Describe(call).Category != ToolCategory.Read)
            {
                return PlanModeRefusal;
            }

            try
            {
                return call.Name switch
                {
                    "read_file" => ReadFile(Required(args, "path")),
                    "list_files" => ListFiles(Arg(args, "path") ?? ".", ArgBool(args, "recursive")),
                    "search_files" => SearchFiles(Arg(args, "path") ?? ".", Required(args, "regex"), Arg(args, "file_pattern")),
                    "write_to_file" => await WriteFileAsync(Required(args, "path"), Arg(args, "content") ?? "", ct),
                    "replace_in_file" => await ReplaceInFileAsync(Required(args, "path"), Required(args, "old_text"), Arg(args, "new_text") ?? "", ct),
                    "execute_command" => await ExecuteCommandAsync(Required(args, "command"), ct),
                    _ => $"Errore: lo strumento {call.Name} non esiste.",
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or RegexParseException or InvalidOperationException)
            {
                return $"Errore: {ex.Message}";
            }
        }

        // ---- Strumenti ----

        private string ReadFile(string path)
        {
            string full = RequirePath(path);
            if (!File.Exists(full)) return $"Errore: il file {path} non esiste.";

            // Contenuto così com'è, senza numeri di riga: altrimenti i modelli li ricopiano nelle modifiche.
            string text = File.ReadAllText(full).Replace("\r\n", "\n");
            if (text.Length > MaxReadChars)
            {
                return text[..MaxReadChars] + $"\n[File troncato ai primi {MaxReadChars} caratteri]";
            }
            return text.Length == 0 ? "(file vuoto)" : text;
        }

        private string ListFiles(string path, bool recursive)
        {
            string full = RequirePath(path);
            if (!Directory.Exists(full)) return $"Errore: la cartella {path} non esiste.";

            var entries = new List<string>();
            bool truncated = false;
            void Walk(string dir)
            {
                foreach (var sub in Directory.EnumerateDirectories(dir).Order(StringComparer.OrdinalIgnoreCase))
                {
                    if (entries.Count >= MaxListEntries) { truncated = true; return; }
                    if (SkippedDirs.Contains(Path.GetFileName(sub))) continue;
                    entries.Add(Relative(sub) + "/");
                    if (recursive) Walk(sub);
                }
                foreach (var file in Directory.EnumerateFiles(dir).Order(StringComparer.OrdinalIgnoreCase))
                {
                    if (entries.Count >= MaxListEntries) { truncated = true; return; }
                    entries.Add(Relative(file));
                }
            }
            Walk(full);

            if (entries.Count == 0) return "(cartella vuota)";
            string result = string.Join('\n', entries);
            return truncated ? result + $"\n[Elenco troncato a {MaxListEntries} voci]" : result;
        }

        private string SearchFiles(string path, string pattern, string? filePattern)
        {
            string full = RequirePath(path);
            if (!Directory.Exists(full)) return $"Errore: la cartella {path} non esiste.";
            var regex = new Regex(pattern, RegexOptions.Compiled, TimeSpan.FromSeconds(2));

            var results = new StringBuilder();
            int count = 0;
            foreach (var file in EnumerateFiles(full, string.IsNullOrWhiteSpace(filePattern) ? "*" : filePattern))
            {
                var info = new FileInfo(file);
                if (info.Length > 2_000_000) continue; // file troppo grandi, probabilmente non sorgenti

                int lineNumber = 0;
                foreach (var line in File.ReadLines(file))
                {
                    lineNumber++;
                    if (line.Contains('\0')) break; // file binario
                    if (!regex.IsMatch(line)) continue;

                    string shown = line.Trim();
                    if (shown.Length > 200) shown = shown[..200] + "…";
                    results.Append(Relative(file)).Append(':').Append(lineNumber).Append(": ").Append(shown).Append('\n');
                    if (++count >= MaxSearchResults)
                    {
                        results.Append($"[Risultati troncati a {MaxSearchResults}]");
                        return results.ToString();
                    }
                }
            }
            return count == 0 ? "Nessuna corrispondenza." : results.ToString();
        }

        private IEnumerable<string> EnumerateFiles(string dir, string pattern)
        {
            foreach (var file in Directory.EnumerateFiles(dir, pattern)) yield return file;
            foreach (var sub in Directory.EnumerateDirectories(dir))
            {
                if (SkippedDirs.Contains(Path.GetFileName(sub))) continue;
                foreach (var file in EnumerateFiles(sub, pattern)) yield return file;
            }
        }

        private async Task<string> WriteFileAsync(string path, string content, CancellationToken ct)
        {
            string full = RequirePath(path);
            bool existed = File.Exists(full);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            await File.WriteAllTextAsync(full, content, new UTF8Encoding(false), ct);
            int lines = content.Length == 0 ? 0 : content.Count(c => c == '\n') + 1;
            return $"{(existed ? "File sovrascritto" : "File creato")}: {path} ({lines} righe).";
        }

        private async Task<string> ReplaceInFileAsync(string path, string oldText, string newText, CancellationToken ct)
        {
            string full = RequirePath(path);
            if (!File.Exists(full)) return $"Errore: il file {path} non esiste.";

            string text = await File.ReadAllTextAsync(full, ct);
            // Il modello scrive "\n"; se il file usa "\r\n" si confronta con gli a capo del file.
            bool crlf = text.Contains("\r\n");
            if (crlf)
            {
                oldText = oldText.Replace("\r\n", "\n").Replace("\n", "\r\n");
                newText = newText.Replace("\r\n", "\n").Replace("\n", "\r\n");
            }

            int first = text.IndexOf(oldText, StringComparison.Ordinal);
            if (first < 0) return "Errore: il testo da sostituire non è stato trovato. Rileggi il file e riprova con il testo esatto.";
            if (text.IndexOf(oldText, first + oldText.Length, StringComparison.Ordinal) >= 0)
                return "Errore: il testo da sostituire compare più volte. Includi più righe di contesto per renderlo unico.";

            string updated = string.Concat(text.AsSpan(0, first), newText, text.AsSpan(first + oldText.Length));
            await File.WriteAllTextAsync(full, updated, new UTF8Encoding(false), ct);
            return $"Modifica applicata a {path}.";
        }

        private async Task<string> ExecuteCommandAsync(string command, CancellationToken ct)
        {
            var start = new ProcessStartInfo("powershell.exe")
            {
                WorkingDirectory = Root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            start.ArgumentList.Add("-NoProfile");
            start.ArgumentList.Add("-NonInteractive");
            start.ArgumentList.Add("-Command");
            // Output in UTF-8, così accenti e simboli arrivano intatti.
            start.ArgumentList.Add("[Console]::OutputEncoding = [Text.Encoding]::UTF8; " + command);

            using var process = Process.Start(start) ?? throw new InvalidOperationException("Impossibile avviare PowerShell.");
            var output = new StringBuilder();
            object gate = new();
            void Append(string? line)
            {
                if (line == null) return;
                lock (gate)
                {
                    if (output.Length < MaxOutputChars) output.Append(line).Append('\n');
                }
            }
            process.OutputDataReceived += (_, e) => Append(e.Data);
            process.ErrorDataReceived += (_, e) => Append(e.Data);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(CommandTimeout);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                if (ct.IsCancellationRequested) throw;
                return $"Comando interrotto dopo {CommandTimeout.TotalMinutes:0} minuti.\n{output}";
            }

            string text;
            lock (gate) text = output.ToString().TrimEnd();
            if (text.Length >= MaxOutputChars) text += $"\n[Output troncato a {MaxOutputChars} caratteri]";
            return $"Codice di uscita: {process.ExitCode}\n{(text.Length == 0 ? "(nessun output)" : text)}";
        }

        // ---- Utilità ----

        /// <summary>Percorso assoluto dentro il progetto, oppure null se esce dalla cartella.</summary>
        private string? SafePath(string path)
        {
            try
            {
                string full = Path.GetFullPath(Path.Combine(Root, path));
                string rootWithSep = Root.EndsWith(Path.DirectorySeparatorChar) ? Root : Root + Path.DirectorySeparatorChar;
                return full.Equals(Root, StringComparison.OrdinalIgnoreCase) || full.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase)
                    ? full
                    : null;
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return null;
            }
        }

        private string RequirePath(string path) =>
            SafePath(path) ?? throw new ArgumentException($"il percorso {path} è fuori dalla cartella del progetto.");

        private string Relative(string full) => Path.GetRelativePath(Root, full).Replace('\\', '/');

        private static JsonElement ParseArgs(string json)
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            return doc.RootElement.Clone();
        }

        private static string? Arg(JsonElement args, string name) =>
            args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        private static bool ArgBool(JsonElement args, string name) =>
            args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

        private static string Required(JsonElement args, string name) =>
            Arg(args, name) is { Length: > 0 } value ? value : throw new ArgumentException($"manca l'argomento \"{name}\".");

        private static string LanguageOf(string path) => Path.GetExtension(path).TrimStart('.').ToLowerInvariant() switch
        {
            "cs" => "csharp",
            "js" or "mjs" or "cjs" => "javascript",
            "ts" or "tsx" => "typescript",
            "py" => "python",
            "ps1" => "powershell",
            "md" => "markdown",
            "" => "testo",
            var ext => ext,
        };
    }
}
