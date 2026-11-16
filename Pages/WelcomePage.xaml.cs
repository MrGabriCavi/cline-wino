using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;
using Windows.System;

namespace ClineWin
{
    /// <summary>
    /// Schermata della sessione: robot di benvenuto, scelta del progetto e composer.
    /// Dal primo messaggio mostra la conversazione con OpenRouter; con un progetto aperto
    /// Cline lavora da agente (vedi <see cref="AgentTools"/>), chiedendo l'approvazione
    /// per scritture e comandi.
    /// </summary>
    public sealed partial class WelcomePage : Page
    {
        // Spostamento massimo degli occhi del robot, in pixel.
        private const double EyeMaxX = 5;
        private const double EyeMaxY = 4;

        public WelcomePage()
        {
            InitializeComponent();
            SelectProvider(PreferredProvider());
            AutoApproveText.Text = AutoApproval.Summary();
            AutoApproval.Changed += () => AutoApproveText.Text = AutoApproval.Summary();
            SetMode(AppPreferences.Get("chat.mode", "act") == "plan", announce: false);
        }

        // ---- Modalità Piano / Agisci ----

        private bool _planMode;

        private void Mode_Click(object sender, RoutedEventArgs e)
        {
            SetMode(ReferenceEquals(sender, PlanModeButton), announce: true);
        }

        /// <summary>
        /// Cambia modalità. In una conversazione già iniziata lascia una riga che segna il passaggio;
        /// la modalità vale dal messaggio successivo (un lavoro in corso finisce nella modalità in cui è partito).
        /// </summary>
        private void SetMode(bool plan, bool announce)
        {
            bool changed = plan != _planMode;
            _planMode = plan;
            PlanModeButton.IsChecked = plan;
            ActModeButton.IsChecked = !plan;
            PromptBox.PlaceholderText = plan
                ? "Descrivi cosa vuoi fare: Cline esplora il progetto e propone un piano."
                : "Chiedi una modifica, @menziona file, cita #PR o esegui /comandi.";
            AppPreferences.Set("chat.mode", plan ? "plan" : "act");
            if (_session != null) _session.Mode = plan ? "plan" : "act";

            if (announce && changed && MessagesPanel.Children.Count > 0)
            {
                RemoveActSuggestion();
                AddNote(plan ? "Modalità Piano: Cline propone, senza modificare." : "Modalità Agisci: Cline può modificare file ed eseguire comandi.", error: false);
            }
        }

        private Button? _actSuggestion;

        /// <summary>Dopo un piano: pulsante per passare ad Agisci e farlo eseguire.</summary>
        private void AddActSuggestion()
        {
            RemoveActSuggestion();
            _actSuggestion = new Button
            {
                Content = "Passa ad Agisci ed esegui il piano",
                Style = (Style)Application.Current.Resources["AccentButtonStyle"],
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            _actSuggestion.Click += (_, _) =>
            {
                SetMode(false, announce: true);
                // Inviato direttamente, senza toccare quello che l'utente sta scrivendo nel composer.
                _ = SendMessageAsync("Il piano va bene. Ora sei in modalità Agisci: eseguilo tu usando gli strumenti.", fromComposer: false, []);
            };
            MessagesPanel.Children.Add(_actSuggestion);
        }

        private void RemoveActSuggestion()
        {
            if (_actSuggestion != null) MessagesPanel.Children.Remove(_actSuggestion);
            _actSuggestion = null;
        }

        // ---- Approvazione automatica ----

        private void AutoApproveFlyout_Opening(object sender, object e)
        {
            AutoReadItem.IsChecked = AutoApproval.IsEnabled(ToolCategory.Read);
            AutoEditItem.IsChecked = AutoApproval.IsEnabled(ToolCategory.Edit);
            AutoCommandItem.IsChecked = AutoApproval.IsEnabled(ToolCategory.Command);
        }

        private void AutoApproveItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleMenuFlyoutItem { Tag: string tag } item && Enum.TryParse<ToolCategory>(tag, out var category))
            {
                AutoApproval.Set(category, item.IsChecked);
            }
        }

        // ---- Progetto ----

