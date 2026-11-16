using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace ClineWin
{
    /// <summary>
    /// Pannello git della sessione: file cambiati (preparati e non), diff, stage/unstage/annulla,
    /// commit, e il menu dei branch sul chip. Si aggiorna aprendo il progetto e dopo ogni turno dell'agente.
    /// </summary>
    public sealed partial class WelcomePage
    {
        private string? _gitRoot;
        private GitStatus? _gitStatus;
        private string? _selectedChange; // "S:percorso" o "W:percorso" del file di cui si mostra il diff

        private static Brush Res(string key) => (Brush)Application.Current.Resources[key];

        /// <summary>Rilegge lo stato git del progetto (se è un repository) e aggiorna chip e pannello.</summary>
        private async Task RefreshGitAsync()
        {
            string? project = _projectPath;
            _gitRoot = project != null ? await GitService.FindRootAsync(project) : null;
            if (project != _projectPath) return; // nel frattempo è cambiato progetto

            if (_gitRoot == null)
            {
                _gitStatus = null;
                ChangesButton.Visibility = Visibility.Collapsed;
                GitPanel.Visibility = Visibility.Collapsed;
                return;
            }

            try
            {
                _gitStatus = await GitService.StatusAsync(_gitRoot);
            }
            catch (GitException ex)
            {
                ShowGitError(ex.Message);
                return;
            }

            int files = _gitStatus.Changes.Select(c => c.Path).Distinct().Count();
            ChangesButtonText.Text = files == 0 ? "Nessuna modifica" : files == 1 ? "1 file modificato" : $"{files} file modificati";
            ChangesButton.Visibility = Visibility.Visible;
            if (_gitStatus.Branch.Length > 0)
            {
                BranchText.Text = _gitStatus.Branch;
                BranchButton.Visibility = WorktreePanel.Visibility = Visibility.Visible;
            }
            if (GitPanel.Visibility == Visibility.Visible) await RenderGitPanelAsync();
        }

        private async void ChangesButton_Click(object sender, RoutedEventArgs e)
        {
            GitPanel.Visibility = GitPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
            if (GitPanel.Visibility == Visibility.Visible) await RefreshGitAsync();
        }

        private void GitClose_Click(object sender, RoutedEventArgs e) => GitPanel.Visibility = Visibility.Collapsed;

        private async void GitRefresh_Click(object sender, RoutedEventArgs e) => await RefreshGitAsync();

        private async Task RenderGitPanelAsync()
        {
            GitContent.Children.Clear();
            if (_gitStatus is not { } status || _gitRoot is not { } root) return;

            string sync = (status.Ahead, status.Behind) switch
            {
                (0, 0) => "",
                (var a, 0) => $" · {a} commit da inviare",
                (0, var b) => $" · {b} commit da scaricare",
                (var a, var b) => $" · {a} da inviare, {b} da scaricare",
            };
            GitBranchText.Text = $"Branch {status.Branch}{sync}";

            var staged = status.Changes.Where(c => c.Staged).ToList();
            var unstaged = status.Changes.Where(c => !c.Staged).ToList();
            if (staged.Count == 0 && unstaged.Count == 0)
            {
                GitContent.Children.Add(new TextBlock
                {
                    Text = "Nessuna modifica: la cartella corrisponde all'ultimo commit.",
                    Margin = new Thickness(8, 8, 8, 0),
                    FontSize = 13,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Res("ClineTextMutedBrush"),
                });
            }

            if (staged.Count > 0) AddSection("Preparate per il commit", staged, root);
            if (unstaged.Count > 0) AddSection("Modifiche", unstaged, root);

            CommitButton.Content = staged.Count == 0 ? "Nessuna modifica preparata" : $"Commit di {staged.Count} file";
            UpdateCommitEnabled();

            // Il diff del file selezionato, se c'è ancora.
            if (_selectedChange != null && status.Changes.FirstOrDefault(c => Key(c) == _selectedChange) is { } selected)
            {
                await ShowDiffAsync(selected);
            }
        }

        private static string Key(GitChange c) => (c.Staged ? "S:" : "W:") + c.Path;

        private void AddSection(string title, List<GitChange> changes, string root)
        {
            var header = new Grid { Margin = new Thickness(8, 12, 4, 4) };
            header.Children.Add(new TextBlock
            {
                Text = $"{title.ToUpperInvariant()}  {changes.Count}",
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = Res("ClineText2Brush"),
                VerticalAlignment = VerticalAlignment.Center,
            });
            // Azione su tutta la sezione: prepara tutto / togli tutto.
            bool stagedSection = changes[0].Staged;
            var all = SmallButton(stagedSection ? "" : "", stagedSection ? "Togli tutto dal commit" : "Prepara tutto per il commit");
            all.HorizontalAlignment = HorizontalAlignment.Right;
            all.Click += async (_, _) => await GitActionAsync(async () =>
            {
                foreach (var change in changes)
                {
                    if (stagedSection) await GitService.UnstageAsync(root, change.Path);
                    else await GitService.StageAsync(root, change.Path);
                }
            });
            header.Children.Add(all);
            GitContent.Children.Add(header);

            foreach (var change in changes) GitContent.Children.Add(ChangeRow(change, root));
        }

        private UIElement ChangeRow(GitChange change, string root)
        {
            var grid = new Grid { ColumnSpacing = 8, Padding = new Thickness(8, 2, 4, 2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            int slash = change.Path.LastIndexOf('/');
            var name = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
            name.Children.Add(new TextBlock { Text = change.Path[(slash + 1)..], FontSize = 13 });
            if (slash > 0)
            {
                name.Children.Add(new TextBlock { Text = change.Path[..slash], FontSize = 11, Foreground = Res("ClineTextMutedBrush"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            }

            // Il nome è un pulsante: apre il diff del file.
            var open = new Button
            {
                Content = name,
                Padding = new Thickness(4, 2, 4, 2),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0),
            };
            ToolTipService.SetToolTip(open, $"{change.Path} ({change.Description}) — mostra le differenze");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(open, $"{change.Path}, {change.Description}");
            open.Click += async (_, _) =>
            {
                _selectedChange = Key(change);
                await ShowDiffAsync(change);
            };
            grid.Children.Add(open);

            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
            if (change.Staged)
            {
                var unstage = SmallButton("", $"Togli {change.Path} dal commit");
                unstage.Click += async (_, _) => await GitActionAsync(() => GitService.UnstageAsync(root, change.Path));
                actions.Children.Add(unstage);
            }
            else if (change.Status != "conflict")
            {
                var discard = SmallButton("", $"Annulla le modifiche a {change.Path}");
                discard.Click += async (_, _) => await DiscardAsync(change, root);
                actions.Children.Add(discard);
                var stage = SmallButton("", $"Prepara {change.Path} per il commit");
                stage.Click += async (_, _) => await GitActionAsync(() => GitService.StageAsync(root, change.Path));
                actions.Children.Add(stage);
            }
            Grid.SetColumn(actions, 1);
            grid.Children.Add(actions);

            var letter = new TextBlock
            {
                Text = change.Letter,
                FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                FontSize = 12,
                Width = 14,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Res(change.Status switch
                {
                    "untracked" or "added" => "ClineSuccessBrush",
                    "deleted" or "conflict" => "ClineErrorTextBrush",
                    _ => "ClinePrimaryBrush",
                }),
            };
            ToolTipService.SetToolTip(letter, change.Description);
            Grid.SetColumn(letter, 2);
            grid.Children.Add(letter);

            bool selected = _selectedChange == Key(change);
            return new Border
            {
                CornerRadius = new CornerRadius(6),
                Background = selected ? Res("ClineSelectedBrush") : null,
                Child = grid,
            };
        }

        private static Button SmallButton(string glyph, string name)
        {
            var button = new Button
            {
                Style = (Style)Application.Current.Resources["ClineIconButtonStyle"],
                Width = 26,
                Height = 26,
                Padding = new Thickness(0),
                Content = new FontIcon { Glyph = glyph, FontSize = 12 },
            };
            ToolTipService.SetToolTip(button, name);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, name);
            return button;
        }

        /// <summary>Mostra il diff del file in fondo al pannello (stesso riquadro delle modifiche dell'agente).</summary>
        private async Task ShowDiffAsync(GitChange change)
        {
            if (_gitRoot is not { } root) return;
            foreach (var old in GitContent.Children.OfType<DiffView>().ToList()) GitContent.Children.Remove(old);
            try
            {
                var (before, after) = await GitService.DiffTextsAsync(root, change);
                if (before.Contains('\0') || after.Contains('\0'))
                {
                    ShowGitError($"{change.Path} è un file binario: niente diff da mostrare.");
                    return;
                }
                var diff = LineDiff.Compute(before, after);
                var view = new DiffView(change.Path, diff, change.Status is "untracked" or "added") { Margin = new Thickness(0, 12, 0, 0) };
                GitContent.Children.Add(view);
                view.StartBringIntoView();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or GitException)
            {
                ShowGitError(ex.Message);
            }
        }

        private async Task DiscardAsync(GitChange change, string root)
        {
            var dialog = new ContentDialog
            {
                Title = "Annullare le modifiche?",
                Content = change.Status == "untracked"
                    ? $"“{change.Path}” è un file nuovo: verrà eliminato. Non si può annullare."
                    : $"Le modifiche non preparate a “{change.Path}” andranno perse e il file tornerà come nell'ultimo commit (o come preparato). Non si può annullare.",
                PrimaryButtonText = change.Status == "untracked" ? "Elimina il file" : "Annulla le modifiche",
                CloseButtonText = "Lascia stare",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            await GitActionAsync(() => GitService.DiscardAsync(root, change));
        }

        private void CommitMessage_TextChanged(object sender, TextChangedEventArgs e) => UpdateCommitEnabled();

        private void UpdateCommitEnabled()
        {
            CommitButton.IsEnabled = _gitStatus?.Changes.Any(c => c.Staged) == true && CommitMessageBox.Text.Trim().Length > 0;
        }

        private async void Commit_Click(object sender, RoutedEventArgs e)
        {
            if (_gitRoot is not { } root) return;
            string message = CommitMessageBox.Text.Trim();
            if (message.Length == 0) return;
            await GitActionAsync(async () =>
            {
                await GitService.CommitAsync(root, message);
                CommitMessageBox.Text = "";
                _selectedChange = null;
            });
        }

        /// <summary>Esegue un'operazione git, mostra l'eventuale errore e aggiorna il pannello.</summary>
        private async Task GitActionAsync(Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (Exception ex) when (ex is GitException or IOException or UnauthorizedAccessException)
            {
                ShowGitError(ex.Message);
            }
            await RefreshGitAsync();
        }

        private void ShowGitError(string message) =>
            ShowBar(InfoBarSeverity.Error, "git: " + message, null);

        // ---- Branch ----

        private async void BranchButton_Click(object sender, RoutedEventArgs e)
        {
            if (_gitRoot is not { } root) return;
            IReadOnlyList<string> branches;
            try
            {
                branches = await GitService.BranchesAsync(root);
            }
            catch (GitException ex)
            {
                ShowGitError(ex.Message);
                return;
            }

            var menu = new MenuFlyout { Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft };
            foreach (var branch in branches)
            {
                var item = new ToggleMenuFlyoutItem { Text = branch, IsChecked = branch == _gitStatus?.Branch };
                item.Click += async (_, _) =>
                {
                    if (branch == _gitStatus?.Branch) return;
                    await GitActionAsync(() => GitService.CheckoutAsync(root, branch));
                };
                menu.Items.Add(item);
            }
            menu.Items.Add(new MenuFlyoutSeparator());
            var create = new MenuFlyoutItem { Text = "Nuovo branch…", Icon = new FontIcon { Glyph = "" } };
            create.Click += async (_, _) => await CreateBranchAsync(root);
            menu.Items.Add(create);
            menu.ShowAt(BranchButton);
        }

        private async Task CreateBranchAsync(string root)
        {
            var nameBox = new TextBox { PlaceholderText = "nome-del-branch", Header = "Nome" };
            var dialog = new ContentDialog
            {
                Title = "Nuovo branch",
                Content = new StackPanel
                {
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock { Text = $"Parte dal branch attuale ({_gitStatus?.Branch}) e ci si sposta subito. Le modifiche non salvate restano nella cartella.", TextWrapping = TextWrapping.Wrap },
                        nameBox,
                    },
                },
                PrimaryButtonText = "Crea",
                CloseButtonText = "Annulla",
                DefaultButton = ContentDialogButton.Primary,
                IsPrimaryButtonEnabled = false,
                XamlRoot = XamlRoot
            };
            // Niente spazi né caratteri vietati da git: il controllo vero lo fa git, qui si evita l'ovvio.
            nameBox.TextChanged += (_, _) =>
                dialog.IsPrimaryButtonEnabled = nameBox.Text.Trim().Length > 0 && !nameBox.Text.Trim().Any(c => char.IsWhiteSpace(c) || "~^:?*[\\".Contains(c));
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            await GitActionAsync(() => GitService.CreateBranchAsync(root, nameBox.Text.Trim()));
        }
    }
}
