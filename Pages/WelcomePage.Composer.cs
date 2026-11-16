using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.System;

namespace ClineWin
{
    /// <summary>Voce dei suggerimenti del composer: un file (per "@") o un comando (per "/").</summary>
    public sealed record ComposerSuggestion(string Glyph, string Label, string Detail, string Value, bool IsCommand);

    /// <summary>
    /// Il composer: "@" per menzionare i file del progetto, "/" per i comandi, allegati
    /// (file e immagini, anche incollati o trascinati), messaggi in coda e stima dei token.
    /// </summary>
    public sealed partial class WelcomePage
    {
        private const int MaxFileChars = 100_000;       // testo di un file allegato o menzionato
        private const long MaxImageBytes = 4 * 1024 * 1024;
        private const int MaxAttachments = 8;

        private static readonly HashSet<string> ImageTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp"
        };

        private readonly List<MessageAttachment> _attachments = [];

        // ---- Comandi "/" ----

        private static readonly ComposerSuggestion[] Commands =
        [
            new("", "/nuova", "Inizia una nuova sessione", "/nuova", true),
            new("", "/piano", "Passa alla modalità Piano", "/piano", true),
            new("", "/agisci", "Passa alla modalità Agisci", "/agisci", true),
        ];

        /// <summary>Esegue il messaggio se è un comando "/" conosciuto. Restituisce true se lo era.</summary>
        private bool TryRunCommand(string prompt)
        {
            switch (prompt.Trim().ToLowerInvariant())
            {
                case "/nuova":
                    ClearComposer();
                    ResetConversation();
                    return true;
                case "/piano":
                    ClearComposer();
                    SetMode(true, announce: true);
                    return true;
                case "/agisci":
                    ClearComposer();
                    SetMode(false, announce: true);
                    return true;
                default:
                    return false;
            }
        }

        // ---- Suggerimenti ----

        // Il testo che si sta completando: da dove parte (posizione di "@" o "/") e la ricerca.
        private (int Start, string Query, bool IsCommand)? _completion;

        private void PromptBox_SelectionChanged(object sender, RoutedEventArgs e) => UpdateSuggestions();

        /// <summary>
        /// Guarda il testo prima del cursore: "@parola" (senza spazi) apre i file del progetto,
        /// "/parola" all'inizio del messaggio apre i comandi. Altrimenti chiude i suggerimenti.
        /// </summary>
        private async void UpdateSuggestions()
        {
            string text = PromptBox.Text;
            int caret = Math.Clamp(PromptBox.SelectionStart, 0, text.Length);
            string left = text[..caret];

            int at = left.LastIndexOf('@');
            if (at >= 0 && (at == 0 || char.IsWhiteSpace(left[at - 1])) && !left[(at + 1)..].Any(char.IsWhiteSpace))
            {
                if (_projectPath == null)
                {
                    ShowSuggestions("Scegli un progetto per menzionare i suoi file.", []);
                    _completion = null;
                    return;
                }
                string query = left[(at + 1)..];
                _completion = (at, query, false);
                string root = _projectPath;
                var files = await FileIndex.GetAsync(root);
                // Nel frattempo il testo potrebbe essere cambiato: si mostra solo se la ricerca è ancora quella.
                if (_completion is not { IsCommand: false } current || current.Query != query) return;
                var matches = FileIndex.Search(files, query)
                    .Select(path =>
                    {
                        int slash = path.LastIndexOf('/');
                        return new ComposerSuggestion("", path[(slash + 1)..], slash > 0 ? path[..slash] : "", path, false);
                    })
                    .ToList();
                ShowSuggestions(matches.Count > 0 ? "File del progetto" : $"Nessun file corrisponde a “{query}”.", matches);
                return;
            }

            if (left.StartsWith('/') && !left.Any(char.IsWhiteSpace))
            {
                string query = left[1..];
                _completion = (0, query, true);
                var matches = Commands.Where(c => c.Label[1..].StartsWith(query, StringComparison.OrdinalIgnoreCase)).ToList();
                ShowSuggestions(matches.Count > 0 ? "Comandi" : "Nessun comando.", matches);
                return;
            }

            _completion = null;
            SuggestionPanel.Visibility = Visibility.Collapsed;
        }

