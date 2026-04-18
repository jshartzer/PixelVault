using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Effects;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

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
        string IntakeBadgeCountText(int count)
        {
            if (count <= 0) return string.Empty;
            if (count > 99) return "99+";
            return count.ToString();
        }
        double PreferredLibraryWindowWidth()
        {
            var available = Math.Max(720, SystemParameters.WorkArea.Width - 24);
            return Math.Min(available, 2560);
        }
        double PreferredLibraryWindowHeight()
        {
            var available = Math.Max(520, SystemParameters.WorkArea.Height - 24);
            return Math.Min(available, 1280);
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
        static bool IsNearWhiteBitmapPixel(byte[] pixels, int offset)
        {
            if (pixels == null || offset < 0 || offset + 3 >= pixels.Length) return false;
            var alpha = pixels[offset + 3];
            if (alpha == 0) return false;
            return pixels[offset] >= 240 && pixels[offset + 1] >= 240 && pixels[offset + 2] >= 240;
        }
        static void RemoveEdgeConnectedNearWhitePixels(byte[] pixels, int width, int height, int stride)
        {
            if (pixels == null || width <= 0 || height <= 0 || stride <= 0) return;
            var visited = new bool[width * height];
            var queue = new Queue<int>();
            void TryEnqueue(int x, int y)
            {
                if (x < 0 || y < 0 || x >= width || y >= height) return;
                var index = (y * width) + x;
                if (visited[index]) return;
                var pixelOffset = (y * stride) + (x * 4);
                if (!IsNearWhiteBitmapPixel(pixels, pixelOffset)) return;
                visited[index] = true;
                queue.Enqueue(index);
            }

            for (var x = 0; x < width; x++)
            {
                TryEnqueue(x, 0);
                TryEnqueue(x, height - 1);
            }
            for (var y = 1; y < height - 1; y++)
            {
                TryEnqueue(0, y);
                TryEnqueue(width - 1, y);
            }

            while (queue.Count > 0)
            {
                var index = queue.Dequeue();
                var x = index % width;
                var y = index / width;
                var pixelOffset = (y * stride) + (x * 4);
                pixels[pixelOffset + 3] = 0;
                TryEnqueue(x - 1, y);
                TryEnqueue(x + 1, y);
                TryEnqueue(x, y - 1);
                TryEnqueue(x, y + 1);
            }
        }
        static void KeepLargestOpaqueComponent(byte[] pixels, int width, int height, int stride)
        {
            if (pixels == null || width <= 0 || height <= 0 || stride <= 0) return;
            var visited = new bool[width * height];
            List<int> largestComponent = null;
            var queue = new Queue<int>();

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var startIndex = (y * width) + x;
                    if (visited[startIndex]) continue;
                    visited[startIndex] = true;
                    var startOffset = (y * stride) + (x * 4);
                    if (pixels[startOffset + 3] == 0) continue;

                    var component = new List<int>();
                    queue.Enqueue(startIndex);
                    while (queue.Count > 0)
                    {
                        var index = queue.Dequeue();
                        component.Add(index);
                        var cx = index % width;
                        var cy = index / width;

                        void TryVisit(int nx, int ny)
                        {
                            if (nx < 0 || ny < 0 || nx >= width || ny >= height) return;
                            var neighborIndex = (ny * width) + nx;
                            if (visited[neighborIndex]) return;
                            visited[neighborIndex] = true;
                            var neighborOffset = (ny * stride) + (nx * 4);
                            if (pixels[neighborOffset + 3] == 0) return;
                            queue.Enqueue(neighborIndex);
                        }

                        TryVisit(cx - 1, cy);
                        TryVisit(cx + 1, cy);
                        TryVisit(cx, cy - 1);
                        TryVisit(cx, cy + 1);
                    }

                    if (largestComponent == null || component.Count > largestComponent.Count)
                    {
                        largestComponent = component;
                    }
                }
            }

            if (largestComponent == null || largestComponent.Count == 0) return;

            var keep = new bool[width * height];
            foreach (var index in largestComponent) keep[index] = true;

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var index = (y * width) + x;
                    if (keep[index]) continue;
                    var pixelOffset = (y * stride) + (x * 4);
                    pixels[pixelOffset + 3] = 0;
                }
            }
        }

        /// <summary>~12% less saturation + slight warm shift so medal reads quieter against dark UI chrome.</summary>
        static BitmapSource TuneCompletionBadgeBitmap(BitmapSource source)
        {
            if (source == null) return null;
            var fmt = PixelFormats.Pbgra32;
            BitmapSource src;
            if (source.Format == fmt)
            {
                src = source;
            }
            else
            {
                var converted = new FormatConvertedBitmap(source, fmt, null, 0);
                converted.Freeze();
                src = converted;
            }

            var w = src.PixelWidth;
            var h = src.PixelHeight;
            if (w <= 0 || h <= 0) return source;

            var stride = w * 4;
            var pixels = new byte[stride * h];
            src.CopyPixels(pixels, stride, 0);
            const double satRetain = 0.88;
            var invW = w <= 1 ? 0.0 : 1.0 / (w - 1);
            var invH = h <= 1 ? 0.0 : 1.0 / (h - 1);
            for (var y = 0; y < h; y++)
            {
                var fy = y * invH;
                for (var x = 0; x < w; x++)
                {
                    var i = (y * stride) + (x * 4);
                    if (pixels[i + 3] == 0) continue;
                    var fx = x * invW;
                    var B = pixels[i];
                    var G = pixels[i + 1];
                    var R = pixels[i + 2];
                    var lum = 0.299 * R + 0.587 * G + 0.114 * B;
                    var r = lum + (R - lum) * satRetain;
                    var g = lum + (G - lum) * satRetain;
                    var b = lum + (B - lum) * satRetain;
                    r = Math.Min(255, r + 4);
                    g = Math.Min(255, g + 1);
                    b = Math.Max(0, b - 6);
                    // Subtle TL highlight / BR shade (baked; avoids a rectangular gloss layer in WPF).
                    var lightMul = 1.0 + (1.0 - fx) * (1.0 - fy) * 0.068 - fx * fy * 0.058;
                    if (lightMul < 0.94) lightMul = 0.94;
                    if (lightMul > 1.07) lightMul = 1.07;
                    r *= lightMul;
                    g *= lightMul;
                    b *= lightMul;
                    pixels[i] = (byte)Math.Max(0, Math.Min(255, Math.Round(b)));
                    pixels[i + 1] = (byte)Math.Max(0, Math.Min(255, Math.Round(g)));
                    pixels[i + 2] = (byte)Math.Max(0, Math.Min(255, Math.Round(r)));
                }
            }

            var wb = new WriteableBitmap(w, h, 96, 96, fmt, null);
            wb.WritePixels(new Int32Rect(0, 0, w, h), pixels, stride, 0);
            wb.Freeze();
            return wb;
        }

        BitmapSource LoadCompletionBadgeBitmap()
        {
            if (libraryCompletionBadgeBitmap != null) return libraryCompletionBadgeBitmap;
            var path = ResolveWorkspaceAssetPath("100 Percent Medal.png")
                ?? ResolveWorkspaceAssetPath("100 Percent Icon.png");
            if (string.IsNullOrWhiteSpace(path)) return null;
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.DecodePixelHeight = 256;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                try
                {
                    libraryCompletionBadgeBitmap = TuneCompletionBadgeBitmap(bmp) ?? bmp;
                }
                catch
                {
                    libraryCompletionBadgeBitmap = bmp;
                }
                return libraryCompletionBadgeBitmap;
            }
            catch
            {
                return null;
            }
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
                case "Xbox":
                    return ResolveWorkspaceAssetPath("Xbox Library Logo.png");
                case "Xbox PC":
                    return ResolveWorkspaceAssetPath("Xbox PC Library Icon.png");
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
                case "Xbox":
                    return Brush("#69B157");
                case "Xbox PC":
                    return Brush("#5FA77A");
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
        FrameworkElement BuildLibraryTileCompletionBadge(double targetHeight = 66, Thickness? margin = null)
        {
            var badgeBitmap = LoadCompletionBadgeBitmap();
            var resolvedMargin = margin ?? new Thickness(0, 13, 12, 0);
            var shadow = new DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 20,
                ShadowDepth = 5,
                Direction = 315,
                Opacity = 0.52
            };

            if (badgeBitmap != null)
            {
                var targetWidth = targetHeight;
                if (badgeBitmap.PixelWidth > 0 && badgeBitmap.PixelHeight > 0)
                {
                    targetWidth = Math.Max(44, Math.Min(92, targetHeight * badgeBitmap.PixelWidth / (double)badgeBitmap.PixelHeight));
                }
                return new Image
                {
                    Source = badgeBitmap,
                    Width = targetWidth,
                    Height = targetHeight,
                    Stretch = Stretch.Uniform,
                    SnapsToDevicePixels = true,
                    Effect = shadow,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = resolvedMargin,
                    IsHitTestVisible = false
                };
            }

            var text = new TextBlock
            {
                Text = "100%",
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Effect = shadow
            };
            text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var tw = Math.Max(44, Math.Ceiling(text.DesiredSize.Width) + 18);
            var th = Math.Max(28, Math.Ceiling(text.DesiredSize.Height) + 8);
            var textHost = new Grid
            {
                Width = tw,
                Height = th,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = resolvedMargin,
                IsHitTestVisible = false
            };
            textHost.Children.Add(text);
            return textHost;
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
