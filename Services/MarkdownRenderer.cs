using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Block = Markdig.Syntax.Block;

namespace ClineWin
{
    /// <summary>
    /// Trasforma il Markdown delle risposte in controlli WinUI nativi.
    /// Markdig fa il parsing; qui ogni blocco diventa un controllo:
    /// paragrafi e titoli → RichTextBlock, codice → riquadro con "Copia",
    /// elenchi, citazioni e tabelle → Grid e Border.
    /// </summary>
    public static class MarkdownRenderer
    {
        private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
            .UsePipeTables()
            .UseAutoLinks()
            .UseEmphasisExtras()
            .UseTaskLists()
            .Build();

        private static readonly FontFamily MonoFont = new("Cascadia Mono, Consolas");
        private const double BodySize = 14;

        private static Brush Res(string key) => (Brush)Application.Current.Resources[key];

        public static UIElement Render(string markdown)
        {
            var document = Markdown.Parse(ThinkingAsQuote(markdown), Pipeline);
            var panel = new StackPanel { Spacing = 10 };
            foreach (var block in document)
            {
                if (RenderBlock(block) is { } element) panel.Children.Add(element);
            }
            return panel;
        }

        private static readonly System.Text.RegularExpressions.Regex ThinkingTag = new(
            @"<thinking>(.*?)(</thinking>|$)",
            System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>
        /// Alcuni modelli scrivono il ragionamento tra &lt;thinking&gt; e &lt;/thinking&gt;:
        /// lo si mostra come citazione (con la barra a sinistra) invece che con i tag.
        /// Funziona anche durante lo streaming, quando il tag di chiusura non è ancora arrivato.
        /// </summary>
        private static string ThinkingAsQuote(string markdown) =>
            ThinkingTag.Replace(markdown, m =>
            {
                var lines = m.Groups[1].Value.Trim().Split('\n').Select(l => "> " + l.TrimEnd('\r'));
                return "\n\n" + string.Join('\n', lines) + "\n\n";
            });

        // ---- Blocchi ----

        private static UIElement? RenderBlock(Block block) => block switch
        {
            HeadingBlock heading => RenderHeading(heading),
            ParagraphBlock paragraph => RenderParagraph(paragraph.Inline),
            FencedCodeBlock fenced => RenderCode(CodeText(fenced), fenced.Info),
            CodeBlock code => RenderCode(CodeText(code), null),
            ListBlock list => RenderList(list),
            QuoteBlock quote => RenderQuote(quote),
            Table table => RenderTable(table),
            ThematicBreakBlock => new Border { Height = 1, Margin = new Thickness(0, 6, 0, 6), Background = Res("ClineBorderBrush") },
            HtmlBlock html => RenderPlain(html.Lines.ToString()),
            ContainerBlock container => RenderContainer(container),
            LeafBlock leaf when leaf.Inline != null => RenderParagraph(leaf.Inline),
            _ => null,
        };

        private static StackPanel RenderContainer(ContainerBlock container, double spacing = 8)
        {
            var panel = new StackPanel { Spacing = spacing };
            foreach (var child in container)
            {
                if (RenderBlock(child) is { } element) panel.Children.Add(element);
            }
            return panel;
        }

        private static RichTextBlock RenderHeading(HeadingBlock heading)
        {
            var text = NewRichText();
            text.FontSize = heading.Level switch { 1 => 22, 2 => 19, 3 => 16.5, _ => 15 };
            text.FontWeight = FontWeights.SemiBold;
            text.Margin = new Thickness(0, heading.Level <= 2 ? 8 : 4, 0, 0);
            var paragraph = new Paragraph();
            AddInlines(paragraph.Inlines, heading.Inline);
            text.Blocks.Add(paragraph);
            return text;
        }

        private static RichTextBlock RenderParagraph(ContainerInline? inline)
        {
            var text = NewRichText();
            var paragraph = new Paragraph();
            AddInlines(paragraph.Inlines, inline);
            text.Blocks.Add(paragraph);
            return text;
        }

        private static RichTextBlock RenderPlain(string content)
        {
            var text = NewRichText();
            var paragraph = new Paragraph();
            paragraph.Inlines.Add(new Run { Text = content.TrimEnd() });
            text.Blocks.Add(paragraph);
            return text;
        }

        private static RichTextBlock NewRichText() => new()
        {
            FontSize = BodySize,
            LineHeight = 22,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
            Foreground = Res("ClineText1Brush"),
        };

        private static string CodeText(LeafBlock code)
        {
            var lines = code.Lines;
            var builder = new System.Text.StringBuilder();
            for (int i = 0; i < lines.Count; i++)
            {
                if (i > 0) builder.Append('\n');
                builder.Append(lines.Lines[i].Slice.ToString());
            }
            return builder.ToString();
        }

        /// <summary>Riquadro del codice: intestazione con linguaggio e "Copia", testo monospazio scorrevole.</summary>
        private static Border RenderCode(string code, string? language)
        {
            var copy = new Button
            {
                Style = (Style)Application.Current.Resources["ClineIconButtonStyle"],
                HorizontalAlignment = HorizontalAlignment.Right,
                Content = new FontIcon { Glyph = "", FontSize = 12 },
            };
            ToolTipService.SetToolTip(copy, "Copia");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(copy, "Copia codice");
            copy.Click += (_, _) =>
            {
                var package = new DataPackage();
                package.SetText(code);
                Clipboard.SetContent(package);
                ((FontIcon)copy.Content).Glyph = ""; // segno di spunta
            };

            var header = new Grid
            {
                Padding = new Thickness(12, 2, 4, 2),
                BorderBrush = Res("ClineBorderBrush"),
                BorderThickness = new Thickness(0, 0, 0, 1),
            };
            header.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(language) ? "codice" : language.Trim(),
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Res("ClineText2Brush"),
            });
            header.Children.Add(copy);

