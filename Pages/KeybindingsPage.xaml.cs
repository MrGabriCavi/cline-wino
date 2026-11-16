using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI.Core;

namespace ClineWin
{
    /// <summary>
    /// Scorciatoie da tastiera: elenco delle azioni con la scorciatoia attuale.
    /// Facendo clic si registra una nuova combinazione (con Ctrl o Alt, oppure F1–F24).
    /// Le scelte vengono salvate; applicarle ai menu della finestra è il passo successivo.
    /// </summary>
    public sealed partial class KeybindingsPage : Page
    {
        private sealed record Action(string Id, string Label, string Description, string Default);

        // Azioni e scorciatoie predefinite su Windows (da lib/keybindings.ts dell'app originale).
        private static readonly Action[] Actions =
        [
            new("newSession", "Nuova sessione", "Avvia una nuova sessione.", "Ctrl+N"),
            new("searchSessions", "Cerca sessioni", "Apre la barra di ricerca delle sessioni.", "Ctrl+P"),
            new("openSettings", "Apri impostazioni", "Apre la vista Impostazioni.", "Ctrl+,"),
            new("toggleTerminal", "Mostra/nascondi terminale", "Mostra o nasconde il pannello del terminale.", "Ctrl+`"),
            new("quit", "Esci", "Chiude l'app.", ""),
        ];

        private Button? _recording;

        public KeybindingsPage()
        {
            InitializeComponent();
            BuildRows();
        }

        private static string Current(Action a) => AppPreferences.Get($"keybinding.{a.Id}", a.Default);

