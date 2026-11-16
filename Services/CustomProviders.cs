using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClineWin
{
    /// <summary>Provider aggiunto dall'utente: un servizio compatibile con OpenAI a un indirizzo a scelta.</summary>
    public sealed record CustomProvider(string Name, string BaseUrl, string? ModelsUrl);

    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(List<CustomProvider>))]
    internal sealed partial class CustomProviderJsonContext : JsonSerializerContext;

    /// <summary>
    /// Provider personalizzati, salvati nelle preferenze come JSON. La chiave API (facoltativa)
    /// sta in Gestione credenziali di Windows con il nome del provider, come per gli altri.
    /// </summary>
    public static class CustomProviders
    {
        private const string PreferenceKey = "customProviders";

        public static event Action? Changed;

        public static List<CustomProvider> Load()
        {
            string json = AppPreferences.Get(PreferenceKey, "[]");
            try
            {
                return JsonSerializer.Deserialize(json, CustomProviderJsonContext.Default.ListCustomProvider) ?? [];
            }
            catch (JsonException)
            {
                return [];
            }
        }

        public static void Add(CustomProvider provider)
        {
            var list = Load();
            list.RemoveAll(p => p.Name.Equals(provider.Name, StringComparison.OrdinalIgnoreCase));
            list.Add(provider);
            Save(list);
        }

        /// <summary>Rimuove il provider e la sua chiave.</summary>
        public static void Remove(string name)
        {
            var list = Load();
            list.RemoveAll(p => p.Name == name);
            Save(list);
            CredentialStore.RemoveApiKey(name);
        }

        private static void Save(List<CustomProvider> list)
        {
            AppPreferences.Set(PreferenceKey, JsonSerializer.Serialize(list, CustomProviderJsonContext.Default.ListCustomProvider));
            Changed?.Invoke();
        }

        /// <summary>
        /// Controlla i dati del modulo. Restituisce il messaggio d'errore, oppure null se va tutto bene;
        /// in <paramref name="normalizedUrl"/> l'URL di base con la barra finale.
        /// </summary>
        public static string? Validate(string name, string baseUrl, string? modelsUrl, out string normalizedUrl)
        {
            normalizedUrl = "";
            name = name.Trim();
            if (name.Length == 0) return "Inserisci un nome.";
            if (name.Length > 40) return "Il nome può avere al massimo 40 caratteri.";
            if (ProviderCatalog.BuiltIn.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                return $"“{name}” è già un provider predefinito: scegli un altro nome.";
            if (Load().Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                return $"Esiste già un provider personalizzato chiamato “{name}”.";

            if (!IsHttpUrl(baseUrl.Trim(), out var uri)) return "L'URL di base deve essere un indirizzo http:// o https:// completo.";
            normalizedUrl = uri!.AbsoluteUri.EndsWith('/') ? uri.AbsoluteUri : uri.AbsoluteUri + "/";

            if (!string.IsNullOrWhiteSpace(modelsUrl) && !IsHttpUrl(modelsUrl.Trim(), out _))
                return "L'URL dell'elenco modelli deve essere un indirizzo http:// o https:// completo.";
            return null;
        }

        /// <summary>True se l'indirizzo è http (non https) e non punta a questo computer: la chiave viaggerebbe in chiaro.</summary>
        public static bool IsInsecureRemote(string url) =>
            Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttp && !uri.IsLoopback;

        private static bool IsHttpUrl(string text, out Uri? uri) =>
            Uri.TryCreate(text, UriKind.Absolute, out uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }
}