            var body = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollMode = ScrollMode.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollMode = ScrollMode.Disabled,
                Content = new TextBlock
                {
                    Text = code,
                    FontFamily = MonoFont,
                    FontSize = 13,
                    LineHeight = 20,
                    Padding = new Thickness(12, 10, 12, 12),
                    IsTextSelectionEnabled = true,
                    Foreground = Res("ClineText1Brush"),
                },
            };

            var layout = new StackPanel();
            layout.Children.Add(header);
            layout.Children.Add(body);
            return new Border
            {
                Background = Res("ClineCardBrush"),
                BorderBrush = Res("ClineBorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Child = layout,
            };
        }

        private static Grid RenderList(ListBlock list)
        {
            var grid = new Grid { ColumnSpacing = 8, RowSpacing = 4 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            int number = int.TryParse(list.OrderedStart, out int start) ? start : 1;
            int row = 0;
            foreach (var item in list.OfType<ListItemBlock>())
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var marker = new TextBlock
                {
                    Text = list.IsOrdered ? $"{number++}." : "•",
                    FontSize = BodySize,
                    LineHeight = 22,
                    MinWidth = list.IsOrdered ? 18 : 10,
                    TextAlignment = TextAlignment.Right,
                    Foreground = Res("ClineText2Brush"),
                };
                Grid.SetRow(marker, row);
                grid.Children.Add(marker);

                var content = RenderContainer(item, 6);
                Grid.SetRow(content, row);
                Grid.SetColumn(content, 1);
                grid.Children.Add(content);
                row++;
            }
            return grid;
        }

        private static Border RenderQuote(QuoteBlock quote) => new()
        {
            BorderBrush = Res("ClineInputBorderBrush"),
            BorderThickness = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(12, 2, 0, 2),
            Child = RenderContainer(quote),
        };

