namespace ClineWin
{
    /// <summary>
    /// Elenco dei file del progetto per i suggerimenti di "@": percorsi relativi, saltando le
    /// cartelle pesanti (come gli strumenti dell'agente). Si ricostruisce al massimo ogni 30 secondi.
    /// </summary>
    public static class FileIndex
    {
        private const int MaxFiles = 20_000;
        private static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(30);

        private static readonly HashSet<string> SkippedDirs = new(StringComparer.OrdinalIgnoreCase)
        {
            ".git", "node_modules", "bin", "obj", ".vs", ".idea", "dist", "build", "out", "target", "__pycache__", ".venv", "venv"
        };

        private static string? s_root;
        private static List<string> s_files = [];
        private static DateTime s_builtAt;

        public static async Task<IReadOnlyList<string>> GetAsync(string root)
        {
            if (s_root == root && DateTime.UtcNow - s_builtAt < MaxAge) return s_files;
            var files = await Task.Run(() => Build(root));
            s_root = root;
            s_files = files;
            s_builtAt = DateTime.UtcNow;
            return files;
        }

        private static List<string> Build(string root)
        {
            var result = new List<string>();
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0 && result.Count < MaxFiles)
            {
                string dir = pending.Pop();
                try
                {
                    foreach (var file in Directory.EnumerateFiles(dir))
                    {
                        result.Add(Path.GetRelativePath(root, file).Replace('\\', '/'));
                        if (result.Count >= MaxFiles) break;
                    }
                    foreach (var sub in Directory.EnumerateDirectories(dir))
                    {
                        if (!SkippedDirs.Contains(Path.GetFileName(sub))) pending.Push(sub);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Cartella non leggibile: la si salta.
                }
            }
            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        /// <summary>
        /// I file che corrispondono alla ricerca, i migliori prima: nome che inizia con il testo,
        /// poi nome che lo contiene, poi percorso che lo contiene; a parità, i percorsi più corti.
        /// </summary>
        public static List<string> Search(IReadOnlyList<string> files, string query, int max = 12)
        {
            if (query.Length == 0) return files.Take(max).ToList();
            return files
                .Select(path =>
                {
                    string name = path[(path.LastIndexOf('/') + 1)..];
                    int rank = name.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 0
                        : name.Contains(query, StringComparison.OrdinalIgnoreCase) ? 1
                        : path.Contains(query, StringComparison.OrdinalIgnoreCase) ? 2
                        : -1;
                    return (path, rank);
                })
                .Where(x => x.rank >= 0)
                .OrderBy(x => x.rank).ThenBy(x => x.path.Length)
                .Take(max)
                .Select(x => x.path)
                .ToList();
        }
    }
}
