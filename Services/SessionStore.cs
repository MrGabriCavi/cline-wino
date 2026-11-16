using System.Text.Json;
using System.Text.Json.Serialization;
using Windows.Storage;

namespace ClineWin
{
    /// <summary>Una conversazione salvata.</summary>
    public sealed class ChatSession
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Title { get; set; } = "";
        public DateTimeOffset Created { get; set; } = DateTimeOffset.Now;
        public DateTimeOffset Updated { get; set; } = DateTimeOffset.Now;
        public string Provider { get; set; } = ProviderCatalog.OpenRouter;
        public string Model { get; set; } = "openrouter/auto";
        public string ModelName { get; set; } = "";
        public string? ProjectPath { get; set; }
        /// <summary>"plan" (Piano) o "act" (Agisci).</summary>
        public string Mode { get; set; } = "act";
        public List<ChatMessage> Messages { get; set; } = [];
    }

    /// <summary>Voce dell'elenco nella barra laterale.</summary>
    public sealed record SessionSummary(string Id, string Title, DateTimeOffset Updated)
    {
        /// <summary>Il titolo: lo usa anche l'accessibilità come nome della voce dell'elenco.</summary>
        public override string ToString() => Title;

        /// <summary>"adesso", "5 min", "3 h", "ieri", "4 g", oppure la data.</summary>
        public string When
        {
            get
            {
                var age = DateTimeOffset.Now - Updated;
                if (age.TotalMinutes < 1) return "adesso";
                if (age.TotalHours < 1) return $"{(int)age.TotalMinutes} min";
                if (age.TotalDays < 1) return $"{(int)age.TotalHours} h";
                if (age.TotalDays < 2) return "ieri";
                if (age.TotalDays < 7) return $"{(int)age.TotalDays} g";
                return Updated.ToString("d MMM", System.Globalization.CultureInfo.GetCultureInfo("it-IT"));
            }
        }
    }

    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
    [JsonSerializable(typeof(ChatSession))]
    internal sealed partial class ChatSessionJsonContext : JsonSerializerContext;

    /// <summary>
    /// Cronologia delle sessioni: un file JSON per sessione nella cartella dati dell'app
    /// (LocalState\sessions del pacchetto, oppure %LOCALAPPDATA%\ClineWin\sessions senza pacchetto).
    /// </summary>
    public static class SessionStore
    {
        /// <summary>Lanciato dopo ogni salvataggio o eliminazione, per aggiornare l'elenco.</summary>
        public static event Action? Changed;

        public static string Folder { get; } = ResolveFolder();

        private static string ResolveFolder()
        {
            string root;
            try { root = ApplicationData.Current.LocalFolder.Path; }
            catch (InvalidOperationException) { root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClineWin"); }
            string folder = Path.Combine(root, "sessions");
            Directory.CreateDirectory(folder);
            return folder;
        }

        private static string FileOf(string id) => Path.Combine(Folder, id + ".json");

        public static async Task<IReadOnlyList<SessionSummary>> ListAsync()
        {
            var result = new List<SessionSummary>();
            foreach (var file in Directory.EnumerateFiles(Folder, "*.json"))
            {
                if (await ReadAsync(file) is { } session)
                {
                    result.Add(new SessionSummary(session.Id, session.Title, session.Updated));
                }
            }
            result.Sort((a, b) => b.Updated.CompareTo(a.Updated));
            return result;
        }

        public static Task<ChatSession?> LoadAsync(string id) => ReadAsync(FileOf(id));

        public static async Task SaveAsync(ChatSession session)
        {
            session.Updated = DateTimeOffset.Now;
            // Si scrive su un file temporaneo e poi lo si sostituisce, per non lasciare JSON a metà.
            string path = FileOf(session.Id);
            string temp = path + ".tmp";
            await using (var stream = File.Create(temp))
            {
                await JsonSerializer.SerializeAsync(stream, session, ChatSessionJsonContext.Default.ChatSession);
            }
            File.Move(temp, path, overwrite: true);
            Changed?.Invoke();
        }

        public static void Delete(string id)
        {
            File.Delete(FileOf(id));
            Changed?.Invoke();
        }

        private static async Task<ChatSession?> ReadAsync(string path)
        {
            try
            {
                await using var stream = File.OpenRead(path);
                return await JsonSerializer.DeserializeAsync(stream, ChatSessionJsonContext.Default.ChatSession);
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                // File illeggibile o rovinato: lo si salta.
                return null;
            }
        }

        /// <summary>Titolo dalla prima richiesta: prima riga, al massimo 60 caratteri.</summary>
        public static string TitleFrom(string prompt)
        {
            string line = prompt.Split('\n', 2)[0].Trim();
            return line.Length <= 60 ? line : line[..57].TrimEnd() + "…";
        }
    }
}
