using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace ClineWin
{
    /// <summary>
    /// Account Cline: schede Panoramica / Utilizzo / Fatturazione e accesso con codice
    /// da confermare nel browser. Finché l'utente non accede tutte le schede mostrano
    /// la scheda di accesso, come nell'app originale.
    /// </summary>
    public sealed partial class ClineAccountPage : Page
    {
        private const string CreateAccountUrl = "https://app.cline.bot";

        // TODO: sostituire con il servizio che parla con il sidecar.
        private readonly IClineAuthService _auth = new DemoClineAuthService();

        private CancellationTokenSource? _signIn;

        public ClineAccountPage()
        {
            InitializeComponent();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            _signIn?.Cancel();
        }

        private void Tabs_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
        {
            TabDescription.Text = (sender.SelectedItem?.Tag as string) switch
            {
                "usage" => "Accedi per vedere l'utilizzo dei crediti per sessione e modello.",
                "billing" => "Accedi per gestire crediti, pagamenti e abbonamento ClinePass.",
                _ => "Collega il tuo account Cline per vedere crediti, utilizzo, fatturazione e organizzazioni."
            };
        }

        // ---- Accesso ----

        private async void SignIn_Click(object sender, RoutedEventArgs e)
        {
            if (_signIn != null) return;

            _signIn = new CancellationTokenSource();
            var token = _signIn.Token;
            ErrorText.Visibility = Visibility.Collapsed;
            SetSigningIn(true);

            try
            {
                var device = await _auth.StartSignInAsync(token);
                CodeRun.Text = device.UserCode;
                CodePanel.Visibility = Visibility.Visible;

                await _auth.WaitForCompletionAsync(token);
                // TODO: accesso riuscito → caricare utente, crediti e organizzazioni.
            }
            catch (OperationCanceledException)
            {
                // Annullato dall'utente o uscita dalla pagina.
            }
            catch (Exception ex)
            {
                ErrorText.Text = $"Accesso non riuscito: {ex.Message}";
                ErrorText.Visibility = Visibility.Visible;
            }
            finally
            {
                _signIn?.Dispose();
                _signIn = null;
                SetSigningIn(false);
            }
        }

        private async void Secondary_Click(object sender, RoutedEventArgs e)
        {
            if (_signIn != null)
            {
                _signIn.Cancel();
                return;
            }
            await Windows.System.Launcher.LaunchUriAsync(new Uri(CreateAccountUrl));
        }

        /// <summary>Passa dai pulsanti "Accedi / Crea account" a "Accesso in corso / Annulla" e viceversa.</summary>
        private void SetSigningIn(bool signingIn)
        {
            // Il pulsante resta "acceso" ma non cliccabile, come nell'originale.
            SignInButton.IsHitTestVisible = !signingIn;
            SignInButton.Opacity = signingIn ? 0.7 : 1;
            SignInIcon.Visibility = signingIn ? Visibility.Collapsed : Visibility.Visible;
            SignInProgress.Visibility = signingIn ? Visibility.Visible : Visibility.Collapsed;
            SignInProgress.IsActive = signingIn;
            SignInText.Text = signingIn ? "Accesso in corso" : "Accedi";

            SecondaryText.Text = signingIn ? "Annulla" : "Crea account";
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(SignInButton, SignInText.Text);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(SecondaryButton, SecondaryText.Text);
            ExternalIcon.Visibility = signingIn ? Visibility.Collapsed : Visibility.Visible;

            if (!signingIn)
            {
                CodePanel.Visibility = Visibility.Collapsed;
            }
        }
    }
}
