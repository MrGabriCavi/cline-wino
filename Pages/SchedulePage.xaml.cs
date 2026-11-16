using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;

namespace ClineWin
{
    /// <summary>
    /// Pianificazione: elenco delle attività (esegui ora, modifica, attiva/disattiva, elimina),
    /// modelli pronti e modulo di creazione. L'esecuzione la fa <see cref="Scheduler"/>.
    /// </summary>
    public sealed partial class SchedulePage : Page
    {
        private sealed record RoutineTemplate(string Glyph, string Title, string Description, string Name, string Prompt, string Frequency, int[] Days, string Time);

        // Prompt scritti per ClineWin: brevi, in italiano, e pensati per un'esecuzione senza nessuno davanti.
        private static readonly RoutineTemplate[] Templates =
        [
            new("", "Rapporto giornaliero", "Riassume ogni mattina le modifiche del giorno prima.",
                "Rapporto giornaliero",
                "Esamina le modifiche al progetto delle ultime 24 ore (per esempio con git log e git diff) e scrivi un rapporto breve: cosa è cambiato, in quali parti, ed eventuali punti da controllare. Non modificare file.",
                "daily", [], "09:00"),
            new("", "Caccia ai bug", "Cerca bug gravi nelle modifiche recenti.",
                "Caccia ai bug",
                "Controlla le modifiche recenti del progetto alla ricerca di bug gravi: perdita di dati, crash, errori di logica evidenti. Per ogni problema indica file, riga, come si manifesta e una correzione proposta. Se non trovi nulla di serio, dillo in una riga.",
                "weekly", [1, 2, 3, 4, 5], "18:00"),
            new("", "Controllo di sicurezza", "Una revisione settimanale dei punti più delicati.",
                "Controllo di sicurezza",
                "Rivedi il progetto cercando problemi di sicurezza concreti: segreti nel codice, input non validati usati in comandi, query o percorsi di file, permessi mancanti. Elenca solo i problemi con un rischio reale, con file e motivazione.",
                "weekly", [1], "10:00"),
            new("", "Documentazione aggiornata", "Verifica che il README descriva ancora il progetto.",
                "Documentazione aggiornata",
                "Confronta il README e la documentazione con lo stato attuale del codice e segnala le parti non più vere o mancanti, con una proposta di testo per ciascuna.",
                "weekly", [5], "16:00"),
        ];

        public SchedulePage()
        {
            InitializeComponent();
            BuildTemplates();
            Refresh();
            ScheduledTaskStore.Changed += OnChanged;
            Scheduler.StateChanged += OnChanged;
            Unloaded += (_, _) =>
            {
                ScheduledTaskStore.Changed -= OnChanged;
                Scheduler.StateChanged -= OnChanged;
            };
        }

        private void OnChanged() => DispatcherQueue.TryEnqueue(Refresh);

        private static Brush Res(string key) => (Brush)Application.Current.Resources[key];

        // ---- Elenco ----

