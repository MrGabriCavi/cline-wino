using Windows.ApplicationModel;

namespace ClineWin
{
    /// <summary>Informazioni sulla build dell'app.</summary>
    public static class AppInfo
    {
        /// <summary>Versione del pacchetto MSIX, oppure dell'assembly se l'app gira senza pacchetto.</summary>
        public static string Version
        {
            get
            {
                try
                {
                    var v = Package.Current.Id.Version;
                    return $"{v.Major}.{v.Minor}.{v.Build}";
                }
                catch (InvalidOperationException)
                {
                    return typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "sconosciuta";
                }
            }
        }
    }
}
