using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        void LibraryBrowserMountQuickEditDrawer(Grid root, LibraryBrowserWorkingSet ws)
        {
            if (root == null || ws == null) return;

            var host = new Grid
            {
                Visibility = Visibility.Collapsed,
                Background = new SolidColorBrush(Color.FromArgb(0x88, 15, 21, 25))
            };
            Panel.SetZIndex(host, 70);
            Grid.SetRowSpan(host, 2);
            host.MouseLeftButtonDown += delegate { LibraryBrowserSetQuickEditDrawerOpen(ws, false); };

            var panel = new Border
            {
                Width = 340,
                HorizontalAlignment = HorizontalAlignment.Right,
                Background = Brush("#151C22"),
                BorderBrush = Brush("#27313A"),
                BorderThickness = new Thickness(1, 0, 0, 0)
            };
            panel.MouseLeftButtonDown += delegate(object _, MouseButtonEventArgs e) { e.Handled = true; };

            var stack = new StackPanel { Margin = new Thickness(18, 20, 18, 20) };
            stack.Children.Add(new TextBlock
            {
                Text = "Quick edit",
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 10)
            });
            stack.Children.Add(new TextBlock
            {
                Text = "Fast metadata actions will plug in here. Use the command palette or Ctrl+Shift+E to toggle.",
                Foreground = Brush("#9CB1BC"),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 14)
            });
            var close = new Button
            {
                Content = "Close",
                Height = 34,
                MinWidth = 88,
                HorizontalAlignment = HorizontalAlignment.Left,
                Foreground = Brushes.White
            };
            ApplyLibraryPillChrome(close, "#232B35", "#33424D", "#2A3440", "#182028", "#D7E2EA");
            close.ToolTip = "Close (Esc)";
            close.Click += delegate { LibraryBrowserSetQuickEditDrawerOpen(ws, false); };
            AccessibilityUi.TryApplyFocusVisualStyle(close);
            stack.Children.Add(close);
            panel.Child = stack;
            host.Children.Add(panel);
            root.Children.Add(host);
            ws.QuickEditDrawerHost = host;
        }

        void LibraryBrowserSetQuickEditDrawerOpen(LibraryBrowserWorkingSet ws, bool open)
        {
            if (ws == null) return;
            ws.QuickEditDrawerOpen = open;
            if (ws.QuickEditDrawerHost != null)
                ws.QuickEditDrawerHost.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        }

        void LibraryBrowserToggleQuickEditDrawer(LibraryBrowserWorkingSet ws)
        {
            if (ws == null) return;
            LibraryBrowserSetQuickEditDrawerOpen(ws, !ws.QuickEditDrawerOpen);
        }
    }
}
