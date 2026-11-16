namespace ClineWin
{
    /// <summary>Codice da confermare nel browser durante l'accesso (device authorization).</summary>
    public sealed record DeviceSignIn(string UserCode, Uri VerificationUri);

    /// <summary>
    /// Accesso all'account Cline. Nell'app originale il flusso passa dal sidecar
    /// (comandi cline_account / login del provider "cline"): il sidecar chiede un codice,
    /// apre il browser e attende che l'utente lo confermi.
    /// </summary>
    public interface IClineAuthService
    {
        /// <summary>Avvia l'accesso e restituisce il codice da mostrare all'utente.</summary>
        Task<DeviceSignIn> StartSignInAsync(CancellationToken cancellationToken);

        /// <summary>Attende che l'utente confermi il codice nel browser.</summary>
        Task WaitForCompletionAsync(CancellationToken cancellationToken);
    }

    /// <summary>
    /// Implementazione dimostrativa finché l'app non è collegata al sidecar:
    /// restituisce un codice fittizio e non completa mai l'accesso (si può solo annullare).
    /// </summary>
    public sealed class DemoClineAuthService : IClineAuthService
    {
        public async Task<DeviceSignIn> StartSignInAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(700, cancellationToken);
            return new DeviceSignIn("DEMO-CODE", new Uri("https://app.cline.bot"));
        }

        public Task WaitForCompletionAsync(CancellationToken cancellationToken) =>
            Task.Delay(Timeout.Infinite, cancellationToken);
    }
}
