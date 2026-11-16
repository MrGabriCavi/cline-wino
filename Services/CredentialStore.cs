using Windows.Security.Credentials;

namespace ClineWin
{
    /// <summary>
    /// Chiavi API dei provider, salvate in Gestione credenziali di Windows
    /// (PasswordVault, cifrato per l'utente). Una voce per provider.
    /// </summary>
    public static class CredentialStore
    {
        private const string Resource = "ClineWin";

        public static string? GetApiKey(string provider)
        {
            try
            {
                var credential = new PasswordVault().Retrieve(Resource, provider);
                credential.RetrievePassword();
                return string.IsNullOrEmpty(credential.Password) ? null : credential.Password;
            }
            catch (Exception)
            {
                // Retrieve lancia un'eccezione se la voce non esiste.
                return null;
            }
        }

        public static bool HasApiKey(string provider) => GetApiKey(provider) != null;

        public static void SetApiKey(string provider, string key)
        {
            RemoveApiKey(provider);
            new PasswordVault().Add(new PasswordCredential(Resource, provider, key));
        }

        public static void RemoveApiKey(string provider)
        {
            try
            {
                var vault = new PasswordVault();
                vault.Remove(vault.Retrieve(Resource, provider));
            }
            catch (Exception)
            {
                // Nessuna chiave salvata.
            }
        }

        /// <summary>Numero di provider con una chiave salvata.</summary>
        public static int Count()
        {
            try
            {
                return new PasswordVault().FindAllByResource(Resource).Count;
            }
            catch (Exception)
            {
                return 0;
            }
        }
    }
}
