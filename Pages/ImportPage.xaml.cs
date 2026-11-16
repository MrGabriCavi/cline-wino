using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace ClineWin
{
    /// <summary>
    /// Importa: cerca sul computer le sessioni di Claude Code, Codex e opencode
    /// (stesse cartelle usate da @cline/core) e mostra quante ne ha trovate.
    /// L'importazione vera e propria arriverà con il collegamento al sidecar.
    /// </summary>
    public sealed partial class ImportPage : Page
    {
        private static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        private sealed record Source(string Name, Func<string> Scan);

        private static readonly Source[] Sources =
        [
            new("Claude Code", ScanClaudeCode),
            new("Codex", ScanCodex),
            new("opencode", ScanOpencode),
        ];

        private readonly List<TextBlock> _statusTexts = new();

        public ImportPage()
        {
            InitializeComponent();
            BuildRows();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            for (int i = 0; i < Sources.Length; i++)
            {
                var source = Sources[i];
                string status;
                try
                {
                    status = await Task.Run(source.Scan);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    status = "Scansione non riuscita";
                }
                _statusTexts[i].Text = status;
            }
        }

        private void BuildRows()
        {
            for (int i = 0; i < Sources.Length; i++)
            {
                var row = new StackPanel
                {
                    Padding = new Thickness(0, 14, 0, 14),
                    Spacing = 2,
                    BorderBrush = (Brush)Application.Current.Resources["ClineBorderBrush"],
                    BorderThickness = new Thickness(0, i == 0 ? 0 : 1, 0, 0)
                };
                row.Children.Add(new TextBlock { Text = Sources[i].Name, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
                var status = new TextBlock
                {
                    Text = "Ricerca in corso…",
                    FontSize = 13,
                    Foreground = (Brush)Application.Current.Resources["ClineText2Brush"]
                };
                _statusTexts.Add(status);
                row.Children.Add(status);
                SourceRows.Children.Add(row);
            }
        }

        private static string Describe(int count) => count switch
        {
            0 => "Nessuna sessione trovata",
            1 => "1 sessione trovata",
            _ => $"{count} sessioni trovate",
        };

        // $CLAUDE_CONFIG_DIR/projects o ~/.claude/projects: un file .jsonl per sessione in ogni cartella progetto.
        private static string ScanClaudeCode()
        {
            string root = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") ?? Path.Combine(Home, ".claude");
            string projects = Path.Combine(root, "projects");
            if (!Directory.Exists(projects)) return "Non rilevato su questo computer";

            int count = Directory.EnumerateDirectories(projects)
                .Sum(dir => Directory.EnumerateFiles(dir, "*.jsonl").Count());
            return Describe(count);
        }

        // $CODEX_HOME/sessions o ~/.codex/sessions: file .jsonl in sottocartelle per data.
        private static string ScanCodex()
        {
            string root = Environment.GetEnvironmentVariable("CODEX_HOME") ?? Path.Combine(Home, ".codex");
            string sessions = Path.Combine(root, "sessions");
            if (!Directory.Exists(sessions)) return "Non rilevato su questo computer";

            return Describe(Directory.EnumerateFiles(sessions, "*.jsonl", SearchOption.AllDirectories).Count());
        }

        // $XDG_DATA_HOME/opencode/opencode.db o ~/.local/share/opencode/opencode.db (database SQLite).
        private static string ScanOpencode()
        {
            string root = Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { } xdg
                ? Path.Combine(xdg, "opencode")
                : Path.Combine(Home, ".local", "share", "opencode");
            return File.Exists(Path.Combine(root, "opencode.db"))
                ? "Rilevato su questo computer"
                : "Non rilevato su questo computer";
        }
    }
}
