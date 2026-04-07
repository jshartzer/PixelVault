using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;

namespace PixelVaultNative
{
    /// <summary>
    /// Thread-safe LRU for decoded <see cref="BitmapImage"/> entries (path + stamp + decode width key). Owned off <see cref="MainWindow"/> for Phase 2 shrink.
    /// </summary>
    internal sealed class LibraryBitmapLruCache
    {
        readonly int _maxEntries;
        readonly Dictionary<string, BitmapImage> _cache = new Dictionary<string, BitmapImage>(StringComparer.OrdinalIgnoreCase);
        readonly LinkedList<string> _order = new LinkedList<string>();
        readonly Dictionary<string, LinkedListNode<string>> _orderNodes = new Dictionary<string, LinkedListNode<string>>(StringComparer.OrdinalIgnoreCase);
        readonly object _sync = new object();

        public LibraryBitmapLruCache(int maxEntries)
        {
            if (maxEntries < 1) throw new ArgumentOutOfRangeException(nameof(maxEntries));
            _maxEntries = maxEntries;
        }

        public void Clear()
        {
            lock (_sync)
            {
                _cache.Clear();
                _order.Clear();
                _orderNodes.Clear();
            }
        }

        public void RemoveCachedImageEntries(IEnumerable<string> sourcePaths)
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
            lock (_sync)
            {
                var keysToRemove = _cache.Keys
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
                    _cache.Remove(cacheKey);
                    LinkedListNode<string> node;
                    if (_orderNodes.TryGetValue(cacheKey, out node) && node != null)
                    {
                        _order.Remove(node);
                    }
                    _orderNodes.Remove(cacheKey);
                }
            }
        }

        public BitmapImage TryGet(string cacheKey)
        {
            if (string.IsNullOrWhiteSpace(cacheKey)) return null;
            lock (_sync)
            {
                BitmapImage cached;
                if (!_cache.TryGetValue(cacheKey, out cached)) return null;
                LinkedListNode<string> node;
                if (_orderNodes.TryGetValue(cacheKey, out node) && node != null)
                {
                    _order.Remove(node);
                    _order.AddLast(node);
                }
                return cached;
            }
        }

        public void Store(string cacheKey, BitmapImage image)
        {
            if (string.IsNullOrWhiteSpace(cacheKey) || image == null) return;
            lock (_sync)
            {
                LinkedListNode<string> existingNode;
                if (_cache.ContainsKey(cacheKey))
                {
                    _cache[cacheKey] = image;
                    if (_orderNodes.TryGetValue(cacheKey, out existingNode) && existingNode != null)
                    {
                        _order.Remove(existingNode);
                        _order.AddLast(existingNode);
                    }
                    return;
                }
                _cache[cacheKey] = image;
                var node = new LinkedListNode<string>(cacheKey);
                _order.AddLast(node);
                _orderNodes[cacheKey] = node;
                while (_cache.Count > _maxEntries && _order.Count > 0)
                {
                    var firstNode = _order.First;
                    if (firstNode == null) break;
                    _order.RemoveFirst();
                    var oldest = firstNode.Value;
                    if (string.IsNullOrWhiteSpace(oldest)) continue;
                    _cache.Remove(oldest);
                    _orderNodes.Remove(oldest);
                }
            }
        }
    }
}
