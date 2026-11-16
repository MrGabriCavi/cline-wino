using Windows.Foundation.Collections;
using Windows.Storage;

namespace ClineWin
{
    /// <summary>
    /// Preferenze dell'app salvate nelle impostazioni locali del pacchetto.
    /// Se l'app gira senza identità di pacchetto le preferenze restano solo in memoria.
    /// </summary>
    public static class AppPreferences
    {
        private static readonly Dictionary<string, object> Fallback = new();

        private static IPropertySet? Store
        {
            get
            {
                try { return ApplicationData.Current.LocalSettings.Values; }
                catch (InvalidOperationException) { return null; }
            }
        }

        public static T Get<T>(string key, T defaultValue)
        {
            object? value = Store is { } s ? (s.TryGetValue(key, out var v) ? v : null)
                : Fallback.GetValueOrDefault(key);
            return value is T typed ? typed : defaultValue;
        }

        public static void Set<T>(string key, T value) where T : notnull
        {
            if (Store is { } s) s[key] = value;
            else Fallback[key] = value;
        }
    }
}
