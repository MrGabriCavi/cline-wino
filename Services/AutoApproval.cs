namespace ClineWin
{
    /// <summary>Tipo di azione, per decidere se chiedere l'approvazione.</summary>
    public enum ToolCategory { Read, Edit, Command }

    /// <summary>
    /// Approvazione automatica per tipo di azione, salvata nelle preferenze.
    /// Di default la lettura è automatica; modifiche e comandi chiedono sempre.
    /// </summary>
    public static class AutoApproval
    {
        public static event Action? Changed;

        private static string Key(ToolCategory category) => $"autoApprove.{category.ToString().ToLowerInvariant()}";

        public static bool IsEnabled(ToolCategory category) =>
            AppPreferences.Get(Key(category), category == ToolCategory.Read);

        public static void Set(ToolCategory category, bool enabled)
        {
            AppPreferences.Set(Key(category), enabled);
            Changed?.Invoke();
        }

        public static string Label(ToolCategory category) => category switch
        {
            ToolCategory.Read => "lettura",
            ToolCategory.Edit => "modifiche",
            _ => "comandi",
        };

        /// <summary>Riepilogo per il pulsante del composer, es. "Auto: lettura, modifiche".</summary>
        public static string Summary()
        {
            var enabled = Enum.GetValues<ToolCategory>().Where(IsEnabled).Select(Label).ToList();
            return enabled.Count switch
            {
                0 => "Chiedi sempre",
                3 => "Auto: tutto",
                _ => "Auto: " + string.Join(", ", enabled),
            };
        }
    }
}
