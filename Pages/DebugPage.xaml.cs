using System.Runtime.InteropServices;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace ClineWin
{
    /// <summary>
    /// Debug: esporta un file di diagnostica (informazioni sull'app e coda del log di Cline)
    /// e apre la cartella dei log condivisa con CLI e app desktop (~/.cline/data/logs).
    /// </summary>
    public sealed partial class DebugPage : Page
    {
        private const int LogLines = 300;

        private static readonly string LogDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cline", "data", "logs");

        public DebugPage()
        {
            InitializeComponent();
            LogPathText.Text = LogDir;
            OpenLogsButton.IsEnabled = Directory.Exists(LogDir);
            if (!OpenLogsButton.IsEnabled)
            {
                LogPathText.Text += "  (non ancora creata)";
            }
        }

        private async void Export_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.Desktop,
                SuggestedFileName = $"clinewin-diagnostica-{DateTime.Now:yyyyMMdd-HHmmss}"
            };
            picker.FileTypeChoices.Add("File di testo", [".txt"]);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));

            StorageFile? file = await picker.PickSaveFileAsync();
            if (file == null) return;

            try
            {
                string report = await Task.Run(BuildReport);
                await FileIO.WriteTextAsync(file, report);
                Show(InfoBarSeverity.Success, "Diagnostica esportata", file.Path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Show(InfoBarSeverity.Error, "Esportazione non riuscita", ex.Message);
            }
        }

        private static string BuildReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine("ClineWin — diagnostica");
            sb.AppendLine($"Data: {DateTimeOffset.Now:O}");
            sb.AppendLine($"Versione app: {AppInfo.Version}");
            sb.AppendLine($"Sistema: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
            sb.AppendLine($"Runtime: {RuntimeInformation.FrameworkDescription}");
            sb.AppendLine($"Lingua: {System.Globalization.CultureInfo.CurrentUICulture.Name}");
            sb.AppendLine();

            string log = Path.Combine(LogDir, "code.log");
            sb.AppendLine($"--- Ultime {LogLines} righe di {log} ---");
            if (File.Exists(log))
            {
                // Il log può essere in uso da Cline: lo si apre in sola lettura condivisa.
                using var stream = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                var tail = new Queue<string>(LogLines);
                while (reader.ReadLine() is { } line)
                {
                    if (tail.Count == LogLines) tail.Dequeue();
                    tail.Enqueue(line);
                }
                foreach (var line in tail) sb.AppendLine(line);
            }
            else
            {
                sb.AppendLine("(nessun log trovato)");
            }
            return sb.ToString();
        }

        private async void OpenLogs_Click(object sender, RoutedEventArgs e)
        {
            await Windows.System.Launcher.LaunchFolderPathAsync(LogDir);
        }

        private void Show(InfoBarSeverity severity, string title, string message)
        {
            ResultBar.Severity = severity;
            ResultBar.Title = title;
            ResultBar.Message = message;
            ResultBar.IsOpen = true;
        }
    }
}