        private void ShowSuggestions(string header, List<ComposerSuggestion> items)
        {
            SuggestionHeader.Text = header;
            SuggestionList.ItemsSource = items;
            SuggestionList.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (items.Count > 0) SuggestionList.SelectedIndex = 0;
            SuggestionPanel.Visibility = Visibility.Visible;
        }

        /// <summary>Frecce, Invio/Tab ed Esc mentre i suggerimenti sono aperti. True se il tasto è stato usato.</summary>
        private bool HandleSuggestionKey(VirtualKey key)
        {
            if (SuggestionPanel.Visibility != Visibility.Visible) return false;
            int count = (SuggestionList.ItemsSource as List<ComposerSuggestion>)?.Count ?? 0;
            switch (key)
            {
                case VirtualKey.Escape:
                    SuggestionPanel.Visibility = Visibility.Collapsed;
                    _completion = null;
                    return true;
                case VirtualKey.Down when count > 0:
                    SuggestionList.SelectedIndex = (SuggestionList.SelectedIndex + 1) % count;
                    SuggestionList.ScrollIntoView(SuggestionList.SelectedItem);
                    return true;
                case VirtualKey.Up when count > 0:
                    SuggestionList.SelectedIndex = (SuggestionList.SelectedIndex - 1 + count) % count;
                    SuggestionList.ScrollIntoView(SuggestionList.SelectedItem);
                    return true;
                case VirtualKey.Enter or VirtualKey.Tab when SuggestionList.SelectedItem is ComposerSuggestion item:
                    AcceptSuggestion(item);
                    return true;
                default:
                    return false;
            }
        }

