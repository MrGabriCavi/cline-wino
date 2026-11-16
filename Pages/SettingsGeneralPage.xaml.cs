using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Windows.AppNotifications;

namespace ClineWin
{
    /// <summary>
    /// Impostazioni generali: notifiche, tema, dimensione testo, colore d'accento,
    /// icona dell'app e aggiornamento della CLI. Per ora le scelte vengono solo salvate;
    /// applicarle all'interfaccia è il passo successivo.
    /// </summary>
    public sealed partial class SettingsGeneralPage : Page
    {
        private static readonly (string Key, string Title, string Description)[] NotificationEvents =
        [
            ("taskCompleted", "Attività completata", "Quando Cline termina un'attività o un turno."),
            ("approvalNeeded", "Approvazione richiesta", "Quando uno strumento è in attesa della tua approvazione."),
            ("questionAsked", "Domanda posta", "Quando Cline ha bisogno di una risposta per continuare."),
            ("sessionError", "Errore di sessione", "Quando un'attività si interrompe per un errore."),
        ];

        // Evita di salvare le preferenze mentre i controlli vengono inizializzati.
        private bool _loading = true;

        public SettingsGeneralPage()
        {
            InitializeComponent();
            BuildNotificationRows();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            _loading = true;

            FontSizeSlider.Value = AppPreferences.Get("fontSize", 15d);
            KeepCliUpdatedSwitch.IsOn = AppPreferences.Get("keepCliUpdated", true);
            Check(AccentPanel, AppPreferences.Get("accent", "violet"));
            Check(AppIconPanel, AppPreferences.Get("appIcon", "Midnight"));
            UpdateNotificationPermission();

            _loading = false;
        }

        private static void Check(Panel panel, string tag)
        {
            var target = panel.Children.OfType<RadioButton>().FirstOrDefault(r => (string)r.Tag == tag)
                ?? panel.Children.OfType<RadioButton>().First();
            target.IsChecked = true;
        }

        // ---- Notifiche ----

        private void BuildNotificationRows()
        {
            foreach (var (key, title, description) in NotificationEvents)
            {
                var row = new Grid { Style = (Style)Resources["NotificationRowStyle"] };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });

                var text = new StackPanel();
                text.Children.Add(new TextBlock { Text = title, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
                text.Children.Add(new TextBlock
                {
                    Text = description,
                    FontSize = 12,
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["ClineText2Brush"],
                    TextWrapping = TextWrapping.Wrap
                });
                row.Children.Add(text);

                row.Children.Add(MakeToggle($"notify.{key}", true, 1));
                row.Children.Add(MakeToggle($"sound.{key}", false, 2));
                NotificationTable.Children.Add(row);
            }
        }

        private ToggleSwitch MakeToggle(string prefKey, bool defaultValue, int column)
        {
            var toggle = new ToggleSwitch
            {
                Style = (Style)Application.Current.Resources["ClineBareToggleStyle"],
                IsOn = AppPreferences.Get(prefKey, defaultValue),
                VerticalAlignment = VerticalAlignment.Center
            };
            toggle.Toggled += (_, _) => AppPreferences.Set(prefKey, toggle.IsOn);
            Grid.SetColumn(toggle, column);
            return toggle;
        }

        private void UpdateNotificationPermission()
        {
            bool blocked;
            try
            {
                blocked = AppNotificationManager.Default.Setting != AppNotificationSetting.Enabled;
            }
            catch (Exception)
            {
                // Senza identità di pacchetto lo stato non è leggibile: non mostriamo l'avviso.
                blocked = false;
            }
            NotificationsBlockedText.Visibility = blocked ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void CheckPermission_Click(object sender, RoutedEventArgs e)
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:notifications"));
            UpdateNotificationPermission();
        }

        // ---- Dimensione testo ----

        private void FontSizeSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (FontSizeText == null) return;
            FontSizeText.Text = $"{(int)e.NewValue}px";
            if (!_loading) AppPreferences.Set("fontSize", e.NewValue);
        }

        private void FontSmaller_Click(object sender, RoutedEventArgs e) => FontSizeSlider.Value -= 1;

        private void FontLarger_Click(object sender, RoutedEventArgs e) => FontSizeSlider.Value += 1;

        // ---- Accento, icona, CLI ----

        private void Accent_Checked(object sender, RoutedEventArgs e)
        {
            if (!_loading && sender is RadioButton r) AppPreferences.Set("accent", (string)r.Tag);
        }

        private void AppIcon_Checked(object sender, RoutedEventArgs e)
        {
            if (!_loading && sender is RadioButton r) AppPreferences.Set("appIcon", (string)r.Tag);
        }

        private void KeepCliUpdated_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_loading) AppPreferences.Set("keepCliUpdated", KeepCliUpdatedSwitch.IsOn);
        }
    }
}
