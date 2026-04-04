using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Media.Imaging;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        void ClearImageCache()
        {
            lock (imageCacheSync)
            {
                imageCache.Clear();
                imageCacheOrder.Clear();
                imageCacheOrderNodes.Clear();
            }
        }

        void RemoveCachedImageEntries(IEnumerable<string> sourcePaths)
        {
            var normalizedPaths = new HashSet<string>(
                (sourcePaths ?? Enumerable.Empty<string>())
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Select(path =>
                    {
                        try
                        {
                            return Path.GetFullPath(path);
                        }
                        catch
                        {
                            return path;
                        }
                    }),
                StringComparer.OrdinalIgnoreCase);
            if (normalizedPaths.Count == 0) return;
            lock (imageCacheSync)
            {
                var keysToRemove = imageCache.Keys
                    .Where(cacheKey =>
                    {
                        if (string.IsNullOrWhiteSpace(cacheKey)) return false;
                        var separatorIndex = cacheKey.IndexOf('|');
                        var cachedPath = separatorIndex >= 0 ? cacheKey.Substring(0, separatorIndex) : cacheKey;
                        return normalizedPaths.Contains(cachedPath);
                    })
                    .ToList();
                foreach (var cacheKey in keysToRemove)
                {
                    imageCache.Remove(cacheKey);
                    LinkedListNode<string> node;
                    if (imageCacheOrderNodes.TryGetValue(cacheKey, out node) && node != null)
                    {
                        imageCacheOrder.Remove(node);
                    }
                    imageCacheOrderNodes.Remove(cacheKey);
                }
            }
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
            if (string.IsNullOrWhiteSpace(cacheKey)) return null;
            lock (imageCacheSync)
            {
                BitmapImage cached;
                if (!imageCache.TryGetValue(cacheKey, out cached)) return null;
                LinkedListNode<string> node;
                if (imageCacheOrderNodes.TryGetValue(cacheKey, out node) && node != null)
                {
                    imageCacheOrder.Remove(node);
                    imageCacheOrder.AddLast(node);
                }
                return cached;
            }
        }

        void StoreCachedImage(string cacheKey, BitmapImage image)
        {
            if (string.IsNullOrWhiteSpace(cacheKey) || image == null) return;
            lock (imageCacheSync)
            {
                LinkedListNode<string> existingNode;
                if (imageCache.ContainsKey(cacheKey))
                {
                    imageCache[cacheKey] = image;
                    if (imageCacheOrderNodes.TryGetValue(cacheKey, out existingNode) && existingNode != null)
                    {
                        imageCacheOrder.Remove(existingNode);
                        imageCacheOrder.AddLast(existingNode);
                    }
                    return;
                }
                imageCache[cacheKey] = image;
                var node = new LinkedListNode<string>(cacheKey);
                imageCacheOrder.AddLast(node);
                imageCacheOrderNodes[cacheKey] = node;
                while (imageCache.Count > MaxImageCacheEntries && imageCacheOrder.Count > 0)
                {
                    var firstNode = imageCacheOrder.First;
                    if (firstNode == null) break;
                    imageCacheOrder.RemoveFirst();
                    var oldest = firstNode.Value;
                    if (string.IsNullOrWhiteSpace(oldest)) continue;
                    imageCache.Remove(oldest);
                    imageCacheOrderNodes.Remove(oldest);
                }
            }
        }

        void QueueImageLoad(Image imageControl, string sourcePath, int decodePixelWidth, Action<BitmapImage> onLoaded, bool prioritize = false, Func<bool> shouldLoad = null)
        {
            if (imageControl == null)
            {
                return;
            }

            var requestToken = Guid.NewGuid().ToString("N");
            var hadSource = imageControl.Source != null;
            var limiter = prioritize ? priorityImageLoadLimiter : imageLoadLimiter;
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
            try
            {
                if (string.IsNullOrWhiteSpace(path)) return null;
                string fullPath;
                try
                {
                    fullPath = Path.GetFullPath(path.Trim());
                }
                catch
                {
                    return null;
                }

                if (!File.Exists(fullPath)) return null;
                var sourcePath = fullPath;
                var normalizedDecodePixelWidth = NormalizeThumbnailDecodeWidth(decodePixelWidth);
                if (IsVideo(fullPath))
                {
                    var poster = EnsureVideoPoster(fullPath, normalizedDecodePixelWidth);
                    if (!string.IsNullOrWhiteSpace(poster) && File.Exists(poster)) fullPath = poster;
                }
                var info = new FileInfo(fullPath);
                var cacheKey = fullPath + "|" + info.LastWriteTimeUtc.Ticks + "|" + info.Length + "|" + normalizedDecodePixelWidth;
                var cached = TryGetCachedImage(cacheKey);
                if (cached != null) return cached;

                BitmapImage image = null;
                var thumbnailPath = IsVideo(sourcePath) ? null : ThumbnailCachePath(fullPath, normalizedDecodePixelWidth);
                if (!string.IsNullOrWhiteSpace(thumbnailPath) && File.Exists(thumbnailPath))
                {
                    image = LoadFrozenBitmap(thumbnailPath, 0);
                }
                if (image == null)
                {
                    image = LoadFrozenBitmap(fullPath, normalizedDecodePixelWidth);
                    if (image != null && !string.IsNullOrWhiteSpace(thumbnailPath) && !File.Exists(thumbnailPath))
                    {
                        SaveThumbnailCache(image, thumbnailPath);
                    }
                }
                StoreCachedImage(cacheKey, image);
                return image;
            }
            catch
            {
                return null;
            }
        }

        int NormalizeThumbnailDecodeWidth(int decodePixelWidth)
        {
            if (decodePixelWidth <= 0) return 0;
            if (decodePixelWidth <= 160) return 160;
            if (decodePixelWidth <= 256) return 256;
            if (decodePixelWidth <= 384) return 384;
            if (decodePixelWidth <= 512) return 512;
            if (decodePixelWidth <= 640) return 640;
            if (decodePixelWidth <= 768) return 768;
            if (decodePixelWidth <= 960) return 960;
            if (decodePixelWidth <= 1280) return 1280;
            return 1600;
        }

        string ThumbnailCachePath(string sourcePath, int decodePixelWidth)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath)) return null;
            var normalizedDecodePixelWidth = NormalizeThumbnailDecodeWidth(decodePixelWidth);
            if (normalizedDecodePixelWidth <= 0 || normalizedDecodePixelWidth > 1600) return null;
            try
            {
                var info = new FileInfo(sourcePath);
                var key = sourcePath + "|" + info.LastWriteTimeUtc.Ticks + "|" + info.Length + "|" + normalizedDecodePixelWidth;
                using (var md5 = MD5.Create())
                {
                    var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(key));
                    var name = BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
                    return Path.Combine(thumbsRoot, name + ".png");
                }
            }
            catch
            {
                return null;
            }
        }

        string ExistingVideoPosterPath(string videoPath, int decodePixelWidth)
        {
            if (string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath)) return null;
            try
            {
                var info = new FileInfo(videoPath);
                var normalizedDecodePixelWidth = NormalizeThumbnailDecodeWidth(decodePixelWidth);
                var width = Math.Max(320, normalizedDecodePixelWidth > 0 ? normalizedDecodePixelWidth : 720);
                var keySource = videoPath + "|" + info.LastWriteTimeUtc.Ticks + "|" + info.Length + "|" + width;
                string hash;
                using (var md5 = MD5.Create())
                {
                    hash = BitConverter.ToString(md5.ComputeHash(Encoding.UTF8.GetBytes(keySource))).Replace("-", string.Empty).ToLowerInvariant();
                }
                var framePosterPath = Path.Combine(thumbsRoot, "video-" + hash + "-frame.png");
                if (File.Exists(framePosterPath)) return framePosterPath;
                var fallbackPosterPath = Path.Combine(thumbsRoot, "video-" + hash + "-fallback.png");
                return File.Exists(fallbackPosterPath) ? fallbackPosterPath : null;
            }
            catch
            {
                return null;
            }
        }

        BitmapImage TryLoadCachedVisualImmediate(string sourcePath, int decodePixelWidth)
        {
            if (string.IsNullOrWhiteSpace(sourcePath)) return null;
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(sourcePath.Trim());
            }
            catch
            {
                return null;
            }

            if (!File.Exists(fullPath)) return null;
            try
            {
                var normalizedDecodePixelWidth = NormalizeThumbnailDecodeWidth(decodePixelWidth);
                if (IsVideo(fullPath))
                {
                    var posterPath = ExistingVideoPosterPath(fullPath, normalizedDecodePixelWidth);
                    return string.IsNullOrWhiteSpace(posterPath) ? null : LoadFrozenBitmap(posterPath, 0);
                }
                var thumbnailPath = ThumbnailCachePath(fullPath, normalizedDecodePixelWidth);
                return string.IsNullOrWhiteSpace(thumbnailPath) || !File.Exists(thumbnailPath)
                    ? null
                    : LoadFrozenBitmap(thumbnailPath, 0);
            }
            catch (Exception ex)
            {
                Log("TryLoadCachedVisualImmediate: " + fullPath + " — " + ex.Message);
                return null;
            }
        }

        BitmapImage LoadFrozenBitmap(string path, int decodePixelWidth)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path.Trim());
            }
            catch
            {
                return null;
            }

            if (!File.Exists(fullPath)) return null;

            try
            {
                using (var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    var image = new BitmapImage();
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    if (decodePixelWidth > 0) image.DecodePixelWidth = decodePixelWidth;
                    image.StreamSource = stream;
                    image.EndInit();
                    image.Freeze();
                    return image;
                }
            }
            catch (Exception ex)
            {
                Log("LoadFrozenBitmap: " + fullPath + " — " + ex.Message);
                return null;
            }
        }

        static bool ThumbnailCacheWriteMayRetry(Exception ex)
        {
            if (ex == null) return false;
            if (ex is UnauthorizedAccessException) return true;
            var msg = ex.Message ?? string.Empty;
            if (msg.IndexOf("being used by another process", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (msg.IndexOf("cannot access the file", StringComparison.OrdinalIgnoreCase) >= 0
                && msg.IndexOf(".tmp", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (msg.IndexOf("Access to the path is denied", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        void SaveThumbnailCache(BitmapSource source, string destinationPath)
        {
            if (source == null || string.IsNullOrWhiteSpace(destinationPath)) return;
            var directory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            if (File.Exists(destinationPath)) return;

            const int maxAttempts = 4;
            for (var attempt = 0; attempt < maxAttempts; attempt++)
            {
                if (attempt > 0) Thread.Sleep(35 * attempt);
                string tempPath = null;
                try
                {
                    tempPath = Path.Combine(
                        directory ?? string.Empty,
                        Path.GetFileName(destinationPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
                    using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(source));
                        encoder.Save(stream);
                    }
                    if (File.Exists(destinationPath)) return;
                    File.Move(tempPath, destinationPath);
                    tempPath = null;
                    return;
                }
                catch (Exception ex)
                {
                    if (File.Exists(destinationPath)) return;
                    if (!string.IsNullOrWhiteSpace(tempPath) && File.Exists(tempPath))
                    {
                        try { File.Delete(tempPath); }
                        catch { /* best-effort */ }
                        tempPath = null;
                    }
                    if (attempt < maxAttempts - 1 && ThumbnailCacheWriteMayRetry(ex)) continue;
                    Log("SaveThumbnailCache failed for " + destinationPath + ". " + ex.Message);
                    return;
                }
            }
        }

    }
}