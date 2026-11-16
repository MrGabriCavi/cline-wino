namespace ClineWin
{
    /// <summary>
    /// Elenco delle pagine dell'app. Ogni pagina ha un tag (usato come parametro di
    /// navigazione e come Tag delle voci della barra laterale) e un titolo.
    /// </summary>
    public static class Routes
    {
        public const string Session = "session";

        public sealed record Route(string Tag, Type PageType, string Title);

        private static readonly Route[] All =
        [
            new(Session, typeof(WelcomePage), "Sessione"),
            new("schedule", typeof(SchedulePage), "Pianificazione"),
            new("customize", typeof(PlaceholderPage), "Personalizza"),

            new("settings/general", typeof(SettingsGeneralPage), "Impostazioni"),
            new("settings/account", typeof(ClineAccountPage), "Account Cline"),
            new("settings/local", typeof(LocalModelsPage), "Modelli locali"),
            new("settings/byok", typeof(ByokPage), "BYOK"),
            new("settings/voice", typeof(VoicePage), "Voce"),
            new("settings/keybindings", typeof(KeybindingsPage), "Scorciatoie da tastiera"),
            new("settings/import", typeof(ImportPage), "Importa"),
            new("settings/remote", typeof(RemotePage), "Ambienti remoti"),
            new("settings/debug", typeof(DebugPage), "Debug"),
            new("settings/about", typeof(AboutPage), "Informazioni"),
        ];

        /// <summary>Restituisce la pagina del tag, o la sessione se il tag non esiste.</summary>
        public static Route Find(string? tag) => All.FirstOrDefault(r => r.Tag == tag) ?? All[0];
    }
}