        private async void PickProject_Click(object sender, RoutedEventArgs e) => await PickProjectAsync();

        /// <summary>Apre la scelta cartella e mostra nome progetto e branch git.</summary>
        public async Task PickProjectAsync()
        {
            var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
            picker.FileTypeFilter.Add("*");

            // Nelle app WinUI 3 il picker va collegato alla finestra.
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var folder = await picker.PickSingleFolderAsync();
            if (folder == null) return;
            ShowProject(folder.Path);
        }

        /// <summary>Mostra nome del progetto e branch git; il percorso viene salvato con la sessione.</summary>
        private void ShowProject(string path)
        {
            _projectPath = path;
            string name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
            ProjectText.Text = name;
            App.MainWindow?.SetSubtitle(name);

            string? branch = ReadGitBranch(path);
            BranchText.Text = branch ?? "";
            BranchButton.Visibility = branch != null ? Visibility.Visible : Visibility.Collapsed;
            WorktreePanel.Visibility = branch != null ? Visibility.Visible : Visibility.Collapsed;
            _ = RefreshGitAsync();
        }

        private void ClearProject()
        {
            _projectPath = null;
            ProjectText.Text = "Scegli progetto";
            App.MainWindow?.SetSubtitle("");
            BranchButton.Visibility = WorktreePanel.Visibility = Visibility.Collapsed;
            _ = RefreshGitAsync(); // senza progetto: nasconde chip e pannello git
        }

        /// <summary>Legge il branch corrente da .git/HEAD; null se non è un repository git.</summary>
        private static string? ReadGitBranch(string folderPath)
        {
            try
            {
                string head = Path.Combine(folderPath, ".git", "HEAD");
                if (!File.Exists(head)) return null;

                string content = File.ReadAllText(head).Trim();
                const string prefix = "ref: refs/heads/";
                if (content.StartsWith(prefix, StringComparison.Ordinal)) return content[prefix.Length..];

                // HEAD staccato: mostra l'hash abbreviato.
                return content.Length >= 7 ? content[..7] : content;
            }
            catch (IOException)
            {
                return null;
            }
        }

        // ---- Composer ----

