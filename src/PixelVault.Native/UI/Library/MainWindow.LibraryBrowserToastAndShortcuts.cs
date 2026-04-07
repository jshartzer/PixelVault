using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using PixelVaultNative.UI.Design;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        void LibraryBrowserMountToastHost(Grid rootGrid, LibraryBrowserWorkingSet ws)
        {
            if (rootGrid == null || ws == null) return;
            var layer = new Grid { IsHitTestVisible = false, Margin = new Thickness(0, 0, 18, 18) };
            layer.VerticalAlignment = VerticalAlignment.Bottom;
            layer.HorizontalAlignment = HorizontalAlignment.Right;
            Panel.SetZIndex(layer, 80);
            Grid.SetRowSpan(layer, 2);
            ws.LibraryToastBorder = new Border
            {
                Background = Brush(DesignTokens.ToastBackground),
                BorderBrush = Brush(DesignTokens.ToastBorder),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(DesignTokens.RadiusToastDip),
                Padding = new Thickness(14, 10, 14, 10),
                MaxWidth = 420,
                Visibility = Visibility.Collapsed,
                Opacity = 0d
            };
            ws.LibraryToastLabel = new TextBlock
            {
                Foreground = Brush(DesignTokens.TextToast),
                FontSize = 12.5,
                TextWrapping = TextWrapping.Wrap
            };
            ws.LibraryToastBorder.Child = ws.LibraryToastLabel;
            layer.Children.Add(ws.LibraryToastBorder);
            rootGrid.Children.Add(layer);
            ws.LibraryToastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.35) };
            ws.LibraryToastTimer.Tick += delegate
            {
                ws.LibraryToastTimer.Stop();
                if (ws.LibraryToastBorder == null) return;
                if (SystemParameters.ClientAreaAnimation)
                {
                    var fade = new DoubleAnimation(ws.LibraryToastBorder.Opacity, 0d, TimeSpan.FromMilliseconds(140));
                    fade.Completed += delegate
                    {
                        ws.LibraryToastBorder.Visibility = Visibility.Collapsed;
                        ws.LibraryToastBorder.Opacity = 0d;
                    };
                    ws.LibraryToastBorder.BeginAnimation(UIElement.OpacityProperty, fade);
                }
                else
                {
                    ws.LibraryToastBorder.Visibility = Visibility.Collapsed;
                    ws.LibraryToastBorder.Opacity = 0d;
                }
            };
        }

        void ShowLibraryBrowserToast(LibraryBrowserWorkingSet ws, string message)
        {
            if (ws == null || ws.LibraryToastBorder == null || ws.LibraryToastLabel == null || string.IsNullOrWhiteSpace(message)) return;
            ws.LibraryToastTimer?.Stop();
            ws.LibraryToastBorder.BeginAnimation(UIElement.OpacityProperty, null);
            ws.LibraryToastLabel.Text = message.Trim();
            ws.LibraryToastBorder.Visibility = Visibility.Visible;
            if (SystemParameters.ClientAreaAnimation)
            {
                ws.LibraryToastBorder.Opacity = 0d;
                ws.LibraryToastBorder.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0d, 1d, TimeSpan.FromMilliseconds(120)));
            }
            else
            {
                ws.LibraryToastBorder.Opacity = 1d;
            }
            ws.LibraryToastTimer?.Start();
        }

        internal static string NormalizeForLibraryToast(string message, int maxChars = 400)
        {
            if (string.IsNullOrWhiteSpace(message)) return string.Empty;
            var s = message.Trim().Replace("\r\n", "\n").Replace('\r', '\n').Replace('\n', ' ');
            while (s.IndexOf("  ", StringComparison.Ordinal) >= 0) s = s.Replace("  ", " ", StringComparison.Ordinal);
            s = s.Trim();
            if (s.Length <= maxChars) return s;
            return s.Substring(0, maxChars - 3) + "...";
        }

        /// <summary>Uses <paramref name="notify"/> when set (e.g. library toast); otherwise <see cref="MessageBox"/> with normalized text.</summary>
        internal static void NotifyOrMessageBox(Action<string, MessageBoxImage> notify, string message, MessageBoxImage icon = MessageBoxImage.Information)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            if (notify != null)
            {
                notify(message, icon);
                return;
            }
            var text = NormalizeForLibraryToast(message);
            if (string.IsNullOrEmpty(text)) return;
            MessageBox.Show(text, "PixelVault", MessageBoxButton.OK, icon);
        }

        /// <summary>Shows a library toast when the browser working set is live; otherwise a non-blocking notice is unavailable so we fall back to <see cref="MessageBox"/>.</summary>
        internal void TryLibraryToast(string message, MessageBoxImage fallbackIcon = MessageBoxImage.Information)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            var text = NormalizeForLibraryToast(message);
            if (string.IsNullOrEmpty(text)) return;
            var ws = _libraryBrowserLiveWorkingSet;
            if (ws != null && ws.LibraryToastBorder != null)
            {
                ShowLibraryBrowserToast(ws, text);
                return;
            }
            MessageBox.Show(text, "PixelVault", MessageBoxButton.OK, fallbackIcon);
        }

        void ShowLibraryBrowserKeyboardShortcutsHelp(Window owner)
        {
            var w = new Window
            {
                Title = "Library shortcuts — PixelVault",
                Width = 520,
                MinHeight = 420,
                SizeToContent = SizeToContent.Height,
                Owner = owner,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = Brush(DesignTokens.PageBackground),
                ResizeMode = ResizeMode.NoResize
            };
            var root = new StackPanel { Margin = new Thickness(22, 18, 22, 22) };
            root.Children.Add(new TextBlock
            {
                Text = "Keyboard shortcuts",
                FontSize = 22,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 14)
            });
            void AddRow(string keys, string desc)
            {
                var row = new Grid { Margin = new Thickness(0, 0, 0, 10) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var k = new TextBlock { Text = keys, Foreground = Brush(DesignTokens.TextShortcutMuted), FontSize = 13 };
                var d = new TextBlock { Text = desc, Foreground = Brush(DesignTokens.TextShortcutBody), FontSize = 13, TextWrapping = TextWrapping.Wrap };
                row.Children.Add(k);
                Grid.SetColumn(d, 1);
                row.Children.Add(d);
                root.Children.Add(row);
            }
            AddRow("F1", "Open this shortcut list");
            AddRow("Ctrl + Shift + E", "Toggle quick edit panel (side drawer)");
            AddRow("Ctrl + Shift + P", "Command palette (library tools, sort, filter, import)");
            AddRow("⋯ (footer)", "Open command palette (same as Ctrl+Shift+P)");
            AddRow("—", "Export Starred (toolbar): copy starred captures to the folder in Path Settings");
            AddRow("Enter", "Apply library search (search box)");
            AddRow("Ctrl + click", "Add or remove a capture from the selection");
            AddRow("Shift + click", "Select a range of captures from the anchor");
            var close = Btn("Close", null, DesignTokens.ActionShortcutDismissFill, Brushes.White);
            close.Margin = new Thickness(0, 18, 0, 0);
            close.HorizontalAlignment = HorizontalAlignment.Right;
            close.Click += delegate { w.Close(); };
            root.Children.Add(close);
            w.Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = root
            };
            w.ShowDialog();
        }
    }
}
