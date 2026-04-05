using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        void ClearImageCache()
        {
            libraryBitmapCache.Clear();
        }

        void RemoveCachedImageEntries(IEnumerable<string> sourcePaths)
        {
            libraryBitmapCache.RemoveCachedImageEntries(sourcePaths);
        }

        void RemoveCachedFolderListings(IEnumerable<string> folderPaths)
        {
            libraryWorkspace.RemoveFolderImageListings(folderPaths);
        }

        void RemoveCachedFileTagEntries(IEnumerable<string> files)
        {
            libraryWorkspace.RemoveCachedFileTagEntries(files);
        }

        BitmapImage TryGetCachedImage(string cacheKey)
        {
            return libraryBitmapCache.TryGet(cacheKey);
        }

        void StoreCachedImage(string cacheKey, BitmapImage image)
        {
            libraryBitmapCache.Store(cacheKey, image);
        }

        void QueueImageLoad(Image imageControl, string sourcePath, int decodePixelWidth, Action<BitmapImage> onLoaded, bool prioritize = false, Func<bool> shouldLoad = null)
        {
            if (imageControl == null)
            {
                return;
            }

            var requestToken = Guid.NewGuid().ToString("N");
            var hadSource = imageControl.Source != null;
            var limiter = imageLoadCoordinator.GetLimiter(prioritize);
            imageControl.Uid = requestToken;
            var immediate = TryLoadCachedVisualImmediate(sourcePath, decodePixelWidth);
            if (immediate != null)
            {
                if (shouldLoad != null && !shouldLoad())
                {
                    if (!hadSource) imageControl.Visibility = Visibility.Collapsed;
                    return;
                }
                if (onLoaded != null)
                {
                    onLoaded(immediate);
                    imageControl.Visibility = Visibility.Visible;
                }
                else
                {
                    imageControl.Source = immediate;
                    imageControl.Visibility = Visibility.Visible;
                }
                imageControl.InvalidateMeasure();
                imageControl.InvalidateVisual();
                hadSource = true;
                return;
            }
            if (!hadSource)
            {
                imageControl.Visibility = Visibility.Collapsed;
            }
            Task.Run(async delegate
            {
                await limiter.WaitAsync().ConfigureAwait(false);
                try
                {
                    if (shouldLoad != null && !shouldLoad()) return null;
                    return LoadImageSource(sourcePath, decodePixelWidth);
                }
                finally
                {
                    limiter.Release();
                }
            }).ContinueWith(delegate(Task<BitmapImage> task)
            {
                imageControl.Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (!string.Equals(imageControl.Uid, requestToken, StringComparison.Ordinal)) return;
                    if (shouldLoad != null && !shouldLoad())
                    {
                        if (!hadSource) imageControl.Visibility = Visibility.Collapsed;
                        return;
                    }
                    if (task.IsFaulted || task.IsCanceled)
                    {
                        if (!hadSource) imageControl.Visibility = Visibility.Collapsed;
                        return;
                    }
                    if (task.Result == null)
                    {
                        if (!hadSource) imageControl.Visibility = Visibility.Collapsed;
                        return;
                    }
                    if (onLoaded != null) onLoaded(task.Result);
                    else imageControl.Source = task.Result;
                    imageControl.Visibility = Visibility.Visible;
                    imageControl.InvalidateMeasure();
                    imageControl.InvalidateVisual();
                }), DispatcherPriority.Render);
            }, TaskScheduler.Default);
        }

        Border CreateAsyncImageTile(string sourcePath, int decodePixelWidth, double tileWidth, double tileHeight, Stretch stretch, string fallbackText, Brush fallbackForeground, Thickness margin, Thickness padding, Brush background, CornerRadius cornerRadius, Brush borderBrush, Thickness borderThickness)
        {
            var tile = new Border
            {
                Width = tileWidth,
                Height = tileHeight,
                Margin = margin,
                Padding = padding,
                Background = background,
                CornerRadius = cornerRadius,
                BorderBrush = borderBrush,
                BorderThickness = borderThickness
            };
            var presenter = new Grid();
            var placeholder = new TextBlock
            {
                Text = fallbackText,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(8),
                Foreground = fallbackForeground,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center
            };
            var image = new Image
            {
                Width = tileWidth,
                Height = tileHeight,
                Stretch = stretch,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed
            };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            presenter.Children.Add(placeholder);
            presenter.Children.Add(image);
            tile.Child = presenter;
            QueueImageLoad(image, sourcePath, decodePixelWidth, delegate(BitmapImage loaded)
            {
                image.Source = loaded;
                image.Visibility = Visibility.Visible;
                placeholder.Visibility = Visibility.Collapsed;
            });
            return tile;
        }

        BitmapImage LoadImageSource(string path, int decodePixelWidth)
        {
            return libraryThumbnailPipeline.LoadImageSource(path, decodePixelWidth);
        }

        BitmapImage TryLoadCachedVisualImmediate(string sourcePath, int decodePixelWidth)
        {
            return libraryThumbnailPipeline.TryLoadCachedVisualImmediate(sourcePath, decodePixelWidth);
        }

        BitmapImage LoadFrozenBitmap(string path, int decodePixelWidth)
        {
            return libraryThumbnailPipeline.LoadFrozenBitmap(path, decodePixelWidth);
        }

        /// <summary>Decode width for folder-cover art; scales with tile size and display DPI (capped at pipeline max).</summary>
        int CalculateLibraryFolderArtDecodeWidth(int tileWidthLogical, double dpiScaleX = 1.0)
        {
            var target = (int)Math.Ceiling((tileWidthLogical + 160) * Math.Max(1.0, dpiScaleX));
            target = Math.Max(384, Math.Min(1600, target));
            return LibraryThumbnailPipeline.NormalizeDecodePixelWidth(target);
        }

        int CalculateLibraryBannerArtDecodeWidth(double dpiScaleX = 1.0)
        {
            var target = (int)Math.Ceiling(720 * Math.Max(1.0, dpiScaleX));
            target = Math.Max(512, Math.Min(1280, target));
            return LibraryThumbnailPipeline.NormalizeDecodePixelWidth(target);
        }

        int CalculateLibraryDetailTileDecodeWidth(int tileWidthLogical, double dpiScaleX = 1.0)
        {
            var target = (int)Math.Ceiling((tileWidthLogical + 128) * Math.Max(1.0, dpiScaleX));
            target = Math.Max(384, Math.Min(1600, target));
            return LibraryThumbnailPipeline.NormalizeDecodePixelWidth(target);
        }

        double ResolveLibraryDpiScale(DependencyObject visualHint = null)
        {
            try
            {
                if (visualHint is Visual v) return VisualTreeHelper.GetDpi(v).DpiScaleX;
                return VisualTreeHelper.GetDpi(this).DpiScaleX;
            }
            catch
            {
                return 1.0;
            }
        }
    }
}
