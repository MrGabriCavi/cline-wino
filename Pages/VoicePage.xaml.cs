using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ClineWin
{
    /// <summary>
    /// Input vocale. Per ora mostra lo stato vuoto dell'originale: la scelta del modello
    /// di trascrizione arriverà quando l'app leggerà i provider dal sidecar.
    /// </summary>
    public sealed partial class VoicePage : Page
    {
        public VoicePage()
        {
            InitializeComponent();
        }

        private void OpenProviders_Click(object sender, RoutedEventArgs e) => App.MainWindow?.NavigateTo("settings/byok");
    }
}