        private void PromptBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateSendEnabled();
            UpdateSuggestions();
        }

        private void UpdateSendEnabled()
        {
            SendButton.IsEnabled = _streaming != null || !string.IsNullOrWhiteSpace(PromptBox.Text) || _attachments.Count > 0;
        }

        private void PromptBox_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
        {
            // Con i suggerimenti aperti, frecce, Invio/Tab ed Esc agiscono sull'elenco.
            if (HandleSuggestionKey(e.Key))
            {
                e.Handled = true;
                return;
            }

            // Invio invia, Maiusc+Invio va a capo (come nell'app originale).
            if (e.Key != VirtualKey.Enter) return;
            var shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);
            if (shift.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down)) return;

            e.Handled = true;
            Send();
        }

        private void Send_Click(object sender, RoutedEventArgs e)
        {
            // Durante la risposta il pulsante diventa "Interrompi".
            if (_streaming != null) _streaming.Cancel();
            else Send();
        }

        /// <summary>
        /// Invio dal composer: un comando "/", un messaggio in coda (se l'agente sta lavorando)
        /// o un messaggio da inviare subito, con gli allegati e i file menzionati con "@".
        /// </summary>
        private void Send()
        {
            string prompt = PromptBox.Text.Trim();
            if (prompt.Length == 0 && _attachments.Count == 0) return;
            if (TryRunCommand(prompt)) return;

            if (_streaming != null)
            {
                Enqueue(prompt, CollectAttachments(prompt));
                ClearComposer();
                return;
            }
            _ = SendMessageAsync(prompt, fromComposer: true);
        }

        private async Task SendMessageAsync(string prompt, bool fromComposer, List<MessageAttachment>? queuedAttachments = null)
        {
            if (_streaming != null) return;
            ChatBar.IsOpen = false;

            var provider = _provider;
            // La chiave si usa se c'è (per i personalizzati è facoltativa); è obbligatoria solo per chi la richiede.
            string? apiKey = CredentialStore.GetApiKey(provider.Name);
            if (provider.NeedsKey && apiKey == null)
            {
                ShowBar(InfoBarSeverity.Warning, $"Serve una chiave API di {provider.Name}. Aggiungila in Impostazioni › BYOK.", ("Apri BYOK", "settings/byok"));
                return;
            }
            if (_modelId is not { } modelId)
            {
                ShowBar(InfoBarSeverity.Informational, $"Scegli un modello di {provider.Name} dal menu del modello, sotto il messaggio.", null);
                return;
            }

            // Gli allegati si raccolgono solo ora, dopo i controlli: se manca la chiave il messaggio resta nel composer.
            var attachments = queuedAttachments ?? CollectAttachments(prompt);
            EnterChatLayout();
            if (fromComposer) ClearComposer();

            // La sessione nasce con il primo messaggio e va subito nella cronologia.
            _session ??= new ChatSession
            {
                Title = SessionStore.TitleFrom(prompt.Length > 0 ? prompt : attachments.FirstOrDefault()?.Name ?? "Allegato"),
                Messages = _history,
                ProjectPath = _projectPath,
            };
            // Riferimenti locali: se durante la risposta si apre un'altra sessione,
            // la risposta finisce comunque in quella giusta.
            var session = _session;
            var history = _history;
            session.Provider = provider.Name;
            session.Model = modelId;
            session.ModelName = ModelButton.Content as string ?? "";
            session.ProjectPath = _projectPath ?? session.ProjectPath;
            bool planMode = _planMode;
            session.Mode = planMode ? "plan" : "act";
            RemoveActSuggestion();
            history.Add(new ChatMessage("user", prompt) { Attachments = attachments.Count > 0 ? attachments : null });
            await SaveSessionAsync(session);
            App.MainWindow?.SetCurrentSession(session.Id);

            AddUserBubble(prompt, attachments);
            UpdateTokenEstimate();

            // Con un progetto aperto Cline lavora da agente (strumenti sui file e comandi);
            // senza progetto risponde e basta.
            AgentTools? tools = session.ProjectPath is { } root && Directory.Exists(root)
                ? new AgentTools(root) { PlanMode = planMode }
                : null;
            if (tools == null && history.Count == 1)
            {
                ShowBar(InfoBarSeverity.Informational, "Nessun progetto scelto: Cline risponde senza leggere o modificare file.", null);
            }

            _streaming = new CancellationTokenSource();
            var ct = _streaming.Token;
            SetStreaming(true);
            bool producedSomething = false;
            bool answeredInText = false; // il turno finale è una risposta scritta (per il pulsante "Passa ad Agisci")
            try
            {
                for (int step = 0; ; step++)
                {
                    if (step >= MaxAgentSteps)
                    {
                        AddNote($"Cline si è fermato dopo {MaxAgentSteps} passi. Scrivi per farlo continuare.", error: false);
                        break;
                    }

                    var request = new List<ChatMessage>();
                    if (tools != null) request.Add(new ChatMessage("system", tools.SystemPrompt));
                    request.AddRange(history);

                    var (reply, toolCalls, failure) = await StreamTurnAsync(provider, apiKey, session.Model, request, tools, ct);
                    if (reply.Length > 0 || toolCalls.Count > 0)
                    {
                        producedSomething = true;
                        history.Add(new ChatMessage("assistant", reply) { ToolCalls = toolCalls.Count > 0 ? toolCalls : null });
                    }
                    if (failure != null)
                    {
                        AddNote(failure.Value.Message, failure.Value.IsError);
                        break;
                    }
                    if (toolCalls.Count == 0)
                    {
                        if (reply.Length == 0) AddNote("Il modello non ha restituito testo.", error: false);
                        answeredInText = reply.Length > 0;
                        break;
                    }

                    // Il modello ha chiesto degli strumenti: si eseguono uno per volta e i risultati tornano a lui.
                    foreach (var call in toolCalls)
                    {
                        var preview = tools!.Describe(call);
                        var view = new ToolCallView(preview);
                        MessagesPanel.Children.Add(view);
                        ScrollToEnd();

                        // Si controlla a ogni azione: l'utente può cambiare le impostazioni mentre Cline lavora.
                        // In Piano scritture e comandi vengono rifiutati da AgentTools: niente da approvare.
                        bool approved = AutoApproval.IsEnabled(preview.Category) || tools.PlanMode;
                        if (approved)
                        {
                            view.MarkAutoApproved();
                        }
                        else
                        {
                            var choice = await view.WaitForApprovalAsync(ct);
                            approved = choice != ApprovalChoice.Reject;
                            if (choice == ApprovalChoice.Always) AutoApproval.Set(preview.Category, true);
                        }

                        string result;
                        if (approved)
                        {
                            view.SetRunning();
                            result = await tools.ExecuteAsync(call, ct);
                        }
                        else
                        {
                            result = ToolCallView.RejectedResult;
                        }
                        view.SetResult(result);
                        history.Add(new ChatMessage("tool", result) { ToolCallId = call.Id });
                        ScrollToEnd();
                    }
                    await SaveSessionAsync(session);
                }
            }
            catch (OperationCanceledException)
            {
                // Interrotto durante un'approvazione o un comando.
                MarkInterruptedTools();
                AddNote("Interrotto.", error: false);
            }
            finally
            {
                // Ogni richiesta di strumento deve avere una risposta, o la richiesta successiva verrebbe rifiutata.
                CompleteDanglingToolCalls(history);
                _streaming.Dispose();
                _streaming = null;
                SetStreaming(false);
                ScrollToEnd();
            }

            // Un piano concluso (in un progetto, dove Agisci può davvero eseguirlo): si propone di passare ad Agisci.
            if (planMode && tools != null && answeredInText && ReferenceEquals(_session, session)) AddActSuggestion();

            // Se il primo invio non ha prodotto nulla, la richiesta non resta in cronologia.
            if (!producedSomething && history.Count > 0 && history[^1].Role == "user") history.RemoveAt(history.Count - 1);
            if (history.Count == 0) DiscardEmptySession(session);
            else await SaveSessionAsync(session);
            UpdateTokenEstimate();
            // L'agente può aver cambiato dei file: si aggiornano chip e pannello git.
            await RefreshGitAsync();

            // Il primo messaggio in coda parte adesso, nella stessa sessione.
            if (ReferenceEquals(_session, session) && _queue.Count > 0)
            {
                var next = _queue[0];
                _queue.RemoveAt(0);
                RenderQueue();
                await SendMessageAsync(next.Prompt, fromComposer: false, next.Attachments);
            }
        }

        private const int MaxAgentSteps = 25;

        /// <summary>
        /// Un turno del modello: mostra il testo in streaming (Markdown ridisegnato al massimo ogni 120 ms)
        /// e raccoglie le chiamate agli strumenti. Errori e interruzione vengono restituiti, non lanciati,
        /// così il testo parziale resta nella conversazione.
        /// </summary>
        private async Task<(string Reply, List<ToolCall> ToolCalls, (string Message, bool IsError)? Failure)> StreamTurnAsync(
            ProviderDefinition provider, string? apiKey, string model, List<ChatMessage> request, AgentTools? tools, CancellationToken ct)
        {
            var answer = AddAssistantBlock();
            var reply = new StringBuilder();
            var turn = new ChatTurn();
            var sinceRender = System.Diagnostics.Stopwatch.StartNew();
            (string, bool)? failure = null;
            try
            {
                await foreach (var piece in ChatClient.StreamChatAsync(provider, apiKey, model, request, tools?.Definitions, turn, ct))
                {
                    reply.Append(piece);
                    if (sinceRender.ElapsedMilliseconds >= 120)
                    {
                        answer.Child = MarkdownRenderer.Render(reply.ToString());
                        sinceRender.Restart();
                        ScrollToEnd();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                failure = (reply.Length == 0 ? "Interrotto." : "Risposta interrotta.", false);
            }
            catch (ProviderException ex)
            {
                failure = (ex.Message, true);
            }
            catch (HttpRequestException ex)
            {
                failure = (provider.IsLocal
                    ? $"{provider.Name} non risponde su {new Uri(provider.BaseUrl).Authority}: controlla che sia avviato."
                    : $"Connessione a {provider.Name} non riuscita: {ex.Message}", true);
            }

            if (reply.Length > 0) answer.Child = MarkdownRenderer.Render(reply.ToString());
            else MessagesPanel.Children.Remove(answer);

            // Le chiamate di un turno interrotto o fallito possono essere incomplete: si scartano.
            var calls = failure == null ? turn.ToolCalls : [];
            return (reply.ToString(), calls, failure);
        }

        /// <summary>Aggiunge una risposta "interrotta" alle richieste di strumenti rimaste senza risultato.</summary>
        private static void CompleteDanglingToolCalls(List<ChatMessage> history)
        {
            int lastAssistant = history.FindLastIndex(m => m.Role == "assistant");
            if (lastAssistant < 0 || history[lastAssistant].ToolCalls is not { Count: > 0 } calls) return;

            var answered = history.Skip(lastAssistant + 1).Where(m => m.Role == "tool").Select(m => m.ToolCallId).ToHashSet();
            foreach (var call in calls.Where(c => !answered.Contains(c.Id)))
            {
                history.Add(new ChatMessage("tool", ToolCallView.InterruptedResult) { ToolCallId = call.Id });
            }
        }

        /// <summary>I riquadri ancora in attesa o in esecuzione diventano "Interrotto".</summary>
        private void MarkInterruptedTools()
        {
            foreach (var view in MessagesPanel.Children.OfType<ToolCallView>().Where(v => !v.IsFinished))
            {
                view.SetResult(ToolCallView.InterruptedResult);
            }
        }

        /// <summary>Nota in fondo alla conversazione (errore in rosso, altrimenti in grigio).</summary>
        private void AddNote(string text, bool error)
        {
            MessagesPanel.Children.Add(new TextBlock
            {
                Text = text,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
                Foreground = (Brush)Application.Current.Resources[error ? "ClineErrorTextBrush" : "ClineTextMutedBrush"]
            });
        }

        // ---- Conversazione ----

        private List<ChatMessage> _history = [];
        private ChatSession? _session;
        private string? _projectPath;
        private CancellationTokenSource? _streaming;

        /// <summary>Id della sessione mostrata, o null se è una sessione nuova non ancora iniziata.</summary>
        public string? CurrentSessionId => _session?.Id;

        private async Task SaveSessionAsync(ChatSession session)
        {
            try
            {
                await SessionStore.SaveAsync(session);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ShowBar(InfoBarSeverity.Error, $"Impossibile salvare la sessione: {ex.Message}", null);
            }
        }

        private void DiscardEmptySession(ChatSession session)
        {
            try { SessionStore.Delete(session.Id); }
            catch (IOException) { }
            if (!ReferenceEquals(_session, session)) return;
            _session = null;
            _history = [];
            App.MainWindow?.SetCurrentSession(null);
        }

        /// <summary>Riapre una sessione salvata: messaggi, modello e progetto.</summary>
        public async Task OpenSessionAsync(string id)
        {
            if (_session?.Id == id) return;
            var session = await SessionStore.LoadAsync(id);
            if (session == null)
            {
                ShowBar(InfoBarSeverity.Error, "La sessione non è più disponibile.", null);
                return;
            }

            ResetConversation();
            _session = session;
            _history = session.Messages;
            // Provider e modello della sessione (senza toccare le preferenze per le sessioni nuove).
            SelectProvider(ProviderCatalog.Find(session.Provider) ?? ProviderCatalog.Default, session.Model, session.ModelName);
            // Il progetto è quello della sessione: se non ne ha, si toglie quello della sessione precedente
            // (altrimenti al messaggio successivo verrebbe salvato nella sessione sbagliata).
            if (session.ProjectPath is { } path && Directory.Exists(path)) ShowProject(path);
            else ClearProject();
            SetMode(session.Mode == "plan", announce: false);

            EnterChatLayout();
            // Gli strumenti si ridisegnano già conclusi: riquadro dalla richiesta, stato dal risultato.
            var describer = new AgentTools(session.ProjectPath ?? Environment.CurrentDirectory);
            var views = new Dictionary<string, ToolCallView>();
            foreach (var message in _history)
            {
                switch (message.Role)
                {
                    case "user":
                        AddUserBubble(message.Content, message.Attachments);
                        break;
                    case "assistant":
                        if (message.Content.Length > 0) AddAssistantBlock().Child = MarkdownRenderer.Render(message.Content);
                        foreach (var call in message.ToolCalls ?? [])
                        {
                            var view = new ToolCallView(describer.Describe(call, live: false));
                            views[call.Id] = view;
                            MessagesPanel.Children.Add(view);
                        }
                        break;
                    case "tool" when message.ToolCallId != null && views.TryGetValue(message.ToolCallId, out var target):
                        target.SetResult(message.Content);
                        break;
                }
            }
            App.MainWindow?.SetCurrentSession(session.Id);
            UpdateTokenEstimate();
            ScrollToEnd();
        }

        /// <summary>Chiamato quando la sessione mostrata viene eliminata dalla cronologia.</summary>
        public void OnSessionDeleted(string id)
        {
            if (_session?.Id == id) ResetConversation();
        }

        /// <summary>Al primo messaggio il robot sparisce e il composer scende in fondo.</summary>
        private void EnterChatLayout()
        {
            if (ConversationScroll.Visibility == Visibility.Visible) return;
            Hero.Visibility = Visibility.Collapsed;
            ChipsRow.Margin = new Thickness(0, 0, 0, 8);
            ConversationScroll.Visibility = Visibility.Visible;
            Grid.SetRow(ComposerArea, 1);
            Grid.SetRowSpan(ComposerArea, 1);
            ComposerArea.VerticalAlignment = VerticalAlignment.Bottom;
            ComposerArea.Margin = new Thickness(24, 8, 24, 24);
        }

        /// <summary>Messaggio dell'utente: testo e, sotto, gli allegati (miniature delle immagini, nomi dei file).</summary>
        private void AddUserBubble(string text, IReadOnlyList<MessageAttachment>? attachments = null)
        {
            var content = new StackPanel { Spacing = 8 };
            if (text.Length > 0)
            {
                content.Children.Add(new TextBlock { Text = text, FontSize = 14, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
            }
            if (attachments is { Count: > 0 })
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                foreach (var attachment in attachments) row.Children.Add(AttachmentChip(attachment, onRemove: null));
                content.Children.Add(new ScrollViewer
                {
                    Content = row,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollMode = ScrollMode.Auto,
                    VerticalScrollMode = ScrollMode.Disabled,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                });
            }

            MessagesPanel.Children.Add(new Border
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                MaxWidth = 640,
                Padding = new Thickness(14, 10, 14, 10),
                CornerRadius = new CornerRadius(12),
                Background = (Brush)Application.Current.Resources["ClineSurface3Brush"],
                Child = content
            });
        }

        /// <summary>Contenitore della risposta: mostra "…" finché non arriva il testo.</summary>
        private Border AddAssistantBlock()
        {
            var answer = new Border
            {
                Child = new TextBlock
                {
                    Text = "…",
                    FontSize = 14,
                    Foreground = (Brush)Application.Current.Resources["ClineTextMutedBrush"]
                }
            };
            MessagesPanel.Children.Add(answer);
            return answer;
        }

        private void SetStreaming(bool streaming)
        {
            SendIcon.Glyph = streaming ? "" : "";
            ToolTipService.SetToolTip(SendButton, streaming ? "Interrompi" : "Invia (Invio)");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(SendButton, streaming ? "Interrompi" : "Invia");
            UpdateSendEnabled();
        }

        /// <summary>
        /// Larghezza esplicita della colonna dei messaggi (max 856, come il composer):
        /// lasciata al ScrollViewer, la colonna usciva dal bordo destro.
        /// </summary>
        private void ConversationScroll_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            MessagesPanel.Width = Math.Max(0, Math.Min(856, e.NewSize.Width) - 48);
        }

        private void ScrollToEnd()
        {
            ConversationScroll.UpdateLayout();
            ConversationScroll.ChangeView(null, ConversationScroll.ScrollableHeight, null, true);
        }

        private void ShowBar(InfoBarSeverity severity, string message, (string Label, string Route)? action)
        {
            ChatBar.Severity = severity;
            ChatBar.Message = message;
            if (action is { } a)
            {
                var button = new Button { Content = a.Label };
                button.Click += (_, _) => { ChatBar.IsOpen = false; App.MainWindow?.NavigateTo(a.Route); };
                ChatBar.ActionButton = button;
            }
            else
            {
                ChatBar.ActionButton = null;
            }
            ChatBar.IsOpen = true;
        }

        /// <summary>Nuova sessione: svuota la conversazione e torna alla schermata di benvenuto.</summary>
        public void ResetConversation()
        {
            _streaming?.Cancel();
            // Non si svuota la lista: appartiene alla sessione salvata.
            _history = [];
            _session = null;
            SelectProvider(PreferredProvider());
            App.MainWindow?.SetCurrentSession(null);
            _actSuggestion = null;
            _queue.Clear();
            RenderQueue();
            MessagesPanel.Children.Clear();
            TokenText.Visibility = Visibility.Collapsed;
            ConversationScroll.Visibility = Visibility.Collapsed;
            Hero.Visibility = Visibility.Visible;
            ChipsRow.Margin = new Thickness(0, 0, 0, 16);
            Grid.SetRow(ComposerArea, 0);
            Grid.SetRowSpan(ComposerArea, 2);
            ComposerArea.VerticalAlignment = VerticalAlignment.Center;
            ComposerArea.Margin = new Thickness(24, 0, 24, 0);
            ChatBar.IsOpen = false;
        }

        // ---- Provider e modello ----

        private ProviderDefinition _provider = ProviderCatalog.Default;
        private string? _modelId;

        // Elenchi dei modelli già scaricati, per provider (validi finché l'app resta aperta).
        private static readonly Dictionary<string, IReadOnlyList<ModelInfo>> s_models = [];
        private IReadOnlyList<ModelInfo>? _shownModels;

        private static ProviderDefinition PreferredProvider() =>
            ProviderCatalog.Find(AppPreferences.Get("chat.provider", ProviderCatalog.OpenRouter)) ?? ProviderCatalog.Default;

        // Modello scelto per ogni provider. Per OpenRouter si leggono anche le chiavi di prima
        // ("chat.model"), così chi aveva già scelto un modello lo ritrova.
        private static string? PreferredModel(ProviderDefinition p) =>
            AppPreferences.Get<string?>($"chat.model.{p.Name}", null)
            ?? (p.Name == ProviderCatalog.OpenRouter ? AppPreferences.Get<string?>("chat.model", null) : null)
            ?? p.DefaultModel;

        private static string? PreferredModelName(ProviderDefinition p) =>
            AppPreferences.Get<string?>($"chat.modelName.{p.Name}", null)
            ?? (p.Name == ProviderCatalog.OpenRouter ? AppPreferences.Get<string?>("chat.modelName", null) : null)
            ?? p.DefaultModelName;

        /// <summary>Mostra il provider e il suo modello (quello scelto l'ultima volta, se c'è).</summary>
        private void SelectProvider(ProviderDefinition provider, string? modelId = null, string? modelName = null)
        {
            _provider = provider;
            ProviderButton.Content = provider.Name;
            _modelId = modelId ?? PreferredModel(provider);
            string? name = modelId != null ? modelName : PreferredModelName(provider);
            ModelButton.Content = _modelId == null ? "Scegli modello" : ShortName(string.IsNullOrEmpty(name) ? _modelId : name);
        }

        private void ProviderMenu_Opening(object sender, object e)
        {
            ProviderMenu.Items.Clear();
            int? lastGroup = null;
            foreach (var provider in ProviderCatalog.All)
            {
                // Gruppi separati da una riga: con chiave, locali, personalizzati.
                int group = provider.Custom ? 2 : provider.IsLocal ? 1 : 0;
                if (lastGroup != null && group != lastGroup) ProviderMenu.Items.Add(new MenuFlyoutSeparator());
                lastGroup = group;
                bool ready = !provider.NeedsKey || CredentialStore.HasApiKey(provider.Name);
                var item = new ToggleMenuFlyoutItem
                {
                    Text = ready ? provider.Name : $"{provider.Name} (senza chiave)",
                    IsChecked = ReferenceEquals(provider, _provider),
                    Tag = provider,
                };
                item.Click += (_, _) =>
                {
                    AppPreferences.Set("chat.provider", provider.Name);
                    SelectProvider(provider);
                    if (_session != null) _session.Provider = provider.Name;
                };
                ProviderMenu.Items.Add(item);
            }
        }

        private async void ModelFlyout_Opening(object sender, object e)
        {
            ModelSearchBox.Text = "";
            var provider = _provider;
            if (s_models.TryGetValue(provider.Name, out var cached))
            {
                _shownModels = cached;
                FilterModels("");
                return;
            }

            _shownModels = null;
            ModelList.ItemsSource = null;
            string? key = CredentialStore.GetApiKey(provider.Name);
            // L'elenco di OpenRouter è pubblico; gli altri provider con chiave la richiedono anche per elencare.
            if (provider.NeedsKey && key == null && provider.Name != ProviderCatalog.OpenRouter)
            {
                ModelStatusText.Text = $"Aggiungi la chiave API di {provider.Name} in Impostazioni › BYOK per vedere i modelli.";
                ModelStatusText.Visibility = Visibility.Visible;
                return;
            }

            ModelStatusText.Text = $"Caricamento dei modelli di {provider.Name}…";
            ModelStatusText.Visibility = Visibility.Visible;
            try
            {
                var models = await ChatClient.ListModelsAsync(provider, key);
                s_models[provider.Name] = models;
                if (!ReferenceEquals(provider, _provider)) return;
                _shownModels = models;
                FilterModels(ModelSearchBox.Text.Trim());
            }
            catch (ProviderException ex)
            {
                ModelStatusText.Text = ex.Message;
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException)
            {
                ModelStatusText.Text = provider.IsLocal
                    ? $"{provider.Name} non risponde su {new Uri(provider.BaseUrl).Authority}: controlla che sia avviato."
                    : "Impossibile caricare l'elenco dei modelli. Controlla la connessione e riapri il menu.";
            }
        }

        private void ModelSearch_TextChanged(object sender, TextChangedEventArgs e) => FilterModels(ModelSearchBox.Text.Trim());

        private void FilterModels(string query)
        {
            if (_shownModels == null) return;
            var matches = _shownModels
                .Where(m => query.Length == 0
                    || m.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                    || m.Id.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();
            ModelList.ItemsSource = matches;
            ModelList.SelectedItem = matches.FirstOrDefault(m => m.Id == _modelId);
            ModelStatusText.Text = matches.Count == 0 ? "Nessun modello trovato." : "";
            ModelStatusText.Visibility = matches.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ModelList_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is not ModelInfo model) return;
            _modelId = model.Id;
            ModelButton.Content = ShortName(model.Name);
            AppPreferences.Set($"chat.model.{_provider.Name}", model.Id);
            AppPreferences.Set($"chat.modelName.{_provider.Name}", model.Name);
            ModelFlyout.Hide();
        }

        /// <summary>"Anthropic: Claude Sonnet" diventa "Claude Sonnet".</summary>
        private static string ShortName(string name)
        {
            int colon = name.IndexOf(": ", StringComparison.Ordinal);
            return colon > 0 ? name[(colon + 2)..] : name;
        }

        private void Reasoning_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem item) ReasoningText.Text = item.Text;
        }

        // ---- Occhi che seguono il puntatore ----

        private void Page_PointerMoved(object sender, PointerRoutedEventArgs e)
        {
            var p = e.GetCurrentPoint(Hero).Position;
            double dx = p.X - Hero.ActualWidth / 2;
            double dy = p.Y - Hero.ActualHeight / 2;
            double length = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));

            // Più il puntatore è lontano, più gli occhi arrivano al bordo (fino al massimo).
            double reach = Math.Min(1, length / 300);
            double x = dx / length * EyeMaxX * reach;
            double y = dy / length * EyeMaxY * reach;

            LeftEyeTransform.X = RightEyeTransform.X = x;
            LeftEyeTransform.Y = RightEyeTransform.Y = y;
        }
    }
}
