using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Effects;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        SolidColorBrush Brush(string hex) { return UiBrushHelper.FromHex(hex); }
        /// <summary>Download-into-inbox style icon (arrow + open tray); used for intake queue toolbar and preview header.</summary>
        static Canvas BuildIntakeDownloadTrayGlyphCanvas(Brush stroke, double strokeThickness)
        {
            var art = new Canvas { Width = 100, Height = 100 };
            System.Windows.Shapes.Path Path(string data) =>
                new System.Windows.Shapes.Path
                {
                    Data = Geometry.Parse(data),
                    Stroke = stroke,
                    StrokeThickness = strokeThickness,
                    Fill = Brushes.Transparent,
                    StrokeLineJoin = PenLineJoin.Round,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round
                };
            art.Children.Add(Path("M 50 8 L 50 34"));
            art.Children.Add(Path("M 38 34 L 50 46 L 62 34"));
            art.Children.Add(Path("M 18 58 L 18 82 Q 18 88 24 88 L 76 88 Q 82 88 82 82 L 82 58"));
            return art;
        }

        /// <summary>Vector intake-queue icon for reuse outside instance chrome (e.g. intake preview dialog).</summary>
        public static FrameworkElement BuildIntakeDownloadTrayGlyph(Brush stroke, double strokeThickness, double width, double height)
        {
            return new Viewbox
            {
                Width = width,
                Height = height,
                Stretch = Stretch.Uniform,
                Child = BuildIntakeDownloadTrayGlyphCanvas(stroke, strokeThickness)
            };
        }

        FrameworkElement BuildSymbolIcon(string glyph, string foregroundHex, double fontSize)
        {
            return new TextBlock
            {
                Text = glyph,
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = fontSize,
                Foreground = Brush(foregroundHex),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center
            };
        }
        object BuildToolbarButtonContent(string glyph, string label, string iconHex = "#D8E4EA")
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(new Border
            {
                Width = 20,
                Height = 20,
                Background = Brushes.Transparent,
                Margin = new Thickness(0, 0, 8, 0),
                Child = BuildSymbolIcon(glyph, iconHex, 13)
            });
            row.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = Brushes.White,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });
            return row;
        }
        object BuildToolbarIconButtonContent(string glyph, string iconHex = "#D8E4EA", double fontSize = 15)
        {
            return new Border
            {
                Width = 18,
                Height = 18,
                Background = Brushes.Transparent,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = BuildSymbolIcon(glyph, iconHex, fontSize)
            };
        }
        string IntakeBadgeCountText(int count)
        {
            if (count <= 0) return string.Empty;
            if (count > 99) return "99+";
            return count.ToString();
        }
        double PreferredLibraryWindowWidth()
        {
            var available = Math.Max(720, SystemParameters.WorkArea.Width - 24);
            return Math.Min(available, 1292);
        }
        double PreferredLibraryWindowHeight()
        {
            var available = Math.Max(520, SystemParameters.WorkArea.Height - 24);
            return Math.Min(available, 1398);
        }
        string ResolveWorkspaceAssetPath(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return string.Empty;
            foreach (var basePath in new[]
            {
                Path.Combine(appRoot, "assets"),
                Path.Combine(appRoot, "assets", "assets"),
                Path.GetFullPath(Path.Combine(appRoot, "..", "..", "assets"))
            }.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var candidate = Path.Combine(basePath, fileName);
                    if (File.Exists(candidate)) return candidate;
                }
                catch
                {
                }
            }
            return string.Empty;
        }
        string ResolveLibrarySectionIconPath(string platformLabel)
        {
            var normalized = NormalizeConsoleLabel(platformLabel);
            switch (normalized)
            {
                case "Steam":
                    return ResolveWorkspaceAssetPath("Steam Library Icon.png");
                case "PS5":
                case "PlayStation":
                    return ResolveWorkspaceAssetPath("PS5 Library Logo.png");
                case "Nintendo":
                case "Switch":
                    return ResolveWorkspaceAssetPath("Nintendo Library Icon.png");
                case "Xbox 360":
                case "Xbox":
                    return ResolveWorkspaceAssetPath("Xbox Library Logo.png");
                case "PC":
                    return ResolveWorkspaceAssetPath("PC Library Icon.png");
                case "Emulation":
                case "Multiple Tags":
                case "Other":
                    return ResolveWorkspaceAssetPath("emulator library licon.png");
                default:
                    return ResolveWorkspaceAssetPath("PixelVault.png");
            }
        }
        SolidColorBrush LibrarySectionAccentBrush(string platformLabel)
        {
            switch (NormalizeConsoleLabel(platformLabel))
            {
                case "Steam":
                    return Brush("#3A8FD6");
                case "PS5":
                case "PlayStation":
                    return Brush("#4E7CFF");
                case "Nintendo":
                case "Switch":
                    return Brush("#E94B43");
                case "Xbox 360":
                case "Xbox":
                    return Brush("#69B157");
                case "PC":
                    return Brush("#8DA0AF");
                case "Emulation":
                    return Brush("#B26A3C");
                case "Multiple Tags":
                    return Brush("#D39B4A");
                default:
                    return Brush("#B67ECF");
            }
        }
        FrameworkElement BuildLibraryPlatformIconVisual(string platformLabel, double size, Thickness margin, HorizontalAlignment horizontalAlignment = HorizontalAlignment.Center, VerticalAlignment verticalAlignment = VerticalAlignment.Center)
        {
            var resolvedLabel = NormalizeConsoleLabel(platformLabel);
            if (string.IsNullOrWhiteSpace(resolvedLabel)) return null;
            var iconPath = ResolveLibrarySectionIconPath(resolvedLabel);
            var accent = LibrarySectionAccentBrush(resolvedLabel);
            if (!string.IsNullOrWhiteSpace(iconPath))
            {
                return new Image
                {
                    Width = size,
                    Height = size,
                    Margin = margin,
                    Source = LoadImageSource(iconPath, Math.Max(64, (int)Math.Ceiling(size * 2))),
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = horizontalAlignment,
                    VerticalAlignment = verticalAlignment,
                    SnapsToDevicePixels = true,
                    IsHitTestVisible = false
                };
            }
            return new Border
            {
                Width = size,
                Height = size,
                Margin = margin,
                Background = Brushes.Transparent,
                HorizontalAlignment = horizontalAlignment,
                VerticalAlignment = verticalAlignment,
                IsHitTestVisible = false,
                Child = new TextBlock
                {
                    Text = resolvedLabel == "Multiple Tags" ? "+" : "•",
                    FontSize = Math.Max(18, size * 0.5),
                    FontWeight = FontWeights.Bold,
                    Foreground = accent,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center
                }
            };
        }
        string LibrarySectionCountLabel(int count)
        {
            return count == 1 ? "game" : "games";
        }
        FrameworkElement BuildLibraryTilePlatformBadge(string platformLabel)
        {
            var normalized = NormalizeConsoleLabel(platformLabel);
            var iconPath = ResolveLibrarySectionIconPath(normalized);
            var accent = LibrarySectionAccentBrush(normalized);
            const double badgeSize = 68;
            var badgeMargin = new Thickness(0, 0, 10, 10);
            if (!string.IsNullOrWhiteSpace(iconPath))
            {
                return new Image
                {
                    Width = badgeSize,
                    Height = badgeSize,
                    Source = LoadImageSource(iconPath, 136),
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = badgeMargin,
                    Opacity = 0.98,
                    SnapsToDevicePixels = true,
                    IsHitTestVisible = false
                };
            }
            return new Border
            {
                Width = badgeSize,
                Height = badgeSize,
                Background = Brushes.Transparent,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = badgeMargin,
                IsHitTestVisible = false,
                Child = new TextBlock
                {
                    Text = normalized == "Multiple Tags" ? "+" : "•",
                    FontSize = 28,
                    FontWeight = FontWeights.Bold,
                    Foreground = accent,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center
                }
            };
        }
        FrameworkElement BuildLibrarySectionHeader(string platformLabel, int folderCount, bool sectionCollapsed, Action toggleSectionCollapse)
        {
            var resolvedLabel = NormalizeConsoleLabel(platformLabel);
            var accent = LibrarySectionAccentBrush(resolvedLabel);

            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition());
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var chevronGlyph = new TextBlock
            {
                Text = sectionCollapsed ? "\uE76C" : "\uE70D",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 13,
                Foreground = Brush("#C5D4DE"),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            var chevronHit = new Border
            {
                Width = 36,
                MinHeight = 48,
                Margin = new Thickness(0, 0, 6, 0),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                Child = chevronGlyph,
                ToolTip = sectionCollapsed ? "Expand section" : "Collapse section"
            };
            if (toggleSectionCollapse != null)
            {
                chevronHit.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
                {
                    e.Handled = true;
                    toggleSectionCollapse();
                };
            }
            headerGrid.Children.Add(chevronHit);

            var iconVisual = BuildLibraryPlatformIconVisual(resolvedLabel, 34, new Thickness(0, 0, 10, 0));
            if (iconVisual != null)
            {
                Grid.SetColumn(iconVisual, 1);
                headerGrid.Children.Add(iconVisual);
            }

            var titleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            titleStack.Children.Add(new TextBlock
            {
                Text = resolvedLabel,
                FontSize = 20,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            });
            titleStack.Children.Add(new Border
            {
                Width = 78,
                Height = 3,
                CornerRadius = new CornerRadius(999),
                Background = accent,
                Margin = new Thickness(0, 7, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left
            });
            Grid.SetColumn(titleStack, 2);
            headerGrid.Children.Add(titleStack);

            var countLine = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            countLine.Children.Add(new TextBlock
            {
                Text = folderCount.ToString(),
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            });
            countLine.Children.Add(new TextBlock
            {
                Text = "\u00A0" + LibrarySectionCountLabel(folderCount),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brush("#9AAAB4"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 1, 0, 0)
            });
            Grid.SetColumn(countLine, 3);
            headerGrid.Children.Add(countLine);

            return new Border
            {
                Background = new LinearGradientBrush(
                    (Color)ColorConverter.ConvertFromString("#1B252B"),
                    (Color)ColorConverter.ConvertFromString("#12191D"),
                    90),
                BorderBrush = Brush("#2A3942"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(18),
                Padding = new Thickness(16, 12, 18, 12),
                Child = headerGrid
            };
        }
    }
}
