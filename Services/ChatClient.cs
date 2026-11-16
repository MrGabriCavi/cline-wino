using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace ClineWin
{
    /// <summary>
    /// Un messaggio della conversazione (formato OpenAI): "system", "user", "assistant" o "tool".
    /// Un messaggio "assistant" può chiedere di usare degli strumenti (ToolCalls);
    /// il risultato di ciascuno torna come messaggio "tool" con lo stesso ToolCallId.
    /// </summary>
    public sealed record ChatMessage(string Role, string Content)
    {
        public List<ToolCall>? ToolCalls { get; init; }
        public string? ToolCallId { get; init; }
        /// <summary>File e immagini allegati a un messaggio "user" (vedi <see cref="MessageAttachment"/>).</summary>
        public List<MessageAttachment>? Attachments { get; init; }
    }

    /// <summary>
    /// Allegato di un messaggio. Kind "file": testo di un file (menzionato con @ o allegato),
    /// inviato al modello dopo il messaggio. Kind "image": immagine come data URL, inviata
    /// come parte "image_url" (serve un modello che veda le immagini).
    /// </summary>
    public sealed record MessageAttachment(string Kind, string Name, string? Text = null, string? DataUrl = null);

    /// <summary>Richiesta del modello di usare uno strumento; Arguments è un oggetto JSON.</summary>
    public sealed record ToolCall(string Id, string Name, string Arguments);

    /// <summary>Esito di un turno: chiamate agli strumenti richieste dal modello (eventualmente nessuna).</summary>
    public sealed class ChatTurn
    {
        public List<ToolCall> ToolCalls { get; } = [];
    }

    /// <summary>Modello del provider. ContextLength: memoria del modello in token, se il provider la dichiara.</summary>
    public sealed record ModelInfo(string Id, string Name, int? ContextLength = null)
    {
        public override string ToString() => Name;
    }

    /// <summary>Errore restituito dal provider, con un messaggio da mostrare all'utente.</summary>
    public sealed class ProviderException(string message) : Exception(message);

    /// <summary>
    /// Client per i provider con API compatibile con OpenAI (vedi <see cref="ProviderCatalog"/>).
    /// Il JSON viene scritto e letto a mano, senza serializzazione per riflessione,
    /// così il codice resta compatibile con il trimming.
    /// </summary>
    public static class ChatClient
    {
        private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

        /// <summary>Richiesta verso il provider, con autenticazione e intestazioni specifiche.</summary>
        private static HttpRequestMessage NewRequest(ProviderDefinition provider, HttpMethod method, string path, string? apiKey)
        {
            // Un provider personalizzato può indicare un indirizzo a parte per l'elenco dei modelli.
            var uri = path == "models" && provider.ModelsUrl is { Length: > 0 } modelsUrl
                ? new Uri(modelsUrl)
                : new Uri(new Uri(provider.BaseUrl), path);
            var request = new HttpRequestMessage(method, uri);
            if (!string.IsNullOrEmpty(apiKey))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            }

            switch (provider.Name)
            {
                case ProviderCatalog.OpenRouter:
                    // Intestazioni facoltative con cui OpenRouter identifica l'app.
                    request.Headers.Add("HTTP-Referer", "https://github.com/cline/cline");
                    request.Headers.Add("X-Title", "ClineWin");
                    break;
                case ProviderCatalog.Anthropic when !string.IsNullOrEmpty(apiKey):
                    // L'elenco dei modelli di Anthropic usa l'autenticazione nativa, non "Bearer".
                    request.Headers.Add("x-api-key", apiKey);
                    request.Headers.Add("anthropic-version", "2023-06-01");
                    break;
            }
            return request;
        }

        /// <summary>Elenco dei modelli del provider, ordinato per nome.</summary>
        public static async Task<IReadOnlyList<ModelInfo>> ListModelsAsync(ProviderDefinition provider, string? apiKey, CancellationToken ct = default)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(provider.IsLocal ? 5 : 20));

            using var request = NewRequest(provider, HttpMethod.Get, "models", apiKey);
            using var response = await Http.SendAsync(request, cts.Token);
            await EnsureSuccessAsync(provider, response, cts.Token);

            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cts.Token), cancellationToken: cts.Token);
            var models = new List<ModelInfo>();
            if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in data.EnumerateArray())
                {
                    string? id = item.TryGetProperty("id", out var i) ? i.GetString() : null;
                    if (string.IsNullOrEmpty(id)) continue;
                    // OpenRouter usa "name", Anthropic "display_name"; gli altri solo l'id.
                    string name = (item.TryGetProperty("name", out var n) ? n.GetString() : null)
                        ?? (item.TryGetProperty("display_name", out var d) ? d.GetString() : null)
                        ?? id;
                    // OpenRouter dichiara "context_length", altri "context_window"; molti nessuno dei due.
                    int? context = null;
                    foreach (var field in new[] { "context_length", "context_window", "max_context_length" })
                    {
                        if (item.TryGetProperty(field, out var c) && c.ValueKind == JsonValueKind.Number && c.TryGetInt32(out int v) && v > 0) { context = v; break; }
                    }
                    models.Add(new ModelInfo(id, name.Length > 0 ? name : id, context));
                }
            }
            models.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
            return models;
        }

        /// <summary>
        /// Invia la conversazione e restituisce il testo della risposta pezzo per pezzo (streaming SSE).
        /// Se si passano gli strumenti (JSON con l'array "tools"), le chiamate richieste dal modello
        /// vengono ricomposte dai frammenti dello stream e messe in <paramref name="turn"/>.
        /// </summary>
        public static async IAsyncEnumerable<string> StreamChatAsync(
            ProviderDefinition provider,
            string? apiKey,
            string model,
            IReadOnlyList<ChatMessage> messages,
            string? toolsJson,
            ChatTurn turn,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            using var request = NewRequest(provider, HttpMethod.Post, "chat/completions", apiKey);
            request.Content = new StringContent(BuildChatBody(model, messages, toolsJson), Encoding.UTF8, "application/json");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            await EnsureSuccessAsync(provider, response, ct);

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            // Frammenti delle chiamate agli strumenti, per indice: id, nome, argomenti (a pezzi).
            var partial = new SortedDictionary<int, (string Id, string Name, StringBuilder Args)>();
            try
            {
                while (await reader.ReadLineAsync(ct) is { } line)
                {
                    // Le righe che iniziano con ":" sono commenti (es. ": OPENROUTER PROCESSING").
                    if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
                    string data = line[5..].Trim();
                    if (data == "[DONE]") yield break;

                    string? text = ParseChunk(data, partial);
                    if (!string.IsNullOrEmpty(text)) yield return text;
                }
            }
            finally
            {
                foreach (var (id, name, args) in partial.Values)
                {
                    if (name.Length == 0) continue;
                    string arguments = args.Length == 0 ? "{}" : args.ToString();
                    turn.ToolCalls.Add(new ToolCall(id.Length > 0 ? id : "call_" + Guid.NewGuid().ToString("N")[..12], name, arguments));
                }
            }
        }

        private static string BuildChatBody(string model, IReadOnlyList<ChatMessage> messages, string? toolsJson)
        {
            using var buffer = new MemoryStream();
            using (var json = new Utf8JsonWriter(buffer))
            {
                json.WriteStartObject();
                json.WriteString("model", model);
                json.WriteBoolean("stream", true);
                json.WriteStartArray("messages");
                foreach (var m in messages)
                {
                    json.WriteStartObject();
                    json.WriteString("role", m.Role);
                    WriteContent(json, m);
                    if (m.ToolCalls is { Count: > 0 } calls)
                    {
                        json.WriteStartArray("tool_calls");
                        foreach (var call in calls)
                        {
                            json.WriteStartObject();
                            json.WriteString("id", call.Id);
                            json.WriteString("type", "function");
                            json.WriteStartObject("function");
                            json.WriteString("name", call.Name);
                            json.WriteString("arguments", call.Arguments);
                            json.WriteEndObject();
                            json.WriteEndObject();
                        }
                        json.WriteEndArray();
                    }
                    if (m.ToolCallId != null) json.WriteString("tool_call_id", m.ToolCallId);
                    json.WriteEndObject();
                }
                json.WriteEndArray();

                if (toolsJson != null)
                {
                    json.WritePropertyName("tools");
                    json.WriteRawValue(toolsJson);
                }
                json.WriteEndObject();
            }
            return Encoding.UTF8.GetString(buffer.ToArray());
        }

        /// <summary>
        /// Contenuto del messaggio. Senza allegati è una stringa; con allegati, il testo dei file
        /// segue il messaggio dentro tag &lt;file&gt; e le immagini diventano parti "image_url"
        /// (formato multimodale di OpenAI, accettato dai provider compatibili).
        /// </summary>
        private static void WriteContent(Utf8JsonWriter json, ChatMessage m)
        {
            if (m.Attachments is not { Count: > 0 } attachments)
            {
                json.WriteString("content", m.Content);
                return;
            }

            var text = new StringBuilder(m.Content);
            foreach (var file in attachments.Where(a => a.Kind == "file"))
            {
                text.Append("\n\n<file path=\"").Append(file.Name).Append("\">\n").Append(file.Text).Append("\n</file>");
            }

            var images = attachments.Where(a => a.Kind == "image" && a.DataUrl != null).ToList();
            if (images.Count == 0)
            {
                json.WriteString("content", text.ToString());
                return;
            }

            json.WriteStartArray("content");
            json.WriteStartObject();
            json.WriteString("type", "text");
            json.WriteString("text", text.Length > 0 ? text.ToString() : "(immagine allegata)");
            json.WriteEndObject();
            foreach (var image in images)
            {
                json.WriteStartObject();
                json.WriteString("type", "image_url");
                json.WriteStartObject("image_url");
                json.WriteString("url", image.DataUrl);
                json.WriteEndObject();
                json.WriteEndObject();
            }
            json.WriteEndArray();
        }

        /// <summary>
        /// Estrae il testo da un evento dello stream e accumula i frammenti delle chiamate agli strumenti;
        /// lancia se l'evento contiene un errore.
        /// </summary>
        private static string? ParseChunk(string data, SortedDictionary<int, (string Id, string Name, StringBuilder Args)> partial)
        {
            using var doc = JsonDocument.Parse(data);
            var root = doc.RootElement;

            // Un errore a metà stream arriva come evento con il campo "error".
            if (root.TryGetProperty("error", out var error))
            {
                throw new ProviderException(ErrorMessage(error));
            }

            if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0) return null;
            var choice = choices[0];
            if (!choice.TryGetProperty("delta", out var delta)) return null;

            if (delta.TryGetProperty("tool_calls", out var calls) && calls.ValueKind == JsonValueKind.Array)
            {
                foreach (var call in calls.EnumerateArray())
                {
                    int index = call.TryGetProperty("index", out var i) && i.ValueKind == JsonValueKind.Number ? i.GetInt32() : partial.Count;
                    var entry = partial.TryGetValue(index, out var existing) ? existing : ("", "", new StringBuilder());
                    if (call.TryGetProperty("id", out var id) && id.GetString() is { Length: > 0 } idText) entry.Item1 = idText;
                    if (call.TryGetProperty("function", out var fn))
                    {
                        if (fn.TryGetProperty("name", out var name) && name.GetString() is { Length: > 0 } nameText) entry.Item2 = nameText;
                        if (fn.TryGetProperty("arguments", out var args) && args.GetString() is { } argsText) entry.Item3.Append(argsText);
                    }
                    partial[index] = entry;
                }
            }

            if (delta.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
            {
                return content.GetString();
            }
            return null;
        }

        private static async Task EnsureSuccessAsync(ProviderDefinition provider, HttpResponseMessage response, CancellationToken ct)
        {
            if (response.IsSuccessStatusCode) return;

            string body = await response.Content.ReadAsStringAsync(ct);
            string? detail = null;
            try
            {
                using var doc = JsonDocument.Parse(body);
                // Gemini a volte risponde con un array di errori: si prende il primo.
                var root = doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0
                    ? doc.RootElement[0]
                    : doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out var error)) detail = ErrorMessage(error);
            }
            catch (JsonException)
            {
                // Corpo non JSON: si usa solo il codice HTTP.
            }

            string prefix = (int)response.StatusCode switch
            {
                401 or 403 => $"Chiave API di {provider.Name} non valida, revocata o senza permessi.",
                402 => $"Credito {provider.Name} esaurito.",
                404 => $"{provider.Name}: risorsa o modello non trovato.",
                429 => "Troppe richieste o limite raggiunto: riprova tra poco.",
                _ => $"{provider.Name} ha risposto con l'errore {(int)response.StatusCode}.",
            };
            throw new ProviderException(detail is { Length: > 0 } ? $"{prefix} {detail}" : prefix);
        }

        private static string ErrorMessage(JsonElement error) =>
            error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out var m) && m.GetString() is { } s
                ? s
                : error.ValueKind == JsonValueKind.String ? error.GetString() ?? "" : error.ToString();
    }
}