        private void Refresh()
        {
            var tasks = ScheduledTaskStore.Load();
            TaskRows.Children.Clear();
            foreach (var task in tasks.OrderBy(t => t.NextRun ?? DateTimeOffset.MaxValue))
            {
                TaskRows.Children.Add(TaskRow(task));
            }
            EmptyText.Visibility = tasks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private UIElement TaskRow(ScheduledTask task)
        {
            bool running = Scheduler.RunningTaskId == task.Id;
            var grid = new Grid { ColumnSpacing = 12 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var info = new StackPanel { Spacing = 3 };
            info.Children.Add(new TextBlock { Text = task.Name, FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            info.Children.Add(new TextBlock
            {
                Text = $"{task.ScheduleText} · {task.Provider} · {task.Model}"
                       + (task.ProjectPath != null ? $" · {Path.GetFileName(task.ProjectPath.TrimEnd('\\'))}" : " · senza progetto"),
                FontSize = 12,
                Foreground = Res("ClineText2Brush"),
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            info.Children.Add(new TextBlock
            {
                Text = running ? "In esecuzione…" : $"{task.NextRunText} · {task.LastRunText}",
                FontSize = 12,
                Foreground = Res(running ? "ClinePrimaryBrush" : "ClineTextMutedBrush"),
                TextWrapping = TextWrapping.Wrap,
            });
            grid.Children.Add(info);

            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
            var toggle = new ToggleSwitch { IsOn = task.Enabled, OnContent = "", OffContent = "", MinWidth = 0, VerticalAlignment = VerticalAlignment.Center };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(toggle, $"Attiva {task.Name}");
            toggle.Toggled += (_, _) =>
            {
                if (toggle.IsOn == task.Enabled) return;
                task.Enabled = toggle.IsOn;
                // Riattivando si riparte da adesso: non si recuperano le esecuzioni del periodo in cui era spenta.
                if (task.Enabled) task.Anchor = DateTimeOffset.Now;
                ScheduledTaskStore.Upsert(task);
            };
            actions.Children.Add(toggle);
            actions.Children.Add(IconButton("", $"Esegui {task.Name} ora", async () => await RunNowAsync(task), enabled: !running && Scheduler.RunningTaskId == null));
            if (task.LastSessionId is { } sessionId)
            {
                actions.Children.Add(IconButton("", $"Apri l'ultima esecuzione di {task.Name}", async () => await OpenSessionAsync(sessionId)));
            }
            actions.Children.Add(IconButton("", $"Modifica {task.Name}", async () => await EditAsync(task)));
            actions.Children.Add(IconButton("", $"Elimina {task.Name}", async () => await DeleteAsync(task)));
            Grid.SetColumn(actions, 1);
            grid.Children.Add(actions);

            return new Border { Padding = new Thickness(16, 12, 12, 12), Style = (Style)Application.Current.Resources["ClineCardStyle"], Child = grid };
        }

        private static Button IconButton(string glyph, string name, Func<Task> onClick, bool enabled = true)
        {
            var button = new Button
            {
                Style = (Style)Application.Current.Resources["ClineIconButtonStyle"],
                Content = new FontIcon { Glyph = glyph, FontSize = 13 },
                IsEnabled = enabled,
            };
            ToolTipService.SetToolTip(button, name);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, name);
            button.Click += async (_, _) => await onClick();
            return button;
        }

        private async Task RunNowAsync(ScheduledTask task)
        {
            if (!await Scheduler.RunAsync(task.Id))
            {
                await ShowMessageAsync("Esecuzione non avviata", "C'è già un'attività in esecuzione: riprova quando ha finito.");
            }
        }

        private static async Task OpenSessionAsync(string sessionId)
        {
            App.MainWindow?.NavigateTo(Routes.Session);
            if (App.MainWindow?.CurrentPage is WelcomePage page) await page.OpenSessionAsync(sessionId);
        }

        private async Task DeleteAsync(ScheduledTask task)
        {
            if (_dialogOpen) return;
            var dialog = new ContentDialog
            {
                Title = "Eliminare l'attività?",
                Content = $"“{task.Name}” non verrà più eseguita. Le sessioni delle esecuzioni passate restano nella cronologia.",
                PrimaryButtonText = "Elimina",
                CloseButtonText = "Annulla",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };
            if (await ShowDialogAsync(dialog) == ContentDialogResult.Primary) ScheduledTaskStore.Delete(task.Id);
        }

        // ---- Modelli ----

        private void BuildTemplates()
        {
            for (int i = 0; i < Templates.Length; i++)
            {
                var template = Templates[i];
                if (i % 2 == 0) TemplateGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var content = new Grid { ColumnSpacing = 12 };
                content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                content.Children.Add(new FontIcon { Glyph = template.Glyph, FontSize = 18, Foreground = Res("ClinePrimaryBrush"), VerticalAlignment = VerticalAlignment.Top });
                var text = new StackPanel { Spacing = 2 };
                text.Children.Add(new TextBlock { Text = template.Title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
                text.Children.Add(new TextBlock { Text = template.Description, FontSize = 12, Foreground = Res("ClineText2Brush"), TextWrapping = TextWrapping.Wrap });
                Grid.SetColumn(text, 1);
                content.Children.Add(text);

                var card = new Button
                {
                    Content = content,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    Padding = new Thickness(16, 12, 16, 12),
                };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(card, $"Modello: {template.Title}");
                card.Click += async (_, _) => await EditAsync(new ScheduledTask
                {
                    Name = template.Name,
                    Prompt = template.Prompt,
                    Frequency = template.Frequency,
                    Days = [.. template.Days],
                    Time = template.Time,
                }, isNew: true);
                Grid.SetRow(card, i / 2);
                Grid.SetColumn(card, i % 2);
                TemplateGrid.Children.Add(card);
            }
        }

        // ---- Modulo ----

        private async void NewTask_Click(object sender, RoutedEventArgs e) => await EditAsync(new ScheduledTask(), isNew: true);

        /// <summary>Modulo di creazione o modifica. Con isNew l'attività non è ancora salvata.</summary>
        private async Task EditAsync(ScheduledTask task, bool isNew = false)
        {
            if (_dialogOpen) return;

            var provider = ProviderCatalog.Find(task.Provider) ?? ProviderCatalog.Default;
            string? projectPath = task.ProjectPath;

            var nameBox = new TextBox { Header = "Nome", Text = task.Name, PlaceholderText = "Revisione giornaliera del codice" };
            var frequency = new ComboBox { Header = "Frequenza", MinWidth = 200, ItemsSource = new[] { "Una volta", "Ogni giorno", "Ogni settimana" } };
            frequency.SelectedIndex = task.Frequency switch { "once" => 0, "weekly" => 2, _ => 1 };

            var datePicker = new CalendarDatePicker { Header = "Data", PlaceholderText = "Scegli la data", MinDate = DateTimeOffset.Now.Date };
            if (DateTime.TryParseExact(task.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) datePicker.Date = date;

            var timePicker = new TimePicker { Header = "Ora", ClockIdentifier = "24HourClock", MinuteIncrement = 5 };
            timePicker.Time = TimeSpan.TryParseExact(task.Time, @"hh\:mm", CultureInfo.InvariantCulture, out var t) ? t : new TimeSpan(9, 0, 0);

            // Giorni della settimana come pulsanti a interruttore, lunedì per primo.
            var dayButtons = ScheduledTask.WeekOrder.Select(day => new ToggleButton
            {
                Content = ScheduledTask.DayLabel(day),
                Tag = day,
                IsChecked = task.Days.Contains(day),
                MinWidth = 48,
                Padding = new Thickness(6, 4, 6, 4),
            }).ToList();
            var daysPanel = new StackPanel { Spacing = 6 };
            daysPanel.Children.Add(new TextBlock { Text = "Giorni" });
            var dayRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            foreach (var b in dayButtons) dayRow.Children.Add(b);
            daysPanel.Children.Add(dayRow);

            var timing = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            timing.Children.Add(frequency);
            timing.Children.Add(datePicker);
            timing.Children.Add(timePicker);

            var promptBox = new TextBox
            {
                Header = "Prompt",
                Text = task.Prompt,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                Height = 140,
                PlaceholderText = "Cosa deve fare l'agente a ogni esecuzione",
            };
            ScrollViewer.SetVerticalScrollBarVisibility(promptBox, ScrollBarVisibility.Auto);

            var providerBox = new ComboBox { Header = "Provider", MinWidth = 200, ItemsSource = ProviderCatalog.All.Select(p => p.Name).ToList(), SelectedItem = provider.Name };
            var modelBox = new AutoSuggestBox { Header = "Modello", Text = task.Model.Length > 0 ? task.Model : PreferredModel(provider) ?? "", PlaceholderText = "Id del modello", MinWidth = 300 };
            var providerRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            providerRow.Children.Add(providerBox);
            providerRow.Children.Add(modelBox);

            var projectText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 340 };
            var pickProject = new Button { Content = "Scegli cartella…" };
            var clearProject = new Button { Content = "Senza progetto" };
            void UpdateProject()
            {
                projectText.Text = projectPath ?? "Nessun progetto: l'agente risponde senza strumenti.";
                clearProject.Visibility = projectPath != null ? Visibility.Visible : Visibility.Collapsed;
            }
            UpdateProject();
            pickProject.Click += async (_, _) =>
            {
                var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
                picker.FileTypeFilter.Add("*");
                WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));
                if (await picker.PickSingleFolderAsync() is { } folder) { projectPath = folder.Path; UpdateProject(); }
            };
            clearProject.Click += (_, _) => { projectPath = null; UpdateProject(); };
            var projectPanel = new StackPanel { Spacing = 6 };
            projectPanel.Children.Add(new TextBlock { Text = "Progetto" });
            var projectRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            projectRow.Children.Add(pickProject);
            projectRow.Children.Add(clearProject);
            projectRow.Children.Add(projectText);
            projectPanel.Children.Add(projectRow);

            var approvalNote = new TextBlock
            {
                Text = $"Approvazione automatica attuale: {AutoApproval.Summary()}. Le altre azioni non verranno eseguite (si cambia dal menu nel composer).",
                FontSize = 12,
                Foreground = Res("ClineText2Brush"),
                TextWrapping = TextWrapping.Wrap,
            };
            var error = new TextBlock { FontSize = 12, Foreground = Res("ClineErrorTextBrush"), TextWrapping = TextWrapping.Wrap };

            var form = new StackPanel { Spacing = 14, Width = 600 };
            foreach (var element in new UIElement[] { nameBox, timing, daysPanel, promptBox, providerRow, projectPanel, approvalNote, error }) form.Children.Add(element);

            void UpdateVisibility()
            {
                datePicker.Visibility = frequency.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
                daysPanel.Visibility = frequency.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
            }
            frequency.SelectionChanged += (_, _) => UpdateVisibility();
            UpdateVisibility();

            // Suggerimenti dei modelli del provider scelto, caricati alla prima digitazione.
            IReadOnlyList<ModelInfo>? models = null;
            string? modelsFor = null;
            providerBox.SelectionChanged += (_, _) =>
            {
                if (ProviderCatalog.Find(providerBox.SelectedItem as string) is { } p) modelBox.Text = PreferredModel(p) ?? "";
            };
            modelBox.TextChanged += async (sender, args) =>
            {
                if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
                var p = ProviderCatalog.Find(providerBox.SelectedItem as string);
                if (p == null) return;
                if (modelsFor != p.Name)
                {
                    modelsFor = p.Name;
                    try { models = await ChatClient.ListModelsAsync(p, CredentialStore.GetApiKey(p.Name)); }
                    catch (Exception ex) when (ex is HttpRequestException or ProviderException or OperationCanceledException or System.Text.Json.JsonException) { models = null; }
                }
                string query = sender.Text.Trim();
                sender.ItemsSource = models?.Where(m => m.Id.Contains(query, StringComparison.OrdinalIgnoreCase) || m.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                    .Take(30).Select(m => m.Id).ToList();
            };

            var dialog = new ContentDialog
            {
                Title = isNew ? "Nuova attività" : "Modifica attività",
                Content = new ScrollViewer { Content = form },
                PrimaryButtonText = isNew ? "Crea" : "Salva",
                CloseButtonText = "Annulla",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot
            };
            // La larghezza massima predefinita (548) taglierebbe il modulo a destra.
            dialog.Resources["ContentDialogMaxWidth"] = 680.0;
            dialog.PrimaryButtonClick += (_, args) =>
            {
                string? message = Validate();
                error.Text = message ?? "";
                args.Cancel = message != null; // il modulo resta aperto finché i dati non sono validi
            };

            string? Validate()
            {
                if (nameBox.Text.Trim().Length == 0) return "Inserisci un nome.";
                if (promptBox.Text.Trim().Length == 0) return "Scrivi il prompt da eseguire.";
                if (modelBox.Text.Trim().Length == 0) return "Indica il modello.";
                if (frequency.SelectedIndex == 2 && !dayButtons.Any(b => b.IsChecked == true)) return "Scegli almeno un giorno.";
                if (frequency.SelectedIndex == 0)
                {
                    if (datePicker.Date is not { } d) return "Scegli la data.";
                    if (d.Date + timePicker.Time <= DateTime.Now) return "La data e l'ora sono già passate.";
                }
                return null;
            }

            if (await ShowDialogAsync(dialog) != ContentDialogResult.Primary) return;

            task.Name = nameBox.Text.Trim();
            task.Prompt = promptBox.Text.Trim();
            task.Frequency = frequency.SelectedIndex switch { 0 => "once", 2 => "weekly", _ => "daily" };
            task.Date = task.Frequency == "once" ? datePicker.Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
            task.Time = timePicker.Time.ToString(@"hh\:mm", CultureInfo.InvariantCulture);
            task.Days = task.Frequency == "weekly" ? dayButtons.Where(b => b.IsChecked == true).Select(b => (int)b.Tag).ToList() : [];
            task.Provider = providerBox.SelectedItem as string ?? ProviderCatalog.OpenRouter;
            task.Model = modelBox.Text.Trim();
            task.ProjectPath = projectPath;
            // Da questo momento: modificando l'orario non partono esecuzioni "arretrate".
            task.Anchor = DateTimeOffset.Now;
            ScheduledTaskStore.Upsert(task);
        }

        private static string? PreferredModel(ProviderDefinition p) =>
            AppPreferences.Get<string?>($"chat.model.{p.Name}", null)
            ?? (p.Name == ProviderCatalog.OpenRouter ? AppPreferences.Get<string?>("chat.model", null) : null)
            ?? p.DefaultModel;

        // ---- Finestre di dialogo (una alla volta: WinUI ne permette una sola aperta) ----

        private bool _dialogOpen;

        private async Task<ContentDialogResult> ShowDialogAsync(ContentDialog dialog)
        {
            if (_dialogOpen) return ContentDialogResult.None;
            _dialogOpen = true;
            try { return await dialog.ShowAsync(); }
            finally { _dialogOpen = false; }
        }

        private async Task ShowMessageAsync(string title, string message) =>
            await ShowDialogAsync(new ContentDialog { Title = title, Content = message, CloseButtonText = "OK", XamlRoot = XamlRoot });
    }
}
