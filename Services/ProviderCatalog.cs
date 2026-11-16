namespace ClineWin
{
    /// <summary>
    /// Un provider di modelli raggiungibile con l'API compatibile con OpenAI
    /// (POST chat/completions in streaming, GET models).
    /// </summary>
    public sealed record ProviderDefinition(
        string Name,
        string BaseUrl,
        bool NeedsKey,
        bool Popular,
        string? KeyUrl = null,
        string? DefaultModel = null,
        string? DefaultModelName = null,
        bool Local = false,
        bool Custom = false,
        string? ModelsUrl = null)
    {
        /// <summary>Provider in esecuzione su questo computer (Ollama, LM Studio).</summary>
        public bool IsLocal => Local;

        public string AuthLabel => Custom ? "Personalizzato" : Local ? "Locale" : "Chiave API";
        public string AuthGlyph => Custom ? "" : Local ? "" : "";

        public override string ToString() => Name;
    }

    /// <summary>
    /// Provider collegati: quelli predefiniti più quelli personalizzati dell'utente.
    /// Tutti usano l'API compatibile con OpenAI, quindi bastano indirizzo e chiave:
    /// lo stesso client (<see cref="ChatClient"/>) li serve tutti.
    /// Gli URL delle chiavi vengono da lib/provider-key-urls.ts dell'app originale.
    /// </summary>
    public static class ProviderCatalog
    {
        public const string OpenRouter = "OpenRouter";
        public const string Anthropic = "Anthropic";

        public static IReadOnlyList<ProviderDefinition> BuiltIn { get; } = (ProviderDefinition[])
        [
            new(OpenRouter, "https://openrouter.ai/api/v1/", true, true, "https://openrouter.ai/settings/keys", "openrouter/auto", "Auto Router"),
            new(Anthropic, "https://api.anthropic.com/v1/", true, true, "https://console.anthropic.com/settings/keys"),
            new("OpenAI", "https://api.openai.com/v1/", true, true, "https://platform.openai.com/api-keys"),
            new("Google Gemini", "https://generativelanguage.googleapis.com/v1beta/openai/", true, true, "https://aistudio.google.com/apikey"),
            new("DeepSeek", "https://api.deepseek.com/v1/", true, false, "https://platform.deepseek.com/api_keys"),
            new("Groq", "https://api.groq.com/openai/v1/", true, false, "https://console.groq.com/keys"),
            new("Mistral", "https://api.mistral.ai/v1/", true, false, "https://console.mistral.ai/api-keys"),
            new("xAI", "https://api.x.ai/v1/", true, false, "https://console.x.ai"),
            new("Ollama", "http://localhost:11434/v1/", false, false, Local: true),
            new("LM Studio", "http://localhost:1234/v1/", false, false, Local: true),
        ];

        /// <summary>Predefiniti e personalizzati (la chiave dei personalizzati è facoltativa).</summary>
        public static IReadOnlyList<ProviderDefinition> All =>
            BuiltIn.Concat(CustomProviders.Load().Select(c => new ProviderDefinition(
                c.Name, c.BaseUrl, NeedsKey: false, Popular: false, Custom: true, ModelsUrl: c.ModelsUrl))).ToArray();

        public static ProviderDefinition? Find(string? name) => All.FirstOrDefault(p => p.Name == name);

        public static ProviderDefinition Default => BuiltIn[0];
    }
}
