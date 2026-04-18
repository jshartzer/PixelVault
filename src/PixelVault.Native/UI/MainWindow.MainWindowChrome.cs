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
        Brush BuildLibraryTileCompletionBorderBrush()
        {
            var brush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1)
            };
            brush.GradientStops.Add(new GradientStop(Brush("#FFF2BF").Color, 0));
            brush.GradientStops.Add(new GradientStop(Brush("#E0B44A").Color, 0.28));
            brush.GradientStops.Add(new GradientStop(Brush("#F8D979").Color, 0.62));
            brush.GradientStops.Add(new GradientStop(Brush("#936915").Color, 1));
            if (brush.CanFreeze) brush.Freeze();
            return brush;
        }
        sealed class LibraryTileCompletionFoilVisual
        {
            readonly Grid root;
            readonly Border glossLayer;
            readonly Border prismLayer;
            readonly Border crossLayer;
            readonly Border stripeLayer;
            readonly Border glintLayer;
            readonly TranslateTransform glossShift;
            readonly TranslateTransform prismShift;
            readonly TranslateTransform crossShift;
            readonly TranslateTransform stripeShift;
            readonly TranslateTransform glintShift;

            internal LibraryTileCompletionFoilVisual(
                Grid root,
                Border glossLayer,
                Border prismLayer,
                Border crossLayer,
                Border stripeLayer,
                Border glintLayer,
                TranslateTransform glossShift,
                TranslateTransform prismShift,
                TranslateTransform crossShift,
                TranslateTransform stripeShift,
                TranslateTransform glintShift)
            {
                this.root = root;
                this.glossLayer = glossLayer;
                this.prismLayer = prismLayer;
                this.crossLayer = crossLayer;
                this.stripeLayer = stripeLayer;
                this.glintLayer = glintLayer;
                this.glossShift = glossShift;
                this.prismShift = prismShift;
                this.crossShift = crossShift;
                this.stripeShift = stripeShift;
                this.glintShift = glintShift;
            }

            internal FrameworkElement Root => root;

            internal void Update(double normalizedX, double normalizedY, bool animate)
            {
                normalizedX = Math.Max(0, Math.Min(1, normalizedX));
                normalizedY = Math.Max(0, Math.Min(1, normalizedY));

                var xBias = normalizedX - 0.5;
                var yBias = normalizedY - 0.5;
                var shimmer = Math.Max(Math.Abs(xBias), Math.Abs(yBias));

                SetDouble(root, UIElement.OpacityProperty, 0.98d + shimmer * 0.08d, animate, 150);
                SetDouble(glossShift, TranslateTransform.XProperty, xBias * 10d, animate, 150);
                SetDouble(glossShift, TranslateTransform.YProperty, yBias * 8d, animate, 150);
                SetDouble(prismShift, TranslateTransform.XProperty, xBias * 36d - yBias * 9d, animate, 150);
                SetDouble(prismShift, TranslateTransform.YProperty, yBias * 22d - xBias * 8d, animate, 150);
                SetDouble(crossShift, TranslateTransform.XProperty, -xBias * 30d + yBias * 12d, animate, 150);
                SetDouble(crossShift, TranslateTransform.YProperty, -yBias * 16d + xBias * 10d, animate, 150);
                SetDouble(stripeShift, TranslateTransform.XProperty, xBias * 14d, animate, 180);
                SetDouble(stripeShift, TranslateTransform.YProperty, yBias * 11d, animate, 180);
                SetDouble(glintShift, TranslateTransform.XProperty, xBias * 20d + yBias * 12d, animate, 120);
                SetDouble(glintShift, TranslateTransform.YProperty, yBias * 12d - xBias * 8d, animate, 120);

                SetDouble(glossLayer, UIElement.OpacityProperty, 0.36d + shimmer * 0.36d, animate, 150);
                SetDouble(prismLayer, UIElement.OpacityProperty, 0.30d + shimmer * 0.42d, animate, 150);
                SetDouble(crossLayer, UIElement.OpacityProperty, 0.22d + shimmer * 0.22d, animate, 150);
                SetDouble(stripeLayer, UIElement.OpacityProperty, 0.15d + shimmer * 0.18d, animate, 180);
                SetDouble(glintLayer, UIElement.OpacityProperty, 0.28d + shimmer * 0.30d, animate, 120);
            }

            static void SetDouble(DependencyObject target, DependencyProperty property, double value, bool animate, int durationMs)
            {
                if (target is Animatable animatable)
                {
                    if (animate && durationMs > 0)
                    {
                        animatable.BeginAnimation(property, new DoubleAnimation
                        {
                            To = value,
                            Duration = TimeSpan.FromMilliseconds(durationMs),
                            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                            FillBehavior = FillBehavior.HoldEnd
                        }, HandoffBehavior.SnapshotAndReplace);
                        return;
                    }

                    animatable.BeginAnimation(property, null);
                }

                target.SetValue(property, value);
            }
        }
        FrameworkElement BuildLibraryTileCompletionFrameOverlay(double width, double height, CornerRadius cornerRadius)
        {
            var frame = new Grid
            {
                Width = width,
                Height = height,
                IsHitTestVisible = false
            };
            var frameRadius = cornerRadius.TopLeft;
            var frameClip = new RectangleGeometry(new Rect(0, 0, width, height), frameRadius, frameRadius);
            if (frameClip.CanFreeze) frameClip.Freeze();
            frame.Clip = frameClip;

            frame.Children.Add(new Border
            {
                CornerRadius = cornerRadius,
                Background = Brushes.Transparent,
                BorderBrush = BuildLibraryTileCompletionBorderBrush(),
                BorderThickness = new Thickness(2)
            });
            frame.Children.Add(new Border
            {
                Margin = new Thickness(1),
                CornerRadius = new CornerRadius(
                    Math.Max(0, cornerRadius.TopLeft - 1),
                    Math.Max(0, cornerRadius.TopRight - 1),
                    Math.Max(0, cornerRadius.BottomRight - 1),
                    Math.Max(0, cornerRadius.BottomLeft - 1)),
                Background = Brushes.Transparent,
                BorderBrush = Brush("#6EF6DE7A"),
                BorderThickness = new Thickness(2)
            });

            return frame;
        }
        LibraryTileCompletionFoilVisual BuildLibraryTileCompletionFoilOverlay(double tileWidth, double tileHeight, double cornerRadius)
        {
            var foil = new Grid
            {
                Width = tileWidth,
                Height = tileHeight,
                IsHitTestVisible = false,
                Opacity = 0.96,
                ClipToBounds = false
            };
            var foilClip = new RectangleGeometry(new Rect(0, 0, tileWidth, tileHeight), cornerRadius, cornerRadius);
            if (foilClip.CanFreeze) foilClip.Freeze();
            foil.Clip = foilClip;
            var glossShift = new TranslateTransform();
            var prismShift = new TranslateTransform();
            var crossShift = new TranslateTransform();
            var stripeShift = new TranslateTransform();
            var glintShift = new TranslateTransform();

            var glossBrush = new LinearGradientBrush
            {
                StartPoint = new Point(0.08, 0),
                EndPoint = new Point(0.92, 1)
            };
            glossBrush.GradientStops.Add(new GradientStop(Brush("#00FFFFFF").Color, 0));
            glossBrush.GradientStops.Add(new GradientStop(Brush("#44FFFFFF").Color, 0.22));
            glossBrush.GradientStops.Add(new GradientStop(Brush("#28F9FCFF").Color, 0.40));
            glossBrush.GradientStops.Add(new GradientStop(Brush("#50FFFFFF").Color, 0.60));
            glossBrush.GradientStops.Add(new GradientStop(Brush("#10FFFFFF").Color, 0.82));
            glossBrush.GradientStops.Add(new GradientStop(Brush("#00FFFFFF").Color, 1));
            if (glossBrush.CanFreeze) glossBrush.Freeze();
            var glossLayer = new Border
            {
                Background = glossBrush,
                Margin = new Thickness(-12),
                RenderTransform = glossShift
            };
            foil.Children.Add(glossLayer);

            var prismSweepBrush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0.94),
                EndPoint = new Point(1, 0.08)
            };
            prismSweepBrush.GradientStops.Add(new GradientStop(Brush("#00FFFFFF").Color, 0.26));
            prismSweepBrush.GradientStops.Add(new GradientStop(Brush("#A0A8F6FF").Color, 0.38));
            prismSweepBrush.GradientStops.Add(new GradientStop(Brush("#A8E39BFF").Color, 0.46));
            prismSweepBrush.GradientStops.Add(new GradientStop(Brush("#BBFFF4A8").Color, 0.54));
            prismSweepBrush.GradientStops.Add(new GradientStop(Brush("#A0FFB6E8").Color, 0.61));
            prismSweepBrush.GradientStops.Add(new GradientStop(Brush("#72FFE7C8").Color, 0.68));
            prismSweepBrush.GradientStops.Add(new GradientStop(Brush("#00FFFFFF").Color, 0.80));
            prismSweepBrush.GradientStops.Add(new GradientStop(Brush("#00FFFFFF").Color, 1));
            if (prismSweepBrush.CanFreeze) prismSweepBrush.Freeze();
            var prismLayer = new Border
            {
                Background = prismSweepBrush,
                Opacity = 0.30,
                Margin = new Thickness(-32),
                RenderTransform = prismShift
            };
            foil.Children.Add(prismLayer);

            var crossSweepBrush = new LinearGradientBrush
            {
                StartPoint = new Point(0.06, 0.18),
                EndPoint = new Point(0.94, 0.82)
            };
            crossSweepBrush.GradientStops.Add(new GradientStop(Brush("#00FFFFFF").Color, 0.38));
            crossSweepBrush.GradientStops.Add(new GradientStop(Brush("#00FFFFFF").Color, 0.48));
            crossSweepBrush.GradientStops.Add(new GradientStop(Brush("#66C6A8FF").Color, 0.55));
            crossSweepBrush.GradientStops.Add(new GradientStop(Brush("#44FFF6D7").Color, 0.61));
            crossSweepBrush.GradientStops.Add(new GradientStop(Brush("#00FFFFFF").Color, 0.72));
            crossSweepBrush.GradientStops.Add(new GradientStop(Brush("#00FFFFFF").Color, 1));
            if (crossSweepBrush.CanFreeze) crossSweepBrush.Freeze();
            var crossLayer = new Border
            {
                Background = crossSweepBrush,
                Opacity = 0.22,
                Margin = new Thickness(-26),
                RenderTransform = crossShift
            };
            foil.Children.Add(crossLayer);

            var stripeDrawing = new DrawingGroup();
            stripeDrawing.Children.Add(new GeometryDrawing(
                Brushes.Transparent,
                new Pen(Brush("#58FFFFFF"), 1.4),
                Geometry.Parse("M 0 10 L 10 0")));
            if (stripeDrawing.CanFreeze) stripeDrawing.Freeze();
            var stripeBrush = new DrawingBrush(stripeDrawing)
            {
                TileMode = TileMode.Tile,
                Stretch = Stretch.None,
                Viewbox = new Rect(0, 0, 10, 10),
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewport = new Rect(0, 0, 10, 10),
                ViewportUnits = BrushMappingMode.Absolute
            };
            if (stripeBrush.CanFreeze) stripeBrush.Freeze();
            var stripeLayer = new Border
            {
                Background = stripeBrush,
                Opacity = 0.12,
                Margin = new Thickness(-10),
                RenderTransform = stripeShift
            };
            foil.Children.Add(stripeLayer);

            var topGlintBrush = new RadialGradientBrush
            {
                Center = new Point(0.22, 0.18),
                GradientOrigin = new Point(0.22, 0.18),
                RadiusX = 0.32,
                RadiusY = 0.26
            };
            topGlintBrush.GradientStops.Add(new GradientStop(Brush("#88FFFFFF").Color, 0));
            topGlintBrush.GradientStops.Add(new GradientStop(Brush("#50FFF9F0").Color, 0.28));
            topGlintBrush.GradientStops.Add(new GradientStop(Brush("#18FFFFFF").Color, 0.60));
            topGlintBrush.GradientStops.Add(new GradientStop(Brush("#00FFFFFF").Color, 1));
            if (topGlintBrush.CanFreeze) topGlintBrush.Freeze();
            var glintLayer = new Border
            {
                Background = topGlintBrush,
                Opacity = 0.28,
                Margin = new Thickness(-16),
                RenderTransform = glintShift
            };
            foil.Children.Add(glintLayer);

            return new LibraryTileCompletionFoilVisual(foil, glossLayer, prismLayer, crossLayer, stripeLayer, glintLayer, glossShift, prismShift, crossShift, stripeShift, glintShift);
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
