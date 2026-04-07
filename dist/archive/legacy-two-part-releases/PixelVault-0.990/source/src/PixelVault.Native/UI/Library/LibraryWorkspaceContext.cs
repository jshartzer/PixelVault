using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Threading;

namespace PixelVaultNative
{
    /// <summary>
    /// Library workspace facade: <see cref="LibraryRoot"/>, folder listing cache, and embedded file-tag cache.
    /// Folder image listings and file-tag maps use per-cache locks so background library work and UI paths can coordinate safely.
    /// </summary>
    internal sealed class LibraryWorkspaceContext
    {
        readonly MainWindow _host;
        readonly Dictionary<string, List<string>> _folderImageListingByPath = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, long> _folderImageListingStampByPath = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        readonly object _folderListingSync = new object();
        readonly Dictionary<string, string[]> _fileTagByPath = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, long> _fileTagStampByPath = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        readonly object _fileTagCacheSync = new object();

        internal LibraryWorkspaceContext(MainWindow host)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
        }

        internal string LibraryRoot => _host.LibraryWorkspaceRoot;

        internal Dispatcher UiDispatcher => _host.Dispatcher;

        internal void RemoveFolderImageListings(IEnumerable<string> folderPaths)
        {
            var normalizedPaths = new HashSet<string>(
                (folderPaths ?? Enumerable.Empty<string>())
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
            lock (_folderListingSync)
            {
                foreach (var folderPath in normalizedPaths)
                {
                    _folderImageListingByPath.Remove(folderPath);
                    _folderImageListingStampByPath.Remove(folderPath);
                }
            }
        }

        internal void ClearFolderImageListings()
        {
            lock (_folderListingSync)
            {
                _folderImageListingByPath.Clear();
                _folderImageListingStampByPath.Clear();
            }
        }

        internal List<string> GetCachedFolderImages(string folderPath)
        {
            lock (_folderListingSync)
            {
                var stamp = Directory.GetLastWriteTimeUtc(folderPath).Ticks;
                List<string> cached;
                long cachedStamp;
                if (_folderImageListingByPath.TryGetValue(folderPath, out cached) && _folderImageListingStampByPath.TryGetValue(folderPath, out cachedStamp) && cachedStamp == stamp)
                {
                    return cached;
                }
                var fresh = Directory.EnumerateFiles(folderPath, "*.*", SearchOption.TopDirectoryOnly).Where(MainWindow.IsImage).ToList();
                _folderImageListingByPath[folderPath] = fresh;
                _folderImageListingStampByPath[folderPath] = stamp;
                return fresh;
            }
        }

        internal bool TryGetCachedFileTags(string file, long expectedStamp, out string[] tags)
        {
            tags = null;
            if (string.IsNullOrWhiteSpace(file)) return false;
            lock (_fileTagCacheSync)
            {
                string[] cachedTags;
                long cachedStamp;
                if (!_fileTagByPath.TryGetValue(file, out cachedTags) || !_fileTagStampByPath.TryGetValue(file, out cachedStamp) || cachedStamp != expectedStamp) return false;
                tags = cachedTags ?? new string[0];
                return true;
            }
        }

        internal void SetCachedFileTags(string file, IEnumerable<string> tags, long stamp)
        {
            if (string.IsNullOrWhiteSpace(file)) return;
            var normalizedTags = (tags ?? Enumerable.Empty<string>())
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Select(MainWindow.CleanTag)
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            lock (_fileTagCacheSync)
            {
                _fileTagByPath[file] = normalizedTags;
                _fileTagStampByPath[file] = stamp;
            }
        }

        internal void RemoveCachedFileTagEntries(IEnumerable<string> files)
        {
            var normalizedPaths = new HashSet<string>(
                (files ?? Enumerable.Empty<string>())
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
            lock (_fileTagCacheSync)
            {
                foreach (var file in normalizedPaths)
                {
                    _fileTagByPath.Remove(file);
                    _fileTagStampByPath.Remove(file);
                }
            }
        }

        internal void ClearFileTagCache()
        {
            lock (_fileTagCacheSync)
            {
                _fileTagByPath.Clear();
                _fileTagStampByPath.Clear();
            }
        }
    }
}