        private static Border RenderTable(Table table)
        {
            var grid = new Grid();
            int columns = table.OfType<TableRow>().Select(r => r.Count).DefaultIfEmpty(0).Max();
            for (int c = 0; c < columns; c++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            }

            int r = 0;
            foreach (var row in table.OfType<TableRow>())
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                for (int c = 0; c < row.Count; c++)
                {
                    var cell = (TableCell)row[c];
                    var content = RenderContainer(cell, 4);
                    if (row.IsHeader)
                    {
                        foreach (var text in content.Children.OfType<RichTextBlock>()) text.FontWeight = FontWeights.SemiBold;
                    }
                    var border = new Border
                    {
                        Padding = new Thickness(10, 6, 10, 6),
                        BorderBrush = Res("ClineBorderBrush"),
                        BorderThickness = new Thickness(c == 0 ? 0 : 1, r == 0 ? 0 : 1, 0, 0),
                        Background = row.IsHeader ? Res("ClineCardBrush") : null,
                        Child = content,
                    };
                    Grid.SetRow(border, r);
                    Grid.SetColumn(border, c);
                    grid.Children.Add(border);
                }
                r++;
            }

            return new Border
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                BorderBrush = Res("ClineBorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Child = new ScrollViewer
                {
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollMode = ScrollMode.Auto,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    VerticalScrollMode = ScrollMode.Disabled,
                    Content = grid,
                },
            };
        }

        // ---- Elementi in linea ----

        private static void AddInlines(InlineCollection target, ContainerInline? container)
        {
            if (container == null) return;
            foreach (var inline in container)
            {
                if (RenderInline(inline) is { } rendered) target.Add(rendered);
            }
        }

        private static Microsoft.UI.Xaml.Documents.Inline? RenderInline(Markdig.Syntax.Inlines.Inline inline)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    return new Run { Text = literal.Content.ToString() };

                case CodeInline code:
                    return new Run
                    {
                        Text = code.Content,
                        FontFamily = MonoFont,
                        FontSize = BodySize - 1,
                        // Il colore "hero" del robot è troppo scuro per il testo: si usa il primario.
                        Foreground = Res("ClinePrimaryBrush"),
                    };

                case LineBreakInline lineBreak:
                    // Un a capo "morbido" nel Markdown è solo uno spazio.
                    return lineBreak.IsHard ? new LineBreak() : new Run { Text = " " };

                case EmphasisInline emphasis:
                {
                    Span span = emphasis.DelimiterChar switch
                    {
                        '~' => new Span(),
                        _ when emphasis.DelimiterCount >= 2 => new Bold(),
                        _ => new Italic(),
                    };
                    if (emphasis.DelimiterChar == '~') span.TextDecorations = Windows.UI.Text.TextDecorations.Strikethrough;
                    AddInlines(span.Inlines, emphasis);
                    return span;
                }

                case LinkInline link when !link.IsImage && Uri.TryCreate(link.Url, UriKind.Absolute, out var uri):
                {
                    var hyperlink = new Hyperlink { NavigateUri = uri };
                    AddInlines(hyperlink.Inlines, link);
                    if (hyperlink.Inlines.Count == 0) hyperlink.Inlines.Add(new Run { Text = link.Url });
                    return hyperlink;
                }

                case LinkInline link:
                {
                    // Immagini e link relativi: si mostra solo il testo.
                    var span = new Span();
                    AddInlines(span.Inlines, link);
                    return span;
                }

                case AutolinkInline auto when Uri.TryCreate(auto.IsEmail ? "mailto:" + auto.Url : auto.Url, UriKind.Absolute, out var autoUri):
                {
                    var hyperlink = new Hyperlink { NavigateUri = autoUri };
                    hyperlink.Inlines.Add(new Run { Text = auto.Url });
                    return hyperlink;
                }

                case Markdig.Extensions.TaskLists.TaskList task:
                    return new Run { Text = task.Checked ? "☑ " : "☐ " };

                case HtmlEntityInline entity:
                    return new Run { Text = entity.Transcoded.ToString() };

                case HtmlInline html:
                    return new Run { Text = html.Tag };

                case ContainerInline nested:
                {
                    var span = new Span();
                    AddInlines(span.Inlines, nested);
                    return span;
                }

                default:
                    return null;
            }
        }
    }
}
