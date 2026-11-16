using System.Text;
using Microsoft.UI.Dispatching;

namespace ClineWin
{
    /// <summary>
    /// Esegue le attività pianificate mentre l'app è aperta. Ogni 30 secondi controlla quelle
    /// scadute (anche quelle saltate ad app chiusa: partono una volta all'avvio) e le esegue una alla volta.
    /// Ogni esecuzione diventa una sessione nella cronologia, da rileggere quando si vuole.
    /// </summary>
    public static class Scheduler
    {
        private const int MaxSteps = 25;
        private static readonly TimeSpan RunTimeout = TimeSpan.FromMinutes(15);

        public const string NotApprovedResult =
            "Esecuzione pianificata, senza nessuno ad approvare: questa azione richiede l'approvazione e non è stata eseguita. "
            + "Continua senza di essa e segnalalo nel rapporto finale.";

        private static DispatcherQueueTimer? s_timer;
        private static bool s_running;

        /// <summary>Id dell'attività in esecuzione, o null.</summary>
        public static string? RunningTaskId { get; private set; }

        /// <summary>Lanciato quando un'esecuzione inizia o finisce (per aggiornare la pagina).</summary>
        public static event Action? StateChanged;

        public static void Start(DispatcherQueue queue)
        {
            if (s_timer != null) return;
            s_timer = queue.CreateTimer();
            s_timer.Interval = TimeSpan.FromSeconds(30);
            s_timer.Tick += async (_, _) => await RunDueAsync();
            s_timer.Start();
            // Un primo controllo poco dopo l'avvio, per recuperare le esecuzioni saltate.
            queue.TryEnqueue(async () => await RunDueAsync());
        }

        private static async Task RunDueAsync()
        {
            if (s_running) return;
            var now = DateTimeOffset.Now;
            var due = ScheduledTaskStore.Load().Where(t => t.NextRun is { } next && next <= now).ToList();
            foreach (var task in due)
            {
                await RunAsync(task.Id);
            }
        }

        /// <summary>Esegue subito l'attività (anche fuori orario, per "Esegui ora"). Restituisce false se ce n'è già una in corso.</summary>
        public static async Task<bool> RunAsync(string taskId)
        {
            if (s_running) return false;
            var task = ScheduledTaskStore.Load().FirstOrDefault(t => t.Id == taskId);
            if (task == null) return false;

            s_running = true;
            RunningTaskId = task.Id;
            StateChanged?.Invoke();
            string status;
            string? sessionId = null;
            try
            {
                (status, sessionId) = await ExecuteAsync(task);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                status = "Errore: " + ex.Message;
            }
            finally
            {
                s_running = false;
                RunningTaskId = null;
            }

            // Si rilegge l'elenco: nel frattempo l'attività potrebbe essere stata modificata o eliminata.
            var tasks = ScheduledTaskStore.Load();
            if (tasks.FirstOrDefault(t => t.Id == task.Id) is { } saved)
            {
                saved.LastRun = DateTimeOffset.Now;
                saved.Anchor = saved.LastRun.Value;
                saved.LastStatus = status;
                saved.LastSessionId = sessionId;
                ScheduledTaskStore.Save(tasks);
            }
            StateChanged?.Invoke();
            return true;
        }

        /// <summary>Il ciclo dell'agente senza interfaccia: stessi strumenti, approvazioni solo automatiche.</summary>
        private static async Task<(string Status, string? SessionId)> ExecuteAsync(ScheduledTask task)
        {
            var provider = ProviderCatalog.Find(task.Provider);
            if (provider == null) return ($"Provider “{task.Provider}” non disponibile", null);
            string? key = CredentialStore.GetApiKey(provider.Name);
            if (provider.NeedsKey && key == null) return ($"Manca la chiave API di {provider.Name}", null);
            if (string.IsNullOrWhiteSpace(task.Model)) return ("Nessun modello scelto", null);

            AgentTools? tools = task.ProjectPath is { } root && Directory.Exists(root) ? new AgentTools(root) : null;
            if (task.ProjectPath != null && tools == null) return ("La cartella del progetto non esiste più", null);

            var session = new ChatSession
            {
                Title = $"⏱ {task.Name}",
                Provider = provider.Name,
                Model = task.Model,
                ModelName = task.Model,
                ProjectPath = task.ProjectPath,
                Mode = "act",
            };
            var history = session.Messages;
            history.Add(new ChatMessage("user", task.Prompt));
            await SessionStore.SaveAsync(session);

            using var cts = new CancellationTokenSource(RunTimeout);
            int notApproved = 0;
            string status = "Completata";
            try
            {
                for (int step = 0; ; step++)
                {
                    if (step >= MaxSteps) { status = $"Fermata dopo {MaxSteps} passi"; break; }

                    var request = new List<ChatMessage>();
                    if (tools != null) request.Add(new ChatMessage("system", tools.SystemPrompt));
                    request.AddRange(history);

                    var turn = new ChatTurn();
                    var reply = new StringBuilder();
                    await foreach (var piece in ChatClient.StreamChatAsync(provider, key, task.Model, request, tools?.Definitions, turn, cts.Token))
                    {
                        reply.Append(piece);
                    }
                    history.Add(new ChatMessage("assistant", reply.ToString()) { ToolCalls = turn.ToolCalls.Count > 0 ? turn.ToolCalls : null });
                    if (turn.ToolCalls.Count == 0 || tools == null) break;

                    foreach (var call in turn.ToolCalls)
                    {
                        var category = tools.Describe(call, live: false).Category;
                        string result;
                        if (AutoApproval.IsEnabled(category))
                        {
                            result = await tools.ExecuteAsync(call, cts.Token);
                        }
                        else
                        {
                            result = NotApprovedResult;
                            notApproved++;
                        }
                        history.Add(new ChatMessage("tool", result) { ToolCallId = call.Id });
                    }
                    await SessionStore.SaveAsync(session);
                }
            }
            catch (OperationCanceledException)
            {
                status = $"Interrotta dopo {RunTimeout.TotalMinutes:0} minuti";
            }
            catch (ProviderException ex)
            {
                status = "Errore: " + ex.Message;
            }
            catch (HttpRequestException ex)
            {
                status = $"Connessione a {provider.Name} non riuscita: {ex.Message}";
            }

            // Ogni richiesta di strumento deve avere una risposta, come nelle sessioni normali.
            if (history.FindLastIndex(m => m.Role == "assistant") is var last and >= 0 && history[last].ToolCalls is { } calls)
            {
                var answered = history.Skip(last + 1).Select(m => m.ToolCallId).ToHashSet();
                foreach (var call in calls.Where(c => !answered.Contains(c.Id)))
                {
                    history.Add(new ChatMessage("tool", ToolCallView.InterruptedResult) { ToolCallId = call.Id });
                }
            }
            await SessionStore.SaveAsync(session);

            if (status == "Completata" && notApproved > 0) status = $"Completata, {notApproved} azioni non approvate";
            return (status, session.Id);
        }
    }
}
