using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PixelVaultNative
{
    /// <summary>
    /// Disk-backed thumbnail paths, frozen bitmap decode, and PNG cache writes — Phase 2 follow-up off <see cref="MainWindow"/>.
    /// </summary>
    internal sealed class LibraryThumbnailPipeline
    {
        readonly string _thumbsRoot;
        readonly Func<string, bool> _isVideo;
        readonly Func<string, int, string> _ensureVideoPoster;
        readonly Action<string> _log;
        readonly Func<string, BitmapImage> _tryGetCachedImage;
        readonly Action<string, BitmapImage> _storeCachedImage;

        public LibraryThumbnailPipeline(
            string thumbsRoot,
            Func<string, bool> isVideo,
            Func<string, int, string> ensureVideoPoster,
            Action<string> log,
            Func<string, BitmapImage> tryGetCachedImage,
            Action<string, BitmapImage> storeCachedImage)
        {
            _thumbsRoot = thumbsRoot ?? throw new ArgumentNullException(nameof(thumbsRoot));
            _isVideo = isVideo ?? throw new ArgumentNullException(nameof(isVideo));
            _ensureVideoPoster = ensureVideoPoster ?? throw new ArgumentNullException(nameof(ensureVideoPoster));
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _tryGetCachedImage = tryGetCachedImage ?? throw new ArgumentNullException(nameof(tryGetCachedImage));
            _storeCachedImage = storeCachedImage ?? throw new ArgumentNullException(nameof(storeCachedImage));
        }

        public static int NormalizeDecodePixelWidth(int decodePixelWidth)
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

        public BitmapImage LoadImageSource(string path, int decodePixelWidth)
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
                var normalizedDecodePixelWidth = NormalizeDecodePixelWidth(decodePixelWidth);
                if (_isVideo(fullPath))
                {
                    var poster = _ensureVideoPoster(fullPath, normalizedDecodePixelWidth);
                    if (!string.IsNullOrWhiteSpace(poster) && File.Exists(poster)) fullPath = poster;
                }
                var info = new FileInfo(fullPath);
                var cacheKey = fullPath + "|" + info.LastWriteTimeUtc.Ticks + "|" + info.Length + "|" + normalizedDecodePixelWidth;
                var cached = _tryGetCachedImage(cacheKey);
                if (cached != null) return cached;

                BitmapImage image = null;
                var thumbnailPath = _isVideo(sourcePath) ? null : ThumbnailCachePath(fullPath, normalizedDecodePixelWidth);
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
                _storeCachedImage(cacheKey, image);
                return image;
            }
            catch
            {
                return null;
            }
        }

        public BitmapImage TryLoadCachedVisualImmediate(string sourcePath, int decodePixelWidth)
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
                var normalizedDecodePixelWidth = NormalizeDecodePixelWidth(decodePixelWidth);
                if (_isVideo(fullPath))
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
                _log("TryLoadCachedVisualImmediate: " + fullPath + " — " + ex.Message);
                return null;
            }
        }

        public BitmapImage LoadFrozenBitmap(string path, int decodePixelWidth)
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
                _log("LoadFrozenBitmap: " + fullPath + " — " + ex.Message);
                return null;
            }
        }

        string ThumbnailCachePath(string sourcePath, int decodePixelWidth)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath)) return null;
            var normalizedDecodePixelWidth = NormalizeDecodePixelWidth(decodePixelWidth);
            if (normalizedDecodePixelWidth <= 0 || normalizedDecodePixelWidth > 1600) return null;
            try
            {
                var info = new FileInfo(sourcePath);
                var key = sourcePath + "|" + info.LastWriteTimeUtc.Ticks + "|" + info.Length + "|" + normalizedDecodePixelWidth;
                using (var md5 = MD5.Create())
                {
                    var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(key));
                    var name = BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
                    return Path.Combine(_thumbsRoot, name + ".png");
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
                var normalizedDecodePixelWidth = NormalizeDecodePixelWidth(decodePixelWidth);
                var width = Math.Max(320, normalizedDecodePixelWidth > 0 ? normalizedDecodePixelWidth : 720);
                var keySource = videoPath + "|" + info.LastWriteTimeUtc.Ticks + "|" + info.Length + "|" + width;
                string hash;
                using (var md5 = MD5.Create())
                {
                    hash = BitConverter.ToString(md5.ComputeHash(Encoding.UTF8.GetBytes(keySource))).Replace("-", string.Empty).ToLowerInvariant();
                }
                var framePosterPath = Path.Combine(_thumbsRoot, "video-" + hash + "-frame.png");
                if (File.Exists(framePosterPath)) return framePosterPath;
                var fallbackPosterPath = Path.Combine(_thumbsRoot, "video-" + hash + "-fallback.png");
                return File.Exists(fallbackPosterPath) ? fallbackPosterPath : null;
            }
            catch
            {
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
                        catch { }
                        tempPath = null;
                    }
                    if (attempt < maxAttempts - 1 && ThumbnailCacheWriteMayRetry(ex)) continue;
                    _log("SaveThumbnailCache failed for " + destinationPath + ". " + ex.Message);
                    return;
                }
            }
        }
    }
}
