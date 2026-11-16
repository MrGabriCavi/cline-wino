using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ClineWin
{
    /// <summary>Informazioni: versione, aggiornamenti, segnalazioni e note di rilascio.</summary>
    public sealed partial class AboutPage : Page
    {
        private const string IssuesUrl = "https://github.com/cline/cline/issues/new/choose";
        private const string ChangelogUrl = "https://github.com/cline/cline/blob/main/apps/examples/desktop-app/CHANGELOG.md";

        public AboutPage()
        {
            InitializeComponent();
            VersionText.Text = $"v{AppInfo.Version}";
        }

        private void CheckUpdates_Click(object sender, RoutedEventArgs e)
        {
            // TODO: aggiornamenti automatici (es. App Installer / feed MSIX) quando ci sarà un canale di rilascio.
            UpdateStatus.Text = "Il controllo degli aggiornamenti non è ancora disponibile.";
        }

        private async void Issues_Click(object sender, RoutedEventArgs e) =>
            await Windows.System.Launcher.LaunchUriAsync(new Uri(IssuesUrl));

        private async void Changelog_Click(object sender, RoutedEventArgs e) =>
            await Windows.System.Launcher.LaunchUriAsync(new Uri(ChangelogUrl));
    }
}
