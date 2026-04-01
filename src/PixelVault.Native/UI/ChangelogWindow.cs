using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace PixelVaultNative
{
    /// <summary>
    /// Modal changelog viewer (markdown-style lines from CHANGELOG.md). Extracted from MainWindow (Phase B3).
    /// </summary>
    static class ChangelogWindow
    {
        static SolidColorBrush HexBrush(string hex)
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        }

        static Button BuildCloseButton(RoutedEventHandler onClick)
        {
            var b = new Button
            {
                Content = "Close",
                Width = 176,
                Height = 48,
                Padding = new Thickness(18, 10, 18, 10),
                Margin = new Thickness(0, 0, 12, 12),
                Foreground = Brushes.White,
                Background = HexBrush("#20343A"),
                BorderBrush = HexBrush("#C0CCD6"),
                BorderThickness = new Thickness(1),
                FontWeight = FontWeights.SemiBold,
                Effect = new DropShadowEffect { Color = Color.FromArgb(64, 18, 27, 36), BlurRadius = 16, ShadowDepth = 4, Direction = 270, Opacity = 0.55 }
            };
            if (onClick != null) b.Click += onClick;
            return b;
        }

        public static void ShowDialog(Window owner, string appVersion, string changelogPath)
        {
            var window = new Window
            {
                Title = "PixelVault " + appVersion + " Changelog",
                Width = 780,
                Height = 700,
                MinWidth = 680,
                MinHeight = 520,
                Owner = owner,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = HexBrush("#F5F8FC")
            };

            var root = new Grid { Margin = new Thickness(24), Background = Brushes.White };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var header = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
            header.Children.Add(new TextBlock { Text = "PixelVault changelog", FontSize = 28, FontWeight = FontWeights.SemiBold, Foreground = HexBrush("#1F2A30") });
            header.Children.Add(new TextBlock { Text = "Recent release notes, fixes, and workflow updates.", Margin = new Thickness(0, 8, 0, 0), Foreground = HexBrush("#5F6970") });
            root.Children.Add(header);

            var viewer = new RichTextBox
            {
                IsReadOnly = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                BorderThickness = new Thickness(0),
                Background = Brushes.White,
                FontFamily = new FontFamily("Segoe UI")
            };
            var doc = new FlowDocument { PagePadding = new Thickness(18), Background = Brushes.White };
            var lines = File.Exists(changelogPath) ? File.ReadAllLines(changelogPath) : new[] { "# PixelVault Changelog", "", "No changelog entries yet." };
            foreach (var rawLine in lines)
            {
                var line = rawLine ?? string.Empty;
                if (line.StartsWith("## "))
                {
                    doc.Blocks.Add(new Paragraph(new Run(line.Substring(3))) { Margin = new Thickness(0, 14, 0, 6), FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = HexBrush("#1F2A30") });
                }
                else if (line.StartsWith("# "))
                {
                    doc.Blocks.Add(new Paragraph(new Run(line.Substring(2))) { Margin = new Thickness(0, 0, 0, 10), FontSize = 26, FontWeight = FontWeights.Bold, Foreground = HexBrush("#1F2A30") });
                }
                else if (line.StartsWith("- "))
                {
                    doc.Blocks.Add(new Paragraph(new Run("• " + line.Substring(2))) { Margin = new Thickness(0, 0, 0, 8), FontSize = 14, Foreground = HexBrush("#3B4650") });
                }
                else if (string.IsNullOrWhiteSpace(line))
                {
                    doc.Blocks.Add(new Paragraph(new Run(string.Empty)) { Margin = new Thickness(0, 2, 0, 2) });
                }
                else
                {
                    doc.Blocks.Add(new Paragraph(new Run(line)) { Margin = new Thickness(0, 0, 0, 8), FontSize = 14, Foreground = HexBrush("#3B4650") });
                }
            }
            viewer.Document = doc;
            Grid.SetRow(viewer, 1);
            root.Children.Add(viewer);

            var buttons = new WrapPanel { Margin = new Thickness(0, 16, 0, 0) };
            var close = BuildCloseButton(delegate { window.Close(); });
            buttons.Children.Add(close);
            Grid.SetRow(buttons, 2);
            root.Children.Add(buttons);

            window.Content = root;
            window.ShowDialog();
        }
    }
}
