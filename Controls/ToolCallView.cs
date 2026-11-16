using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ClineWin
{
    /// <summary>Scelta dell'utente su un'azione: approva, approva sempre questo tipo, rifiuta.</summary>
    public enum ApprovalChoice { Approve, Always, Reject }

    /// <summary>
    /// Riquadro di una chiamata a uno strumento nella conversazione:
    /// icona e riepilogo, stato, dettaglio (contenuto del file, modifica o comando),
    /// pulsanti Approva/Rifiuta quando serve l'approvazione, e il risultato in un Expander.
    /// </summary>
    public sealed class ToolCallView : UserControl
    {
        public const string RejectedResult = "L'utente ha rifiutato questa azione.";
        public const string InterruptedResult = "Azione interrotta dall'utente.";

        private readonly TextBlock _status;
        private readonly StackPanel _body;
        private readonly ToolCategory _category;
        private StackPanel? _approvalRow;
        private TaskCompletionSource<ApprovalChoice>? _approval;
        private bool _autoApproved;

        private static Brush Res(string key) => (Brush)Application.Current.Resources[key];

        public ToolCallView(ToolPreview preview)
        {
            _category = preview.Category;

            var header = new Grid { ColumnSpacing = 10 };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            header.Children.Add(new FontIcon { Glyph = preview.Glyph, FontSize = 13, Foreground = Res("ClineText2Brush") });
            var summary = new TextBlock
            {
                Text = preview.Summary,
                FontSize = 13,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            };
            ToolTipService.SetToolTip(summary, preview.Summary);
            Grid.SetColumn(summary, 1);
            header.Children.Add(summary);

            _status = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Foreground = Res("ClineTextMutedBrush") };
            Grid.SetColumn(_status, 2);
            header.Children.Add(_status);

            _body = new StackPanel { Spacing = 8 };
            _body.Children.Add(header);

            if (preview.Diff is { } diff)
            {
                _body.Children.Add(new DiffView(diff.Path, diff.Result, diff.NewFile));
            }
            else if (!string.IsNullOrEmpty(preview.Detail))
            {
                _body.Children.Add(new ScrollViewer
                {
                    MaxHeight = 320,
                    Content = MarkdownRenderer.Render(Fence(preview.Detail, preview.DetailLanguage)),
                });
            }

            Content = new Border
            {
                Background = Res("ClineCardBrush"),
                BorderBrush = Res("ClineBorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 8, 12, 8),
                Child = _body,
            };
        }

        /// <summary>
        /// Mostra Approva / Approva sempre / Rifiuta e aspetta la scelta.
        /// Con l'annullamento (Interrompi) lancia OperationCanceledException.
        /// </summary>
        public async Task<ApprovalChoice> WaitForApprovalAsync(CancellationToken ct)
        {
            _status.Text = "In attesa di approvazione";
            _status.Foreground = Res("ClineText1Brush");
            _approval = new TaskCompletionSource<ApprovalChoice>(TaskCreationOptions.RunContinuationsAsynchronously);

            var approve = new Button { Content = "Approva", Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
            var always = new Button { Content = $"Approva sempre ({AutoApproval.Label(_category)})" };
            ToolTipService.SetToolTip(always, "Approva e da ora esegue senza chiedere le azioni di questo tipo. Si cambia dal menu dell'approvazione automatica.");
            var reject = new Button { Content = "Rifiuta" };
            approve.Click += (_, _) => _approval.TrySetResult(ApprovalChoice.Approve);
            always.Click += (_, _) => _approval.TrySetResult(ApprovalChoice.Always);
            reject.Click += (_, _) => _approval.TrySetResult(ApprovalChoice.Reject);
            _approvalRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            _approvalRow.Children.Add(approve);
            _approvalRow.Children.Add(always);
            _approvalRow.Children.Add(reject);
            _body.Children.Add(_approvalRow);

            using (ct.Register(() => _approval.TrySetCanceled(ct)))
            {
                try
                {
                    return await _approval.Task;
                }
                finally
                {
                    _body.Children.Remove(_approvalRow);
                }
            }
        }

        /// <summary>True quando è stato mostrato il risultato (eseguito, rifiutato o interrotto).</summary>
        public bool IsFinished { get; private set; }

        /// <summary>
        /// Azione eseguita senza chiedere: lo stato finale dirà "approvato in automatico".
        /// Vale solo per modifiche e comandi; la lettura automatica è la norma e non si segnala.
        /// </summary>
        public void MarkAutoApproved() => _autoApproved = _category != ToolCategory.Read;

        public void SetRunning()
        {
            _status.Text = "In esecuzione…";
            _status.Foreground = Res("ClineTextMutedBrush");
        }

        /// <summary>Mostra lo stato finale e il risultato (chiuso in un Expander).</summary>
        public void SetResult(string result)
        {
            IsFinished = true;
            bool rejected = result == RejectedResult || result == Scheduler.NotApprovedResult;
            bool interrupted = result == InterruptedResult;
            bool error = result.StartsWith("Errore", StringComparison.Ordinal);

            _status.Text = result == Scheduler.NotApprovedResult ? "Non approvato (esecuzione pianificata)"
                : rejected ? "Rifiutato" : interrupted ? "Interrotto" : error ? "Errore"
                : _autoApproved ? "Eseguito (approvato in automatico)" : "Eseguito";
            _status.Foreground = Res(error || rejected ? "ClineErrorTextBrush" : interrupted ? "ClineTextMutedBrush" : "ClineSuccessBrush");
            if (rejected || interrupted) return;

            var text = new TextBlock
            {
                Text = result,
                FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                FontSize = 12,
                LineHeight = 18,
                IsTextSelectionEnabled = true,
                Foreground = Res("ClineText2Brush"),
            };
            _body.Children.Add(new Expander
            {
                Header = "Risultato",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                IsExpanded = error,
                Content = new ScrollViewer
                {
                    MaxHeight = 300,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollMode = ScrollMode.Auto,
                    Content = text,
                },
            });
        }

        /// <summary>Blocco di codice Markdown con una recinzione più lunga di ogni ``` del contenuto.</summary>
        private static string Fence(string content, string? language)
        {
            int longest = 0, run = 0;
            foreach (char c in content)
            {
                run = c == '`' ? run + 1 : 0;
                longest = Math.Max(longest, run);
            }
            string fence = new('`', Math.Max(3, longest + 1));
            return $"{fence}{language}\n{content}\n{fence}";
        }
    }
}
