using System.ComponentModel;
using System.Net.Http;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel.DataTransfer;

namespace ClineWin
{
    /// <summary>Un modello del catalogo "Modelli consigliati" (da lib/local-models.ts dell'app originale).</summary>
    public sealed partial class LocalModelInfo : INotifyPropertyChanged
    {
        public required string Name { get; init; }
        public required string OllamaId { get; init; }
        public required string LmStudioId { get; init; }
        public required double SizeGb { get; init; }
        public required int MemoryGb { get; init; }
        public required int ContextK { get; init; }
        public required string[] Tags { get; init; }
        public required string Description { get; init; }

        public Thickness TopBorder { get; set; } = new(0, 1, 0, 0);
        public string RuntimeId { get; set; } = "";

        public string Details => $"{RuntimeId} · download da {SizeGb:0.#} GB · richiede ~{MemoryGb} GB di memoria · contesto {ContextK}K";
        public string DownloadLabel => $"Scarica ({SizeGb:0.#} GB)";

        private bool _canDownload;
        public bool CanDownload
        {
            get => _canDownload;
            set { _canDownload = value; PropertyChanged?.Invoke(this, new(nameof(CanDownload))); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    /// <summary>
    /// Modelli locali: scelta tra Ollama e LM Studio, stato del runtime (controllato davvero
    /// sul server locale), istruzioni di installazione e catalogo dei modelli consigliati.
    /// Download, caricamento e "Usa con Cline" arriveranno con il collegamento al sidecar.
    /// </summary>
    public sealed partial class LocalModelsPage : Page
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(2) };

        private enum RuntimeState { Checking, Running, NotRunning, NotInstalled }

        private sealed record Runtime(
            string Id, string Name, string ProbeUrl, string DownloadUrl, string DownloadLabel,
            string Step1, string? Command, string Step2, string Step3, string Footer, string[] InstallPaths);

        private static readonly string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        private static readonly Runtime Ollama = new(
            "ollama", "Ollama", "http://localhost:11434/api/tags",
            "https://ollama.com/download/windows", "Scarica Ollama per Windows",
            "1. Installa Ollama. È gratuito, open source e fa girare i modelli sul tuo hardware.",
            null,
            "2. Apri Ollama. Funziona in background dall'area di notifica.",
            "3. Torna qui. Questa pagina rileva Ollama da sola, poi potrai scaricare un modello.",
            "I modelli caricati da qui restano in memoria finché non li scarichi. Un modello che Ollama carica da solo, quando arriva un messaggio, viene scaricato dopo qualche minuto di inattività (configurabile con OLLAMA_KEEP_ALIVE). L'URL del server si cambia in Provider → BYOK → Ollama.",
            [Path.Combine(LocalAppData, "Programs", "Ollama", "ollama.exe")]);

        private static readonly Runtime LmStudio = new(
            "lmstudio", "LM Studio", "http://localhost:1234/v1/models",
            "https://lmstudio.ai/download", "Scarica LM Studio",
            "1. Installa LM Studio. È gratuito, con il suo catalogo di modelli e la sua app di chat.",
            "irm https://lmstudio.ai/install.ps1 | iex",
            "2. Apri LM Studio una volta. Dopo, Cline potrà avviarlo per te.",
            "3. Torna qui. Questa pagina rileva LM Studio da sola, poi potrai scaricare un modello.",
            "I modelli caricati da qui restano in memoria finché non li scarichi. Un modello che LM Studio carica da solo, quando arriva un messaggio, usa la lunghezza di contesto predefinita di LM Studio e viene scaricato dopo un'ora di inattività. L'URL del server si cambia in Provider → BYOK → LM Studio.",
            [
                Path.Combine(LocalAppData, "Programs", "LM Studio", "LM Studio.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".lmstudio", "bin", "lms.exe"),
            ]);

        private static readonly LocalModelInfo[] Catalog =
        [
            new() { Name = "Qwen3.8 27B", OllamaId = "qwen3.8:27b", LmStudioId = "qwen/qwen3.8-27b", SizeGb = 18, MemoryGb = 23, ContextK = 64, Tags = ["Migliore", "Visione"],
                Description = "Il modello locale più forte per il coding agentico. Uso degli strumenti affidabile anche nelle attività lunghe." },
            new() { Name = "Laguna XS 2.1", OllamaId = "laguna-xs-2.1", LmStudioId = "poolside/laguna-xs-2.1", SizeGb = 20, MemoryGb = 24, ContextK = 64, Tags = ["Veloce"],
                Description = "Modello mixture-of-experts di Poolside pensato per il coding su attività lunghe. Solo 3B parametri attivi, quindi è veloce." },
            new() { Name = "Qwen3.6 35B A3B", OllamaId = "qwen3.6:35b-a3b", LmStudioId = "qwen/qwen3.6-35b-a3b", SizeGb = 24, MemoryGb = 26, ContextK = 64, Tags = ["Veloce", "Visione"],
                Description = "Qwen mixture-of-experts veloce con un buon coding agentico. Ottimo quando conta più la velocità della profondità." },
            new() { Name = "Devstral Small 2", OllamaId = "devstral-small-2:24b", LmStudioId = "mistralai/devstral-small-2-2512", SizeGb = 15, MemoryGb = 21, ContextK = 32, Tags = ["Visione"],
                Description = "Il modello di coding agentico di Mistral, ottimizzato per esplorare codebase e modificare molti file." },
            new() { Name = "gpt-oss 20B", OllamaId = "gpt-oss:20b", LmStudioId = "openai/gpt-oss-20b", SizeGb = 14, MemoryGb = 16, ContextK = 64, Tags = ["Veloce"],
                Description = "Il modello di ragionamento open-weight di OpenAI. Leggero sulla memoria e rapido, ideale per macchine da 16-24 GB." },
            new() { Name = "Qwen3-Coder-Next", OllamaId = "qwen3-coder-next", LmStudioId = "qwen/qwen3-coder-next", SizeGb = 52, MemoryGb = 56, ContextK = 64, Tags = ["Veloce"],
                Description = "Coder mixture-of-experts da 80B con 3B parametri attivi, per macchine con 64 GB o più." },
            new() { Name = "Qwen3 8B", OllamaId = "qwen3:8b", LmStudioId = "qwen/qwen3-8b", SizeGb = 5.2, MemoryGb = 10, ContextK = 32, Tags = ["Leggero"],
                Description = "Abbastanza piccolo per macchine da 8-16 GB. Gestisce modifiche semplici, ma fatica con le attività più lunghe." },
        ];

        private readonly Dictionary<string, RuntimeState> _states = new() { ["ollama"] = RuntimeState.Checking, ["lmstudio"] = RuntimeState.Checking };
        private Runtime _selected = Ollama;

        public LocalModelsPage()
        {
            InitializeComponent();
            for (int i = 0; i < Catalog.Length; i++)
            {
                Catalog[i].TopBorder = new Thickness(0, i == 0 ? 0 : 1, 0, 0);
            }
            ModelList.ItemsSource = Catalog;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            (AppPreferences.Get("localRuntime", "ollama") == "lmstudio" ? LmStudioCard : OllamaCard).IsChecked = true;
            await CheckRuntimesAsync();
        }

        private void Runtime_Checked(object sender, RoutedEventArgs e)
        {
            _selected = (string)((FrameworkElement)sender).Tag == "lmstudio" ? LmStudio : Ollama;
            AppPreferences.Set("localRuntime", _selected.Id);
            UpdateView();
        }

        private async void CheckAgain_Click(object sender, RoutedEventArgs e) => await CheckRuntimesAsync();

        /// <summary>Interroga i server locali di Ollama e LM Studio; se non rispondono controlla se sono installati.</summary>
        private async Task CheckRuntimesAsync()
        {
            _states["ollama"] = _states["lmstudio"] = RuntimeState.Checking;
            UpdateView();

            var results = await Task.WhenAll(ProbeAsync(Ollama), ProbeAsync(LmStudio));
            _states["ollama"] = results[0];
            _states["lmstudio"] = results[1];
            UpdateView();
        }

        private static async Task<RuntimeState> ProbeAsync(Runtime runtime)
        {
            try
            {
                using var response = await Http.GetAsync(runtime.ProbeUrl);
                if (response.IsSuccessStatusCode) return RuntimeState.Running;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                // Server non raggiungibile: si prosegue con il controllo dell'installazione.
            }
            return runtime.InstallPaths.Any(File.Exists) ? RuntimeState.NotRunning : RuntimeState.NotInstalled;
        }

        private void UpdateView()
        {
            SetStatus(OllamaDot, OllamaStatus, _states["ollama"]);
            SetStatus(LmStudioDot, LmStudioStatus, _states["lmstudio"]);

            var state = _states[_selected.Id];
            StatusDot.Fill = DotBrush(state);
            StatusText.Text = state switch
            {
                RuntimeState.Checking => $"Ricerca di {_selected.Name} in corso…",
                RuntimeState.Running => $"{_selected.Name} è in esecuzione",
                RuntimeState.NotRunning => $"{_selected.Name} è installato ma non in esecuzione. Avvialo per scaricare ed eseguire modelli.",
                _ => $"{_selected.Name} non è installato",
            };

            InstallCard.Visibility = state == RuntimeState.NotInstalled ? Visibility.Visible : Visibility.Collapsed;
            InstallStep1.Text = _selected.Step1;
            InstallStep2.Text = _selected.Step2;
            InstallStep3.Text = _selected.Step3;
            DownloadButton.Content = _selected.DownloadLabel;
            CommandPanel.Visibility = _selected.Command != null ? Visibility.Visible : Visibility.Collapsed;
            CommandText.Text = _selected.Command ?? "";
            CopyButton.Content = "Copia";
            FooterNote.Text = _selected.Footer;

            foreach (var model in Catalog)
            {
                model.RuntimeId = _selected == Ollama ? model.OllamaId : model.LmStudioId;
                // Come nell'originale, il download è possibile solo con il runtime raggiungibile.
                model.CanDownload = state == RuntimeState.Running;
            }
            // Ricrea le righe per aggiornare gli id mostrati.
            ModelList.ItemsSource = null;
            ModelList.ItemsSource = Catalog;
        }

        private static void SetStatus(Microsoft.UI.Xaml.Shapes.Ellipse dot, TextBlock text, RuntimeState state)
        {
            dot.Fill = DotBrush(state);
            dot.Visibility = state == RuntimeState.Checking ? Visibility.Collapsed : Visibility.Visible;
            text.Text = state switch
            {
                RuntimeState.Running => "In esecuzione",
                RuntimeState.NotRunning => "Non in esecuzione",
                RuntimeState.NotInstalled => "Non installato",
                _ => "",
            };
        }

        private static Brush DotBrush(RuntimeState state) => new SolidColorBrush(state switch
        {
            RuntimeState.Running => ColorHelper.FromArgb(0xFF, 0x3D, 0xD6, 0x8C),
            RuntimeState.NotRunning => ColorHelper.FromArgb(0xFF, 0xF5, 0xB0, 0x41),
            _ => ColorHelper.FromArgb(0xFF, 0x73, 0x73, 0x7D),
        });

        private async void Download_Click(object sender, RoutedEventArgs e)
        {
            await Windows.System.Launcher.LaunchUriAsync(new Uri(_selected.DownloadUrl));
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            var package = new DataPackage();
            package.SetText(CommandText.Text);
            Clipboard.SetContent(package);
            CopyButton.Content = "Copiato";
        }
    }
}
