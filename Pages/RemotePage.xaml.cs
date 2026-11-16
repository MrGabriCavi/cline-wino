using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ClineWin
{
    /// <summary>Un host SSH salvato.</summary>
    public sealed record SshHost(string Name, string Host, string? User, int? Port, string? IdentityFile)
    {
        public string Destination => (User is { Length: > 0 } ? $"{User}@" : "") + Host + (Port is { } p ? $":{p}" : "");
    }

    [JsonSerializable(typeof(List<SshHost>))]
    internal sealed partial class SshHostJsonContext : JsonSerializerContext;

    /// <summary>
    /// Ambienti remoti: elenco degli host SSH e modulo per aggiungerli.
    /// Gli host vengono salvati nelle preferenze dell'app; connessione e test
    /// arriveranno con il collegamento al sidecar (RemoteEnvironmentService di @cline/core).
    /// </summary>
    public sealed partial class RemotePage : Page
    {
        private const string PrefKey = "remoteHosts";

        private readonly ObservableCollection<SshHost> _hosts = new(Load());
        private SshHost? _editing;

        public RemotePage()
        {
            InitializeComponent();
            HostList.ItemsSource = _hosts;
            RefreshList();
        }

        private static List<SshHost> Load()
        {
            try
            {
                return JsonSerializer.Deserialize(AppPreferences.Get(PrefKey, "[]"), SshHostJsonContext.Default.ListSshHost) ?? [];
            }
            catch (JsonException)
            {
                return [];
            }
        }

        private void Persist() =>
            AppPreferences.Set(PrefKey, JsonSerializer.Serialize(_hosts.ToList(), SshHostJsonContext.Default.ListSshHost));

        private void RefreshList()
        {
            HostCount.Text = _hosts.Count.ToString();
            EmptyHostsText.Visibility = _hosts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // ---- Modulo ----

        private void NewHost_Click(object sender, RoutedEventArgs e)
        {
            HostList.SelectedItem = null;
            ShowHost(null);
        }

        private void HostList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (HostList.SelectedItem is SshHost host) ShowHost(host);
        }

        private void ShowHost(SshHost? host)
        {
            _editing = host;
            FormTitle.Text = host?.Name ?? "Nuovo host";
            NameBox.Text = host?.Name ?? "";
            HostBox.Text = host?.Host ?? "";
            UserBox.Text = host?.User ?? "";
            PortBox.Text = host?.Port?.ToString() ?? "";
            IdentityBox.Text = host?.IdentityFile ?? "";

            // Come nell'originale, host, utente e porta non si cambiano dopo il salvataggio.
            bool saved = host != null;
            HostBox.IsEnabled = UserBox.IsEnabled = PortBox.IsEnabled = !saved;
            LockedNote.Visibility = DeleteButton.Visibility = saved ? Visibility.Visible : Visibility.Collapsed;
            SaveButton.Content = saved ? "Salva" : "Aggiungi";
            ValidationText.Visibility = Visibility.Collapsed;
            SaveButton.IsEnabled = false;
        }

        private void Field_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (SaveButton == null) return;
            var current = BuildHost(validate: false);
            SaveButton.IsEnabled = current != null && current != _editing;
        }

        /// <summary>Crea l'host dai campi; con validate mostra gli errori come nell'originale.</summary>
        private SshHost? BuildHost(bool validate)
        {
            string? error = null;
            int? port = null;
            if (NameBox.Text.Trim().Length == 0) error = "Il nome è obbligatorio.";
            else if (HostBox.Text.Trim().Length == 0) error = "L'host SSH è obbligatorio.";
            else if (PortBox.Text.Trim().Length > 0)
            {
                if (int.TryParse(PortBox.Text.Trim(), out int p) && p is >= 1 and <= 65535) port = p;
                else error = "La porta deve essere un numero intero tra 1 e 65535.";
            }

            if (validate)
            {
                ValidationText.Text = error ?? "";
                ValidationText.Visibility = error != null ? Visibility.Visible : Visibility.Collapsed;
            }
            if (error != null) return null;

            static string? Optional(string s) => s.Trim().Length > 0 ? s.Trim() : null;
            return new SshHost(NameBox.Text.Trim(), HostBox.Text.Trim(), Optional(UserBox.Text), port, Optional(IdentityBox.Text));
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var host = BuildHost(validate: true);
            if (host == null) return;

            if (_editing != null)
            {
                _hosts[_hosts.IndexOf(_editing)] = host;
            }
            else
            {
                _hosts.Add(host);
            }
            Persist();
            RefreshList();
            HostList.SelectedItem = host;
            ShowHost(host);
        }

        private async void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (_editing == null) return;
            var dialog = new ContentDialog
            {
                Title = "Eliminare l'host SSH?",
                Content = $"Eliminare “{_editing.Name}” dagli ambienti remoti? I progetti e i dati delle sessioni di Cline restano sull'host remoto.",
                PrimaryButtonText = "Elimina",
                CloseButtonText = "Annulla",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

            _hosts.Remove(_editing);
            Persist();
            RefreshList();
            ShowHost(null);
        }
    }
}
