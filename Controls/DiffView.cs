using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace ClineWin
{
    /// <summary>
    /// Diff di un file come in un editor: intestazione con percorso e conteggi (+N −N),
    /// poi le righe con numeri prima/dopo, righe aggiunte in verde, tolte in rosso,
    /// e i tratti invariati compressi ("12 righe invariate").
    /// </summary>
    public sealed class DiffView : UserControl
    {
        // Oltre questo numero di righe se ne mostra solo l'inizio: il riquadro resta leggibile e veloce.
        private const int MaxShownLines = 400;

        private static readonly FontFamily Mono = new("Cascadia Mono, Consolas");

        private static Brush Res(string key) => (Brush)Application.Current.Resources[key];

        // Sfondi semitrasparenti ricavati dai colori di successo ed errore del tema.
        private static readonly Brush AddedBackground = new SolidColorBrush(Color.FromArgb(0x24, 0x3D, 0xD6, 0x8C));
        private static readonly Brush RemovedBackground = new SolidColorBrush(Color.FromArgb(0x24, 0xFF, 0x6D, 0x60));

        public DiffView(string path, DiffResult diff, bool newFile)
        {
            var header = new Grid { Padding = new Thickness(12, 6, 12, 6), ColumnSpacing = 10, BorderBrush = Res("ClineBorderBrush"), BorderThickness = new Thickness(0, 0, 0, 1) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Children.Add(new TextBlock
            {
                Text = newFile ? $"{path} (nuovo file)" : path,
                FontFamily = Mono,
                FontSize = 12,
                Foreground = Res("ClineText2Brush"),
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            var plus = new TextBlock { Text = $"+{diff.Added}", FontSize = 12, FontFamily = Mono, Foreground = Res("ClineSuccessBrush") };
            var minus = new TextBlock { Text = $"−{diff.Removed}", FontSize = 12, FontFamily = Mono, Foreground = Res("ClineErrorTextBrush") };
            Grid.SetColumn(plus, 1);
            Grid.SetColumn(minus, 2);
            header.Children.Add(plus);
            header.Children.Add(minus);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(this, $"Modifiche a {path}: {diff.Added} righe aggiunte, {diff.Removed} tolte");

            var rows = new StackPanel();
            if (diff.Lines.Count == 0)
            {
                rows.Children.Add(new TextBlock { Text = "Nessuna differenza.", Margin = new Thickness(12, 8, 12, 8), FontSize = 12, Foreground = Res("ClineTextMutedBrush") });
            }
            foreach (var line in diff.Lines.Take(MaxShownLines))
            {
                rows.Children.Add(Row(line, newFile));
            }
            if (diff.Lines.Count > MaxShownLines)
            {
                rows.Children.Add(Row(new DiffLine(DiffKind.Gap, $"… altre {diff.Lines.Count - MaxShownLines} righe non mostrate", null, null), newFile));
            }

            var body = new ScrollViewer
            {
                MaxHeight = 360,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollMode = ScrollMode.Auto,
                Content = rows,
            };
            // Le righe colorate arrivano fino al bordo anche quando il testo è corto.
            body.SizeChanged += (_, _) => rows.MinWidth = body.ViewportWidth;

            var layout = new StackPanel();
            layout.Children.Add(header);
            layout.Children.Add(body);
            Content = new Border
            {
                Background = Res("ClineCardBrush"),
                BorderBrush = Res("ClineBorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Child = layout,
            };
        }

        private static Grid Row(DiffLine line, bool newFile)
        {
            var row = new Grid
            {
                Background = line.Kind switch
                {
                    DiffKind.Added => AddedBackground,
                    DiffKind.Removed => RemovedBackground,
                    _ => new SolidColorBrush(Colors.Transparent),
                },
            };

            if (line.Kind == DiffKind.Gap)
            {
                row.Padding = new Thickness(12, 2, 12, 2);
                row.Children.Add(new TextBlock
                {
                    Text = "⋯ " + line.Text,
                    FontSize = 11,
                    FontStyle = Windows.UI.Text.FontStyle.Italic,
                    Foreground = Res("ClineTextMutedBrush"),
                });
                return row;
            }

            // Numero prima / numero dopo / segno / testo. Per un file nuovo basta un numero.
            if (!newFile) row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            int column = 0;
            if (!newFile) AddCell(row, column++, line.OldNumber?.ToString() ?? "", Res("ClineTextMutedBrush"), right: true);
            AddCell(row, column++, line.NewNumber?.ToString() ?? "", Res("ClineTextMutedBrush"), right: true);
            var (sign, signBrush) = line.Kind switch
            {
                DiffKind.Added => ("+", Res("ClineSuccessBrush")),
                DiffKind.Removed => ("−", Res("ClineErrorTextBrush")),
                _ => (" ", Res("ClineTextMutedBrush")),
            };
            AddCell(row, column++, sign, signBrush, right: false, padLeft: 6);
            var text = AddCell(row, column, line.Text.Replace("\t", "    "), Res("ClineText1Brush"), right: false);
            text.Padding = new Thickness(0, 0, 12, 0);
            text.IsTextSelectionEnabled = true;
            return row;
        }

        private static TextBlock AddCell(Grid row, int column, string text, Brush foreground, bool right, double padLeft = 0)
        {
            var cell = new TextBlock
            {
                Text = text,
                FontFamily = Mono,
                FontSize = 12,
                LineHeight = 19,
                Foreground = foreground,
                TextAlignment = right ? TextAlignment.Right : TextAlignment.Left,
                Padding = new Thickness(padLeft, 0, right ? 6 : 0, 0),
            };
            Grid.SetColumn(cell, column);
            row.Children.Add(cell);
            return cell;
        }
    }
}
