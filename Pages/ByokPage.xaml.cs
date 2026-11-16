using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ClineWin
{
    /// <summary>
    /// BYOK ("usa la tua chiave"): i provider collegati (<see cref="ProviderCatalog"/>) con ricerca
    /// e pannello di dettaglio: chiave API, oppure nota per i provider locali, ed elenco dei modelli.
    /// </summary>
    public sealed partial class ByokPage : Page
    {
        private static IReadOnlyList<ProviderDefinition> Providers => ProviderCatalog.All;

        private ProviderDefinition? _selected;

        public ByokPage()
        {
            InitializeComponent();
            ApplyFilter("");
        }

        private void Search_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            ApplyFilter(sender.Text.Trim());
        }

        private void ApplyFilter(string query)
        {
            bool Match(ProviderDefinition p) => query.Length == 0 || p.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase);

            var popular = Providers.Where(p => p.Popular && Match(p)).ToList();
            var all = Providers.Where(Match).OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

            PopularList.ItemsSource = popular;
            AllList.ItemsSource = all;

            // Durante la ricerca si mostra un solo elenco, senza la sezione "Popolari".
            bool searching = query.Length > 0;
            PopularHeader.Visibility = PopularHint.Visibility = PopularList.Visibility =
                !searching && popular.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            AllHeader.Visibility = all.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            NoMatchText.Text = $"Nessun provider corrisponde a \"{query}\".";
            NoMatchText.Visibility = all.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            UpdateSubtitle();
        }

        private void UpdateSubtitle()
        {
            // Configurati: chi ha una chiave salvata, più i personalizzati (la loro chiave è facoltativa).
            int configured = Providers.Count(p => p.Custom || (p.NeedsKey && CredentialStore.HasApiKey(p.Name)));
            SubtitleText.Text = $"{configured} configurati · {Providers.Count} disponibili";
        }

        private void Provider_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is not ProviderDefinition provider) return;
            // Tiene selezionata la stessa voce in un solo elenco.
            (ReferenceEquals(sender, PopularList) ? AllList : PopularList).SelectedItem = null;
            ShowDetail(provider);
        }

        /// <summary>Apre il pannello di dettaglio del provider.</summary>
        private void ShowDetail(ProviderDefinition provider)
        {
            _selected = provider;
            DetailTitle.Text = provider.Name;
            LocalCard.Visibility = provider.IsLocal ? Visibility.Visible : Visibility.Collapsed;
            LocalText.Text = provider.IsLocal
                ? $"Cline si collega a {provider.Name} su {new Uri(provider.BaseUrl).Authority}. Non serve una chiave API: basta che {provider.Name} sia avviato con almeno un modello."
                : "";
            CustomCard.Visibility = provider.Custom ? Visibility.Visible : Visibility.Collapsed;
            CustomUrlText.Text = provider.Custom
                ? $"URL di base: {provider.BaseUrl}" + (provider.ModelsUrl is { Length: > 0 } m ? $"\nElenco modelli: {m}" : "")
                : "";
            CustomWarningText.Visibility = provider.Custom && CustomProviders.IsInsecureRemote(provider.BaseUrl) ? Visibility.Visible : Visibility.Collapsed;
            ApiKeyRow.Visibility = ApiKeyNote.Visibility = provider.NeedsKey || provider.Custom ? Visibility.Visible : Visibility.Collapsed;
            KeyLink.Content = $"Ottieni una chiave API di {provider.Name}";
            KeyLink.Visibility = provider.KeyUrl != null ? Visibility.Visible : Visibility.Collapsed;
            ApiKeyBox.Password = "";
            ModelsList.ItemsSource = null;
            ModelsList.Visibility = Visibility.Collapsed;
            ModelsStatusText.Text = "Fai clic su aggiorna per caricare i modelli.";
            ModelsStatusText.Visibility = Visibility.Visible;
            UpdateKeyState();

            DetailColumn.Width = new GridLength(1, GridUnitType.Star);
            DetailPane.Visibility = Visibility.Visible;
        }

        // ---- Chiave API ----

        /// <summary>Aggiorna badge, pulsanti e nota in base alla chiave salvata del provider scelto.</summary>
        private void UpdateKeyState()
        {
            if (_selected == null) return;
            bool saved = CredentialStore.HasApiKey(_selected.Name);

            StatusBadgeText.Text = _selected.Custom ? "Personalizzato" : _selected.IsLocal ? "Locale" : saved ? "Configurato" : "Non configurato";
            ApiKeyBox.PlaceholderText = saved ? "Salvata (incolla per sostituire)" : _selected.Custom ? "Facoltativa" : "Incolla la chiave";
            RemoveKeyButton.Visibility = saved ? Visibility.Visible : Visibility.Collapsed;
            SaveKeyButton.IsEnabled = ApiKeyBox.Password.Trim().Length > 0;

            ApiKeyNote.Text = "La chiave viene salvata in Gestione credenziali di Windows, cifrata per il tuo utente."
                + (_selected.Custom ? " Per questo provider è facoltativa: serve solo se il servizio la richiede." : "");
            UpdateSubtitle();
        }

        // ---- Modelli ----

        private async void RefreshModels_Click(object sender, RoutedEventArgs e)
        {
            if (_selected is not { } provider) return;
            string? key = CredentialStore.GetApiKey(provider.Name);
            // L'elenco di OpenRouter è pubblico; gli altri provider con chiave la richiedono.
            if (provider.NeedsKey && key == null && provider.Name != ProviderCatalog.OpenRouter)
            {
                ModelsStatusText.Text = "Salva prima la chiave API: serve anche per leggere l'elenco dei modelli.";
                return;
            }

            ModelsStatusText.Text = "Caricamento…";
            ModelsStatusText.Visibility = Visibility.Visible;
            ModelsList.Visibility = Visibility.Collapsed;
            try
            {
                var models = await ChatClient.ListModelsAsync(provider, key);
                if (!ReferenceEquals(_selected, provider)) return; // nel frattempo è stato scelto un altro provider
                ModelsList.ItemsSource = models;
                ModelsList.Visibility = models.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                ModelsStatusText.Text = models.Count > 0 ? $"{models.Count} modelli disponibili." : "Il provider non ha restituito modelli.";
            }
            catch (ProviderException ex)
            {
                ModelsStatusText.Text = ex.Message;
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException)
            {
                ModelsStatusText.Text = provider.IsLocal
                    ? $"{provider.Name} non risponde: controlla che sia avviato."
                    : $"Impossibile raggiungere {provider.Name}. Controlla la connessione.";
            }
        }

        private void OpenLocalModels_Click(object sender, RoutedEventArgs e) => App.MainWindow?.NavigateTo("settings/local");

        private void ApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            SaveKeyButton.IsEnabled = ApiKeyBox.Password.Trim().Length > 0;
        }

        private void SaveKey_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null) return;
            string key = ApiKeyBox.Password.Trim();
            if (key.Length == 0) return;

            CredentialStore.SetApiKey(_selected.Name, key);
            ApiKeyBox.Password = "";
            UpdateKeyState();
        }

        private void RemoveKey_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null) return;
            CredentialStore.RemoveApiKey(_selected.Name);
            UpdateKeyState();
        }

        private void CloseDetail_Click(object sender, RoutedEventArgs e)
        {
            DetailPane.Visibility = Visibility.Collapsed;
            DetailColumn.Width = new GridLength(0);
            PopularList.SelectedItem = AllList.SelectedItem = null;
        }

        private async void KeyLink_Click(object sender, RoutedEventArgs e)
        {
            if (_selected?.KeyUrl is { } url)
            {
                await Windows.System.Launcher.LaunchUriAsync(new Uri(url));
            }
        }

        // ---- Provider personalizzati ----

        // WinUI permette una sola ContentDialog aperta: aprirne una seconda fa chiudere l'app.
        private bool _dialogOpen;

        private async void AddProvider_Click(object sender, RoutedEventArgs e)
        {
            if (_dialogOpen) return;
            _dialogOpen = true;
            try
            {
                await AddProviderAsync();
            }
            finally
            {
                _dialogOpen = false;
            }
        }

        private async Task AddProviderAsync()
        {
            var brushes = Application.Current.Resources;
            var form = new StackPanel { Spacing = 12, MinWidth = 420 };
            form.Children.Add(new TextBlock
            {
                Text = "Aggiungi un servizio compatibile con OpenAI (per esempio un server vLLM, LiteLLM o un gateway aziendale). "
                     + "Comparirà tra i provider del composer.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)brushes["ClineText2Brush"]
            });
            var nameBox = new TextBox { Header = "Nome", PlaceholderText = "Il mio server" };
            var urlBox = new TextBox { Header = "URL di base", PlaceholderText = "https://api.example.com/v1" };
            var modelsBox = new TextBox { Header = "URL elenco modelli (facoltativo)", PlaceholderText = "Se vuoto: URL di base + /models" };
            var keyBox = new PasswordBox { Header = "Chiave API (facoltativa)", PlaceholderText = "sk-...", PasswordRevealMode = PasswordRevealMode.Peek };
            var message = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap };
            foreach (var element in new UIElement[] { nameBox, urlBox, modelsBox, keyBox, message }) form.Children.Add(element);

            var dialog = new ContentDialog
            {
                Title = "Aggiungi provider",
                Content = new ScrollViewer { Content = form },
                PrimaryButtonText = "Aggiungi provider",
                IsPrimaryButtonEnabled = false,
                CloseButtonText = "Annulla",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot
            };

            string normalizedUrl = "";
            bool touched = false;
            void Revalidate()
            {
                string? error = CustomProviders.Validate(nameBox.Text, urlBox.Text, modelsBox.Text, out normalizedUrl);
                dialog.IsPrimaryButtonEnabled = error == null;
                // Il primo errore compare solo dopo che si è iniziato a scrivere l'URL, per non accogliere con un rimprovero.
                if (urlBox.Text.Length > 0) touched = true;
                bool warn = error == null && keyBox.Password.Length > 0 && CustomProviders.IsInsecureRemote(urlBox.Text);
                message.Text = error != null && touched ? error
                    : warn ? "Attenzione: l'indirizzo usa http senza cifratura, la chiave API viaggerà in chiaro."
                    : "";
                message.Foreground = (Microsoft.UI.Xaml.Media.Brush)brushes[error != null ? "ClineErrorTextBrush" : "ClineText2Brush"];
            }
            nameBox.TextChanged += (_, _) => Revalidate();
            urlBox.TextChanged += (_, _) => Revalidate();
            modelsBox.TextChanged += (_, _) => Revalidate();
            keyBox.PasswordChanged += (_, _) => Revalidate();

            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            // Si ricontrolla al momento del salvataggio (il nome potrebbe essere stato preso nel frattempo).
            if (CustomProviders.Validate(nameBox.Text, urlBox.Text, modelsBox.Text, out normalizedUrl) != null) return;

            string name = nameBox.Text.Trim();
            string? modelsUrl = string.IsNullOrWhiteSpace(modelsBox.Text) ? null : modelsBox.Text.Trim();
            CustomProviders.Add(new CustomProvider(name, normalizedUrl, modelsUrl));
            if (keyBox.Password.Trim() is { Length: > 0 } key) CredentialStore.SetApiKey(name, key);

            SearchBox.Text = "";
            ApplyFilter("");
            // Apre subito il dettaglio del nuovo provider.
            if (ProviderCatalog.Find(name) is { } added) ShowDetail(added);
        }

        private async void RemoveProvider_Click(object sender, RoutedEventArgs e)
        {
            if (_selected is not { Custom: true } provider || _dialogOpen) return;
            _dialogOpen = true;
            var dialog = new ContentDialog
            {
                Title = "Rimuovere il provider?",
                Content = $"“{provider.Name}” verrà tolto dai provider, insieme alla sua chiave API salvata. Le sessioni che lo usavano restano nella cronologia.",
                PrimaryButtonText = "Rimuovi",
                CloseButtonText = "Annulla",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };
            ContentDialogResult result;
            try { result = await dialog.ShowAsync(); }
            finally { _dialogOpen = false; }
            if (result != ContentDialogResult.Primary) return;

            CustomProviders.Remove(provider.Name);
            CloseDetail_Click(this, new RoutedEventArgs());
            ApplyFilter(SearchBox.Text.Trim());
        }
    }
}
