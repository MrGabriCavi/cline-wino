using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Windows.Storage;

namespace ClineWin
{
    /// <summary>Un'attività pianificata: un prompt che l'agente esegue da solo a un orario.</summary>
    public sealed class ScheduledTask
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "";
        public string Prompt { get; set; } = "";

        /// <summary>"once" (una volta), "daily" (ogni giorno) o "weekly" (giorni scelti).</summary>
        public string Frequency { get; set; } = "daily";
        /// <summary>Per "once": data in formato yyyy-MM-dd.</summary>
        public string? Date { get; set; }
        /// <summary>Ora locale in formato HH:mm.</summary>
        public string Time { get; set; } = "09:00";
        /// <summary>Per "weekly": giorni della settimana (0 = domenica … 6 = sabato).</summary>
        public List<int> Days { get; set; } = [];

        public string Provider { get; set; } = ProviderCatalog.OpenRouter;
        public string Model { get; set; } = "";
        public string? ProjectPath { get; set; }
        public bool Enabled { get; set; } = true;

        /// <summary>Riferimento per la prossima esecuzione: creazione/modifica o ultima esecuzione.</summary>
        public DateTimeOffset Anchor { get; set; } = DateTimeOffset.Now;
        public DateTimeOffset? LastRun { get; set; }
        public string? LastStatus { get; set; }
        public string? LastSessionId { get; set; }

        private static readonly CultureInfo It = CultureInfo.GetCultureInfo("it-IT");

        private TimeSpan TimeOfDay =>
            TimeSpan.TryParseExact(Time, @"hh\:mm", CultureInfo.InvariantCulture, out var t) ? t : new TimeSpan(9, 0, 0);

        /// <summary>Prossima esecuzione dopo <paramref name="after"/>, oppure null se non ce ne sono più.</summary>
        public DateTimeOffset? NextRunAfter(DateTimeOffset after)
        {
            var local = after.ToLocalTime();
            switch (Frequency)
            {
                case "once":
                    if (!DateTime.TryParseExact(Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)) return null;
                    var at = new DateTimeOffset(day.Date + TimeOfDay, TimeZoneInfo.Local.GetUtcOffset(day.Date + TimeOfDay));
                    return at > after ? at : null;

                case "weekly" when Days.Count == 0:
                    return null;

                default:
                    for (int offset = 0; offset <= 7; offset++)
                    {
                        var candidateDay = local.Date.AddDays(offset);
                        if (Frequency == "weekly" && !Days.Contains((int)candidateDay.DayOfWeek)) continue;
                        var candidate = candidateDay + TimeOfDay;
                        var withOffset = new DateTimeOffset(candidate, TimeZoneInfo.Local.GetUtcOffset(candidate));
                        if (withOffset > after) return withOffset;
                    }
                    return null;
            }
        }

        /// <summary>Prossima esecuzione prevista (null se disattivata o conclusa).</summary>
        public DateTimeOffset? NextRun => Enabled ? NextRunAfter(Anchor) : null;

        /// <summary>Descrizione dell'orario, es. "Ogni giorno alle 09:00" o "Lun, Mer alle 18:30".</summary>
        public string ScheduleText => Frequency switch
        {
            "once" when DateTime.TryParseExact(Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                => $"Il {d.ToString("d MMMM yyyy", It)} alle {Time}",
            "weekly" => (Days.Count == 7 ? "Tutti i giorni" : string.Join(", ", Days.Order().Select(DayLabel))) + $" alle {Time}",
            _ => $"Ogni giorno alle {Time}",
        };

        public string NextRunText => NextRun is { } next
            ? "Prossima: " + (next.Date == DateTimeOffset.Now.Date ? "oggi" : next.Date == DateTimeOffset.Now.Date.AddDays(1) ? "domani" : next.ToString("ddd d MMM", It)) + $" alle {next:HH:mm}"
            : Enabled ? "Nessuna esecuzione futura" : "Disattivata";

        public string LastRunText => LastRun is { } last
            ? $"Ultima: {last.ToString("d MMM HH:mm", It)} · {LastStatus}"
            : "Mai eseguita";

        // Lunedì per primo, come nel calendario italiano.
        public static readonly int[] WeekOrder = [1, 2, 3, 4, 5, 6, 0];
        public static string DayLabel(int day) => It.DateTimeFormat.AbbreviatedDayNames[day] is var s && s.Length > 0
            ? char.ToUpper(s[0], It) + s[1..].TrimEnd('.')
            : day.ToString(CultureInfo.InvariantCulture);
    }

    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
    [JsonSerializable(typeof(List<ScheduledTask>))]
    internal sealed partial class ScheduledTaskJsonContext : JsonSerializerContext;

    /// <summary>Attività pianificate salvate in schedules.json nella cartella dati dell'app.</summary>
    public static class ScheduledTaskStore
    {
        public static event Action? Changed;

        private static readonly string FilePath = Path.Combine(DataFolder(), "schedules.json");

        private static string DataFolder()
        {
            try { return ApplicationData.Current.LocalFolder.Path; }
            catch (InvalidOperationException)
            {
                string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClineWin");
                Directory.CreateDirectory(folder);
                return folder;
            }
        }

        public static List<ScheduledTask> Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return [];
                return JsonSerializer.Deserialize(File.ReadAllText(FilePath), ScheduledTaskJsonContext.Default.ListScheduledTask) ?? [];
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                return [];
            }
        }

        public static void Save(List<ScheduledTask> tasks)
        {
            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(tasks, ScheduledTaskJsonContext.Default.ListScheduledTask));
            File.Move(temp, FilePath, overwrite: true);
            Changed?.Invoke();
        }

        /// <summary>Aggiunge o sostituisce l'attività con lo stesso Id.</summary>
        public static void Upsert(ScheduledTask task)
        {
            var tasks = Load();
            int index = tasks.FindIndex(t => t.Id == task.Id);
            if (index >= 0) tasks[index] = task; else tasks.Add(task);
            Save(tasks);
        }

        public static void Delete(string id)
        {
            var tasks = Load();
            tasks.RemoveAll(t => t.Id == id);
            Save(tasks);
        }
    }
}
