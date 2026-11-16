using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.Graphics;

namespace ClineWin
{
    /// <summary>
    /// Finestra principale: barra del titolo, barra dei menu, barra laterale
    /// e un Frame che ospita le pagine (benvenuto, pianificazione, ...).
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);
            AppWindow.SetIcon("Assets/AppIcon.ico");
            AppWindow.Resize(new SizeInt32(1500, 980));

            if (AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.PreferredMinimumWidth = 900;
                presenter.PreferredMinimumHeight = 600;
            }

            // Pulsanti riduci/ingrandisci/chiudi con i colori di Cline.
            var titleBar = AppWindow.TitleBar;
            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            titleBar.ButtonHoverBackgroundColor = ColorHelper.FromArgb(0x13, 0xE8, 0xE8, 0xFA);
            titleBar.ButtonPressedBackgroundColor = ColorHelper.FromArgb(0x1D, 0xDB, 0xDB, 0xFB);

            NavigateTo(Routes.Session);

            SessionStore.Changed += () => DispatcherQueue.TryEnqueue(async () => await LoadSessionsAsync());
            _ = LoadSessionsAsync();

            // Attività pianificate: partono all'orario mentre la finestra è aperta.
            Scheduler.Start(DispatcherQueue);
        }

        /// <summary>La pagina mostrata in questo momento.</summary>
        public object? CurrentPage => RootFrame.Content;

        // ---- Cronologia sessioni ----

        private string? _currentSessionId;

        // True mentre è il codice a cambiare elenco o selezione: quei cambi non devono aprire sessioni.
        private bool _updatingSessionList;

        private async Task LoadSessionsAsync()
        {
            var sessions = await SessionStore.ListAsync();
            _updatingSessionList = true;
            try
            {
                SessionList.ItemsSource = sessions;
                NoSessionsText.Visibility = sessions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                SelectCurrentSession();
            }
            finally
            {
                _updatingSessionList = false;
            }
        }

        /// <summary>Evidenzia nella barra laterale la sessione mostrata (null = nessuna).</summary>
        public void SetCurrentSession(string? id)
        {
            _currentSessionId = id;
            SelectCurrentSession();
        }

        private void SelectCurrentSession()
        {
            bool wasUpdating = _updatingSessionList;
            _updatingSessionList = true;
            try
            {
                SessionList.SelectedItem = (SessionList.ItemsSource as IEnumerable<SessionSummary>)?
                    .FirstOrDefault(s => s.Id == _currentSessionId);
            }
            finally
            {
                _updatingSessionList = wasUpdating;
            }
        }

        private async void SessionList_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is SessionSummary summary) await OpenSessionAsync(summary.Id);
        }

        /// <summary>
        /// Anche la selezione da tastiera (Invio/Spazio) o dall'accessibilità apre la sessione.
        /// La selezione impostata dal codice (ricaricamento dell'elenco) viene ignorata:
        /// prima, ricaricando, la selezione poteva finire su un'altra sessione e aprirla.
        /// </summary>
        private async void SessionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_updatingSessionList) return;
            if (SessionList.SelectedItem is SessionSummary summary && summary.Id != _currentSessionId)
            {
                await OpenSessionAsync(summary.Id);
            }
        }

        private async Task OpenSessionAsync(string id)
        {
            NavigateTo(Routes.Session);
            if (RootFrame.Content is WelcomePage page) await page.OpenSessionAsync(id);
        }

        private async void DeleteSession_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not string id) return;
            var summary = (SessionList.ItemsSource as IEnumerable<SessionSummary>)?.FirstOrDefault(s => s.Id == id);

            var dialog = new ContentDialog
            {
                Title = "Eliminare la sessione?",
                Content = $"“{summary?.Title}” verrà eliminata definitivamente dalla cronologia.",
                PrimaryButtonText = "Elimina",
                CloseButtonText = "Annulla",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = Content.XamlRoot
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

            try
            {
                SessionStore.Delete(id);
            }
            catch (IOException)
            {
                return;
            }
            (RootFrame.Content as WelcomePage)?.OnSessionDeleted(id);
        }

        /// <summary>Mostra un testo accanto al titolo (es. il nome del progetto).</summary>
        public void SetSubtitle(string text)
        {
            AppTitleBar.Subtitle = text;
        }

        // ---- Navigazione ----

        private void NavList_ItemClick(object sender, ItemClickEventArgs e)
        {
            // Con ListViewItem dichiarati in XAML, ClickedItem è il loro contenuto:
            // risaliamo al ListViewItem per leggerne il Tag.
            var container = e.ClickedItem as ListViewItem ?? (e.ClickedItem as FrameworkElement)?.Parent as ListViewItem;
            string? tag = container?.Tag as string;
            // "+ Sessione" apre una sessione nuova, come "Nuova sessione" nel menu.
            if (tag == Routes.Session) NewSession_Click(sender, new RoutedEventArgs());
            else NavigateTo(tag);
        }

        /// <summary>Apre la pagina associata al tag (vedi <see cref="Routes"/>), se non è già quella mostrata.</summary>
        public void NavigateTo(string? tag)
        {
            var route = Routes.Find(tag);
            if (_currentTag == route.Tag) return;
            RootFrame.Navigate(route.PageType, route.Tag);
        }

        private string? _currentTag;

        private void RootFrame_Navigated(object sender, NavigationEventArgs e)
        {
            BackButton.IsEnabled = RootFrame.CanGoBack;
            ForwardButton.IsEnabled = RootFrame.CanGoForward;

            _currentTag = e.Parameter as string ?? Routes.Session;
            bool inSettings = _currentTag.StartsWith("settings/", StringComparison.Ordinal);
            bool inProvider = _currentTag is "settings/account" or "settings/local" or "settings/byok";

            // Nelle impostazioni la barra laterale mostra le sezioni al posto delle sessioni.
            SettingsSection.Visibility = inSettings ? Visibility.Visible : Visibility.Collapsed;
            SessionsHeader.Visibility = SessionsBody.Visibility = inSettings ? Visibility.Collapsed : Visibility.Visible;
            SettingsButton.Background = inSettings ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["ClineSelectedBrush"] : null;

            // Il gruppo Provider si apre quando si è in una delle sue pagine.
            var providerItems = new[] { AccountItem, LocalItem, ByokItem };
            foreach (var item in providerItems)
            {
                item.Visibility = inProvider ? Visibility.Visible : Visibility.Collapsed;
            }

            ProviderGroupItem.FontWeight = inProvider ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;

            // Tiene allineata la selezione della barra laterale con la pagina mostrata.
            // Va fatto dopo che il ListView ha applicato la selezione del clic
            // (cliccando "Provider" deve restare selezionato "Account Cline").
            string tag = _currentTag;
            DispatcherQueue.TryEnqueue(() =>
            {
                NavList.SelectedItem = NavList.Items.OfType<ListViewItem>().FirstOrDefault(i => (string)i.Tag == tag);
                SettingsNav.SelectedItem = inProvider
                    ? providerItems.First(i => (string)i.Tag == tag)
                    : SettingsNav.Items.OfType<ListViewItem>().FirstOrDefault(i => (string)i.Tag == tag);
            });
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            if (RootFrame.CanGoBack) RootFrame.GoBack();
        }

        private void Forward_Click(object sender, RoutedEventArgs e)
        {
            if (RootFrame.CanGoForward) RootFrame.GoForward();
        }

        // ---- Menu ----

        private void NewSession_Click(object sender, RoutedEventArgs e)
        {
            NavigateTo(Routes.Session);
            // La pagina della sessione resta in cache: "Nuova sessione" la svuota.
            (RootFrame.Content as WelcomePage)?.ResetConversation();
        }

        private void Schedule_Click(object sender, RoutedEventArgs e) => NavigateTo("schedule");

        private void Settings_Click(object sender, RoutedEventArgs e) => NavigateTo("settings/general");

        private async void OpenProject_Click(object sender, RoutedEventArgs e)
        {
            NavigateTo(Routes.Session);
            if (RootFrame.Content is WelcomePage page)
            {
                await page.PickProjectAsync();
            }
        }

        private void ToggleSidebar_Click(object sender, RoutedEventArgs e)
        {
            bool visible = ToggleSidebarItem.IsChecked;
            Sidebar.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            SidebarColumn.Width = visible ? new GridLength(260) : new GridLength(0);
        }

        private async void Docs_Click(object sender, RoutedEventArgs e)
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri("https://docs.cline.bot"));
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Exit();
        }

        private async void About_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title = "Informazioni su Cline",
                Content = "ClineWin — client Windows nativo per Cline.",
                CloseButtonText = "OK",
                XamlRoot = Content.XamlRoot
            };
            await dialog.ShowAsync();
        }
    }
}