        private void BuildRows()
        {
            BindingRows.Children.Clear();
            var muted = (Brush)Application.Current.Resources["ClineText2Brush"];
            var errorBrush = (Brush)Application.Current.Resources["ClineErrorTextBrush"];

            for (int i = 0; i < Actions.Length; i++)
            {
                var action = Actions[i];
                var row = new Grid
                {
                    Padding = new Thickness(16, 12, 16, 12),
                    ColumnSpacing = 16,
                    BorderBrush = (Brush)Application.Current.Resources["ClineBorderBrush"],
                    BorderThickness = new Thickness(0, i == 0 ? 0 : 1, 0, 0)
                };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var text = new StackPanel { Spacing = 2 };
                text.Children.Add(new TextBlock { Text = action.Label, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
                text.Children.Add(new TextBlock { Text = action.Description, FontSize = 12, Foreground = muted, TextWrapping = TextWrapping.Wrap });

                string? conflict = FindConflict(action);
                if (conflict != null)
                {
                    text.Children.Add(new TextBlock { Text = $"Assegnata anche a “{conflict}”.", FontSize = 12, Foreground = errorBrush });
                }
                row.Children.Add(text);

                var button = new Button
                {
                    Width = 140,
                    Height = 32,
                    Padding = new Thickness(6, 0, 6, 0),
                    Tag = action,
                    Content = KeyCaps(Current(action)),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, $"Scorciatoia per {action.Label}");
                button.Click += Shortcut_Click;
                button.PreviewKeyDown += Shortcut_PreviewKeyDown;
                button.LostFocus += (_, _) => { if (_recording == button) StopRecording(); };
                Grid.SetColumn(button, 1);
                row.Children.Add(button);

                BindingRows.Children.Add(row);
            }
        }

        private static string? FindConflict(Action action)
        {
            string combo = Current(action);
            if (combo.Length == 0) return null;
            return Actions.FirstOrDefault(a => a != action && Current(a) == combo)?.Label;
        }

        /// <summary>Disegna la combinazione come tasti (es. [Ctrl] [N]), oppure "Nessuna".</summary>
        private static UIElement KeyCaps(string combo)
        {
            if (combo.Length == 0)
            {
                return new TextBlock { Text = "Nessuna", FontSize = 12, Foreground = (Brush)Application.Current.Resources["ClineTextMutedBrush"] };
            }

            var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center };
            // "Ctrl+," e "Ctrl++" contengono il separatore: si divide solo sui primi "+".
            var parts = new List<string>();
            string rest = combo;
            foreach (var modifier in new[] { "Ctrl+", "Alt+", "Shift+" })
            {
                if (rest.StartsWith(modifier, StringComparison.Ordinal))
                {
                    parts.Add(modifier[..^1]);
                    rest = rest[modifier.Length..];
                }
            }
            parts.Add(rest);

            foreach (var part in parts)
            {
                panel.Children.Add(new Border
                {
                    Padding = new Thickness(6, 1, 6, 1),
                    MinWidth = 22,
                    CornerRadius = new CornerRadius(4),
                    BorderThickness = new Thickness(1),
                    BorderBrush = (Brush)Application.Current.Resources["ClineInputBorderBrush"],
                    Background = (Brush)Application.Current.Resources["ClineSurface3Brush"],
                    Child = new TextBlock { Text = part, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center }
                });
            }
            return panel;
        }

        // ---- Registrazione ----

        private void Shortcut_Click(object sender, RoutedEventArgs e)
        {
            if (_recording != null) StopRecording();
            _recording = (Button)sender;
            _recording.Content = new TextBlock { Text = "Premi la scorciatoia…", FontSize = 12 };
        }

        private void Shortcut_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (!ReferenceEquals(_recording, sender)) return;
            var action = (Action)_recording.Tag;
            e.Handled = true;

            switch (e.Key)
            {
                case VirtualKey.Escape:
                    StopRecording();
                    return;
                case VirtualKey.Back:
                case VirtualKey.Delete:
                    Save(action, "");
                    return;
                case VirtualKey.Control or VirtualKey.Menu or VirtualKey.Shift
                    or VirtualKey.LeftControl or VirtualKey.RightControl
                    or VirtualKey.LeftMenu or VirtualKey.RightMenu
                    or VirtualKey.LeftShift or VirtualKey.RightShift
                    or VirtualKey.LeftWindows or VirtualKey.RightWindows:
                    return; // Si aspetta il tasto principale.
            }

            bool ctrl = IsDown(VirtualKey.Control);
            bool alt = IsDown(VirtualKey.Menu);
            bool shift = IsDown(VirtualKey.Shift);
            bool function = e.Key is >= VirtualKey.F1 and <= VirtualKey.F24;
            string? key = KeyName(e.Key);

            if (key == null || (!ctrl && !alt && !function))
            {
                _recording.Content = new TextBlock { Text = "Usa Ctrl o Alt, oppure F1–F24", FontSize = 11 };
                return;
            }

            string combo = (ctrl ? "Ctrl+" : "") + (alt ? "Alt+" : "") + (shift ? "Shift+" : "") + key;
            Save(action, combo);
        }

        private static bool IsDown(VirtualKey key) =>
            InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

        private static string? KeyName(VirtualKey key)
        {
            if (key is >= VirtualKey.A and <= VirtualKey.Z or >= VirtualKey.Number0 and <= VirtualKey.Number9)
                return ((char)key).ToString();
            if (key is >= VirtualKey.F1 and <= VirtualKey.F24)
                return key.ToString();

            return (int)key switch
            {
                188 => ",",
                190 => ".",
                189 => "-",
                187 => "+",
                192 => "`",
                (int)VirtualKey.Space => "Spazio",
                (int)VirtualKey.Enter => "Invio",
                (int)VirtualKey.Tab => "Tab",
                (int)VirtualKey.Up => "Su",
                (int)VirtualKey.Down => "Giù",
                (int)VirtualKey.Left => "Sinistra",
                (int)VirtualKey.Right => "Destra",
                _ => null
            };
        }

        private void Save(Action action, string combo)
        {
            AppPreferences.Set($"keybinding.{action.Id}", combo);
            _recording = null;
            BuildRows(); // Aggiorna tasti e avvisi di conflitto.
        }

        private void StopRecording()
        {
            if (_recording?.Tag is Action action)
            {
                _recording.Content = KeyCaps(Current(action));
            }
            _recording = null;
        }

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            foreach (var action in Actions)
            {
                AppPreferences.Set($"keybinding.{action.Id}", action.Default);
            }
            _recording = null;
            BuildRows();
        }
    }
}
