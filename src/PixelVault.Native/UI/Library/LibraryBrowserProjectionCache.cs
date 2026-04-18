#nullable enable
using System;
using System.Collections.Generic;

namespace PixelVaultNative
{
    /// <summary>
    /// PV-PLN-UI-001 Step 13 Pass C: owns the merged "All" library browser projection that
    /// previously lived as two private fields (<c>_libraryBrowserAllMergeProjection</c>,
    /// <c>_libraryBrowserAllMergeProjectionFingerprint</c>) on the
    /// <c>MainWindow.LibraryBrowserViewModel</c> partial. This is the cache that lets us skip
    /// rebuilding merged folder rows on every render when the source folder list did not
    /// change.
    ///
    /// Behavior contract:
    /// - "console" grouping never caches (every call clears + rebuilds, matching prior behavior).
    /// - Other groupings ("all", timeline-projected "all") fingerprint the folder list with
    ///   <see cref="LibraryBrowserViewModelMath.ComputeLibraryBrowserFoldersMergeFingerprint"/>
    ///   and skip rebuild when the fingerprint matches the cached one.
    ///
    /// iOS alignment: contract-shaped. Inputs are plain values + delegates, no
    /// <c>MainWindow</c> references. Future <c>ILibrarySession</c> / backend projections can
    /// wire their own <c>build</c> + <c>normalizeGroupingMode</c> implementations.
    /// </summary>
    internal sealed class LibraryBrowserProjectionCache
    {
        long _fingerprint = long.MinValue;
        List<LibraryBrowserFolderView>? _projection;

        /// <summary>Returns cached merged rows for "All" grouping when folder data unchanged; console mode is always rebuilt and clears the cache.</summary>
        public List<LibraryBrowserFolderView> GetOrBuild(
            IReadOnlyList<LibraryFolderInfo>? folders,
            string? groupingMode,
            Func<string?, string> normalizeGroupingMode,
            Func<IReadOnlyList<LibraryFolderInfo>?, string?, List<LibraryBrowserFolderView>> build)
        {
            if (normalizeGroupingMode == null) throw new ArgumentNullException(nameof(normalizeGroupingMode));
            if (build == null) throw new ArgumentNullException(nameof(build));

            var normalized = normalizeGroupingMode(groupingMode);
            if (string.Equals(normalized, "console", StringComparison.OrdinalIgnoreCase))
            {
                Reset();
                return build(folders, groupingMode);
            }

            var fp = LibraryBrowserViewModelMath.ComputeLibraryBrowserFoldersMergeFingerprint(folders);
            if (_projection != null && fp == _fingerprint)
            {
                return _projection;
            }

            var built = build(folders, groupingMode);
            _projection = built;
            _fingerprint = fp;
            return built;
        }

        /// <summary>Drops the cached projection. Called when the cache is known to be stale (e.g. console grouping toggled).</summary>
        public void Reset()
        {
            _projection = null;
            _fingerprint = long.MinValue;
        }

        /// <summary>For tests / diagnostics: true while a cached projection is held.</summary>
        internal bool HasCachedProjection => _projection != null;

        /// <summary>For tests / diagnostics: the fingerprint of the cached projection (or <see cref="long.MinValue"/> when empty).</summary>
        internal long CachedFingerprint => _fingerprint;
    }
}