        private void SuggestionList_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is ComposerSuggestion item) AcceptSuggestion(item);
        }

        /// <summary>Sostituisce "@ricerca" con "@percorso " (o esegue il comando) e riporta il cursore nel testo.</summary>
        private void AcceptSuggestion(ComposerSuggestion item)
        {
            if (_completion is not { } completion) return;
            SuggestionPanel.Visibility = Visibility.Collapsed;
            _completion = null;

            if (item.IsCommand)
            {
                TryRunCommand(item.Value);
                return;
            }

            string text = PromptBox.Text;
            int end = completion.Start + 1 + completion.Query.Length;
            string insert = "@" + item.Value + " ";
            PromptBox.Text = text[..completion.Start] + insert + text[Math.Min(end, text.Length)..];
            PromptBox.SelectionStart = completion.Start + insert.Length;
            PromptBox.Focus(FocusState.Programmatic);
        }

        // ---- Allegati ----

        private async void Attach_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
            picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));
            var files = await picker.PickMultipleFilesAsync();
            foreach (var file in files) await AddFileAsync(file);
        }

        private void PromptBox_DragOver(object sender, DragEventArgs e)
        {
            if (e.DataView.Contains(StandardDataFormats.StorageItems) || e.DataView.Contains(StandardDataFormats.Bitmap))
            {
                e.AcceptedOperation = DataPackageOperation.Copy;
                e.DragUIOverride.Caption = "Allega";
            }
        }

        private async void PromptBox_Drop(object sender, DragEventArgs e)
        {
            await AddFromDataViewAsync(e.DataView);
        }

        /// <summary>Incolla: un'immagine (es. uno screenshot) o dei file diventano allegati; il testo si incolla normalmente.</summary>
        private async void PromptBox_Paste(object sender, TextControlPasteEventArgs e)
        {
            var data = Clipboard.GetContent();
            if (data.Contains(StandardDataFormats.Bitmap) || data.Contains(StandardDataFormats.StorageItems))
            {
                e.Handled = true;
                await AddFromDataViewAsync(data);
            }
        }

        private async Task AddFromDataViewAsync(DataPackageView data)
        {
            if (data.Contains(StandardDataFormats.StorageItems))
            {
                foreach (var item in await data.GetStorageItemsAsync())
                {
                    if (item is StorageFile file) await AddFileAsync(file);
                }
            }
            else if (data.Contains(StandardDataFormats.Bitmap))
            {
                // Gli appunti di Windows danno spesso una bitmap (BMP/DIB), che molti modelli rifiutano:
                // la si converte sempre in PNG.
                var reference = await data.GetBitmapAsync();
                using var stream = await reference.OpenReadAsync();
                AddImage($"Immagine incollata {DateTime.Now:HH.mm.ss}.png", await ToPngAsync(stream), "image/png");
            }
        }

        private async Task AddFileAsync(StorageFile file)
        {
            if (_attachments.Count >= MaxAttachments)
            {
                ShowBar(InfoBarSeverity.Warning, $"Al massimo {MaxAttachments} allegati per messaggio.", null);
                return;
            }
            try
            {
                string extension = Path.GetExtension(file.Name);
                if (ImageTypes.Contains(extension))
                {
                    using var stream = await file.OpenReadAsync();
                    // PNG, JPEG, GIF e WebP passano così come sono; gli altri formati (BMP) diventano PNG.
                    var (bytes, mediaType) = WebFormats.TryGetValue(extension, out var type)
                        ? (await ReadAllAsync(stream), type)
                        : (await ToPngAsync(stream), "image/png");
                    AddImage(file.Name, bytes, mediaType);
                    return;
                }

                string? text = ReadTextFile(file.Path, out string? problem);
                if (text == null)
                {
                    ShowBar(InfoBarSeverity.Warning, $"“{file.Name}” non è stato allegato: {problem}", null);
                    return;
                }
                // Dentro il progetto si usa il percorso relativo, come per "@".
                string name = _projectPath != null && file.Path.StartsWith(_projectPath, StringComparison.OrdinalIgnoreCase)
                    ? Path.GetRelativePath(_projectPath, file.Path).Replace('\\', '/')
                    : file.Name;
                _attachments.Add(new MessageAttachment("file", name, text));
                RenderAttachments();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ShowBar(InfoBarSeverity.Warning, $"Impossibile leggere “{file.Name}”: {ex.Message}", null);
            }
        }

        private void AddImage(string name, byte[] bytes, string mediaType)
        {
            if (bytes.Length > MaxImageBytes)
            {
                ShowBar(InfoBarSeverity.Warning, $"“{name}” supera i {MaxImageBytes / 1024 / 1024} MB e non è stata allegata.", null);
                return;
            }
            if (_attachments.Count >= MaxAttachments) return;
            _attachments.Add(new MessageAttachment("image", name, DataUrl: $"data:{mediaType};base64,{Convert.ToBase64String(bytes)}"));
            RenderAttachments();
        }

        // Formati accettati dai modelli che vedono le immagini, con il tipo da dichiarare nel data URL.
        private static readonly Dictionary<string, string> WebFormats = new(StringComparer.OrdinalIgnoreCase)
        {
            [".png"] = "image/png",
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".gif"] = "image/gif",
            [".webp"] = "image/webp",
        };

        /// <summary>Decodifica un'immagine (qualsiasi formato supportato da Windows) e la ricodifica in PNG.</summary>
        private static async Task<byte[]> ToPngAsync(IRandomAccessStream input)
        {
            var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(input);
            using var pixels = await decoder.GetSoftwareBitmapAsync(
                Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied);
            using var output = new InMemoryRandomAccessStream();
            var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, output);
            encoder.SetSoftwareBitmap(pixels);
            await encoder.FlushAsync();
            return await ReadAllAsync(output);
        }

        private static async Task<byte[]> ReadAllAsync(IRandomAccessStream stream)
        {
            using var reader = new DataReader(stream.GetInputStreamAt(0));
            uint size = (uint)stream.Size;
            await reader.LoadAsync(size);
            var bytes = new byte[size];
            reader.ReadBytes(bytes);
            return bytes;
        }

        /// <summary>Testo di un file, oppure null (con il motivo) se è binario o troppo grande.</summary>
        private static string? ReadTextFile(string path, out string? problem)
        {
            problem = null;
            var info = new FileInfo(path);
            if (info.Length > MaxFileChars * 4L)
            {
                problem = "è troppo grande.";
                return null;
            }
            string text = File.ReadAllText(path);
            if (text.Contains('\0'))
            {
                problem = "non è un file di testo.";
                return null;
            }
            return text.Length > MaxFileChars ? text[..MaxFileChars] + "\n[… file troncato]" : text;
        }

        private void RenderAttachments()
        {
            AttachmentsBar.Children.Clear();
            foreach (var attachment in _attachments)
            {
                AttachmentsBar.Children.Add(AttachmentChip(attachment, onRemove: () =>
                {
                    _attachments.Remove(attachment);
                    RenderAttachments();
                }));
            }
            AttachmentsScroll.Visibility = _attachments.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdateSendEnabled();
        }

        /// <summary>Etichetta di un allegato: miniatura o icona, nome e (nel composer) il pulsante per toglierlo.</summary>
        private UIElement AttachmentChip(MessageAttachment attachment, Action? onRemove)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
            if (attachment.Kind == "image" && attachment.DataUrl is { } dataUrl && ImageFromDataUrl(dataUrl) is { } image)
            {
                row.Children.Add(new Border
                {
                    Width = 32,
                    Height = 32,
                    CornerRadius = new CornerRadius(4),
                    Child = new Image { Source = image, Stretch = Stretch.UniformToFill },
                });
            }
            else
            {
                row.Children.Add(new FontIcon { Glyph = "", FontSize = 13, Foreground = (Brush)Application.Current.Resources["ClineText2Brush"] });
            }
            row.Children.Add(new TextBlock { Text = attachment.Name, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 220, TextTrimming = TextTrimming.CharacterEllipsis });

            if (onRemove != null)
            {
                var remove = new Button
                {
                    Style = (Style)Application.Current.Resources["ClineIconButtonStyle"],
                    Width = 22,
                    Height = 22,
                    Padding = new Thickness(0),
                    Content = new FontIcon { Glyph = "", FontSize = 9 },
                };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(remove, $"Togli {attachment.Name}");
                remove.Click += (_, _) => onRemove();
                row.Children.Add(remove);
            }

            var chip = new Border
            {
                Padding = new Thickness(8, 4, onRemove != null ? 2 : 8, 4),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                BorderBrush = (Brush)Application.Current.Resources["ClineBorderBrush"],
                Background = (Brush)Application.Current.Resources["ClineCardBrush"],
                Child = row,
            };
            ToolTipService.SetToolTip(chip, attachment.Name);
            return chip;
        }

        private static BitmapImage? ImageFromDataUrl(string dataUrl)
        {
            int comma = dataUrl.IndexOf(',');
            if (comma < 0) return null;
            try
            {
                var bytes = Convert.FromBase64String(dataUrl[(comma + 1)..]);
                var stream = new InMemoryRandomAccessStream();
                using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
                {
                    writer.WriteBytes(bytes);
                    writer.StoreAsync().AsTask().GetAwaiter().GetResult();
                }
                var image = new BitmapImage { DecodePixelHeight = 64 };
                image.SetSource(stream);
                return image;
            }
            catch (FormatException)
            {
                return null;
            }
        }

        private static readonly Regex MentionPattern = new(@"(?<=^|\s)@([^\s@]+)", RegexOptions.Compiled);

        /// <summary>
        /// Allegati del messaggio: quelli aggiunti nel composer più i file menzionati con "@percorso"
        /// che esistono nel progetto (letti adesso, così il modello vede il contenuto attuale).
        /// </summary>
        private List<MessageAttachment> CollectAttachments(string prompt)
        {
            var result = new List<MessageAttachment>(_attachments);
            if (_projectPath == null) return result;
            string root = Path.GetFullPath(_projectPath);
            foreach (Match match in MentionPattern.Matches(prompt))
            {
                string relative = match.Groups[1].Value.TrimEnd('.', ',', ';', ':', ')');
                if (result.Any(a => a.Kind == "file" && a.Name == relative)) continue;
                try
                {
                    string full = Path.GetFullPath(Path.Combine(root, relative));
                    // Solo file dentro il progetto.
                    if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(full)) continue;
                    if (ReadTextFile(full, out _) is { } text) result.Add(new MessageAttachment("file", relative, text));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
                {
                    // Menzione non valida: resta testo semplice.
                }
            }
            return result;
        }

        private void ClearComposer()
        {
            PromptBox.Text = "";
            _attachments.Clear();
            RenderAttachments();
            SuggestionPanel.Visibility = Visibility.Collapsed;
            _completion = null;
        }

        // ---- Messaggi in coda ----

        private sealed record QueuedMessage(string Prompt, List<MessageAttachment> Attachments);

        private readonly List<QueuedMessage> _queue = [];

        private void Enqueue(string prompt, List<MessageAttachment> attachments)
        {
            _queue.Add(new QueuedMessage(prompt, attachments));
            RenderQueue();
        }

        private void RenderQueue()
        {
            QueuePanel.Children.Clear();
            foreach (var item in _queue)
            {
                var grid = new Grid { ColumnSpacing = 8 };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.Children.Add(new FontIcon { Glyph = "", FontSize = 12, Foreground = (Brush)Application.Current.Resources["ClineTextMutedBrush"] });
                var text = new TextBlock
                {
                    Text = "In coda: " + (item.Prompt.Length > 0 ? item.Prompt : $"{item.Attachments.Count} allegati"),
                    FontSize = 12,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Foreground = (Brush)Application.Current.Resources["ClineText2Brush"],
                };
                Grid.SetColumn(text, 1);
                grid.Children.Add(text);
                var remove = new Button
                {
                    Style = (Style)Application.Current.Resources["ClineIconButtonStyle"],
                    Content = new FontIcon { Glyph = "", FontSize = 10 },
                };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(remove, "Togli dalla coda");
                ToolTipService.SetToolTip(remove, "Togli dalla coda");
                remove.Click += (_, _) => { _queue.Remove(item); RenderQueue(); };
                Grid.SetColumn(remove, 2);
                grid.Children.Add(remove);

                QueuePanel.Children.Add(new Border
                {
                    Padding = new Thickness(12, 4, 4, 4),
                    CornerRadius = new CornerRadius(8),
                    Background = (Brush)Application.Current.Resources["ClineCardBrush"],
                    Child = grid,
                });
            }
            QueuePanel.Visibility = _queue.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // ---- Stima dei token ----

        /// <summary>
        /// Stima approssimativa (circa 4 caratteri per token, ~1000 token per immagine) di quanto occupa
        /// la conversazione, con la memoria del modello se il provider la dichiara.
        /// </summary>
        private void UpdateTokenEstimate()
        {
            if (_history.Count == 0)
            {
                TokenText.Visibility = Visibility.Collapsed;
                return;
            }

            long chars = 0;
            int images = 0;
            foreach (var m in _history)
            {
                chars += m.Content.Length;
                foreach (var call in m.ToolCalls ?? []) chars += call.Arguments.Length;
                foreach (var a in m.Attachments ?? [])
                {
                    if (a.Kind == "image") images++;
                    else chars += a.Text?.Length ?? 0;
                }
            }
            long tokens = chars / 4 + images * 1000;

            int? context = s_models.TryGetValue(_provider.Name, out var models)
                ? models.FirstOrDefault(m => m.Id == _modelId)?.ContextLength
                : null;
            string used = FormatTokens(tokens);
            TokenText.Text = context is { } max
                ? $"≈ {used} / {FormatTokens(max)} token ({Math.Min(100, tokens * 100 / max)}%)"
                : $"≈ {used} token";
            ToolTipService.SetToolTip(TokenText, "Stima della memoria del modello occupata dalla conversazione");
            TokenText.Visibility = Visibility.Visible;
        }

        private static string FormatTokens(long tokens) =>
            tokens >= 1_000_000 ? $"{tokens / 1_000_000.0:0.#}M"
            : tokens >= 1000 ? $"{tokens / 1000.0:0.#}k"
            : tokens.ToString(System.Globalization.CultureInfo.CurrentCulture);
    }
}
