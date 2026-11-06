#nullable enable
using System;
using System.Collections.Generic;

namespace PixelVaultNative
{
    // Retain both grouping projections for the current root and folder snapshot.
    // A data change invalidates both, including the grouping that is not visible.
    internal sealed class LibraryBrowserProjectionCache
    {
        long _fingerprint = long.MinValue;
        string? _context;
        readonly Dictionary<string, List<LibraryBrowserFolderView>> _projections = new(StringComparer.OrdinalIgnoreCase);

        public List<LibraryBrowserFolderView> GetOrBuild(
            IReadOnlyList<LibraryFolderInfo>? folders,
            string? groupingMode,
            Func<string?, string> normalizeGroupingMode,
            Func<IReadOnlyList<LibraryFolderInfo>?, string?, List<LibraryBrowserFolderView>> build,
            string? context = null)
        {
            if (normalizeGroupingMode == null) throw new ArgumentNullException(nameof(normalizeGroupingMode));
            if (build == null) throw new ArgumentNullException(nameof(build));
            var mode = string.Equals(normalizeGroupingMode(groupingMode), "console", StringComparison.OrdinalIgnoreCase) ? "console" : "all";
            var fingerprint = LibraryBrowserViewModelMath.ComputeLibraryBrowserFoldersMergeFingerprint(folders);
            if (fingerprint != _fingerprint || !string.Equals(context, _context, StringComparison.OrdinalIgnoreCase))
            {
                Reset();
                _fingerprint = fingerprint;
                _context = context;
            }
            if (_projections.TryGetValue(mode, out var cached)) return cached;
            var result = build(folders, mode);
            _projections[mode] = result;
            return result;
        }

        public void Reset()
        {
            _projections.Clear();
            _fingerprint = long.MinValue;
            _context = null;
        }

        internal bool HasCachedProjection => _projections.Count > 0;
        internal long CachedFingerprint => _fingerprint;
    }
}
