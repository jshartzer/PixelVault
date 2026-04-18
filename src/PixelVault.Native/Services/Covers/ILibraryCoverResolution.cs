#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PixelVaultNative
{
    /// <summary>
    /// Orchestrates Steam / SteamGridDB / Steam-store cover + hero + logo resolution for library folders.
    /// Sits above <see cref="ICoverService"/>: <see cref="ICoverService"/> owns raw HTTP and on-disk path
    /// helpers; this service owns the per-folder decisions (ID resolution, cache promotion, download
    /// fallback order, progress reporting). PV-PLN-UI-001 Step 8 Pass B.
    /// </summary>
    internal interface ILibraryCoverResolution
    {
        /// <summary>Resolves a game title for sorting/organize. Accepts a bare stem or a full path/filename with extension.</summary>
        string GetGameNameFromFileName(string? fileNameOrPath);

        /// <summary>Windows-safe folder name derived from <paramref name="name"/>; collapses whitespace, strips reserved chars, title-cases all-caps.</summary>
        string GetSafeGameFolderName(string? name);

        /// <summary>Resolves the best Steam App ID for <paramref name="folder"/> (cached → saved row → convention → Steam search). Updates <paramref name="folder"/> and the saved game index on success.</summary>
        Task<string> ResolveBestLibraryFolderSteamAppIdAsync(string root, LibraryFolderInfo folder, bool allowLookup = true, CancellationToken cancellationToken = default);

        /// <summary>Resolves the best SteamGridDB ID for <paramref name="folder"/> (cached → saved row → by-Steam-AppId → by-name). Updates the saved game index on success.</summary>
        Task<string> ResolveBestLibraryFolderSteamGridDbIdAsync(string root, LibraryFolderInfo folder, CancellationToken cancellationToken = default);

        /// <summary>Force-refresh the library cover for <paramref name="folder"/>: re-download via Steam / SteamGridDB and restore the previous cached file on failure. Preserves custom covers.</summary>
        Task<string?> ForceRefreshLibraryArtAsync(LibraryFolderInfo folder, CancellationToken cancellationToken = default);

        /// <summary>Outer orchestration for &quot;refresh covers for N folders&quot; — resolves IDs, downloads missing covers, updates the folder cache, and reports progress via <paramref name="progress"/>.</summary>
        Task<(int resolvedIds, int coversReady)> RefreshLibraryCoversAsync(
            string root,
            List<LibraryFolderInfo> libraryFolders,
            List<LibraryFolderInfo> requestedFolders,
            Action<int, int, string>? progress,
            CancellationToken cancellationToken,
            bool forceRefreshExistingCovers,
            bool rebuildFullCacheAfterRefresh);

        /// <summary>Custom cover, on-disk cache entry, or folder preview path — no network. Library tiles / banner when downloads are disabled.</summary>
        string? GetLibraryArtPathForDisplayOnly(LibraryFolderInfo? folder);

        /// <summary>
        /// Resolve a library folder cover path. When <paramref name="allowDownload"/> is <c>false</c>, returns a completed task
        /// (<see cref="GetLibraryArtPathForDisplayOnly"/> only — no network). When <c>true</c>, may download via Steam / SteamGridDB.
        /// </summary>
        Task<string?> ResolveLibraryArtAsync(LibraryFolderInfo? folder, bool allowDownload, CancellationToken cancellationToken = default);

        /// <summary>Photo-workspace banner: custom or cached wide art only — prefers SteamGridDB Heroes, not the library cover.</summary>
        string? GetLibraryHeroBannerPathForDisplayOnly(LibraryFolderInfo? folder);

        /// <summary>Photo-workspace logo slot: custom logo or cached SteamGridDB logo/icon; falls back to title text when neither exists.</summary>
        string? GetLibraryHeroLogoPathForDisplayOnly(LibraryFolderInfo? folder);

        /// <summary>Resolves banner art: custom → SteamGridDB Heroes → Valve <c>library_hero</c> / store-header fallback.</summary>
        Task<string?> ResolveLibraryHeroBannerWithDownloadAsync(LibraryFolderInfo? folder, CancellationToken cancellationToken = default);

        /// <summary>Resolves photo-workspace logo art: custom logo or cached SteamGridDB logo/icon, else network download.</summary>
        Task<string?> ResolveLibraryHeroLogoWithDownloadAsync(LibraryFolderInfo? folder, CancellationToken cancellationToken = default);
    }

    /// <summary>Wiring for <see cref="LibraryCoverResolutionService"/>. Mirrors <see cref="CoverServiceDependencies"/>: interfaces for the always-available services, delegates for anything <see cref="MainWindow"/> still owns.</summary>
    internal sealed class LibraryCoverResolutionDependencies
    {
        public ICoverService CoverService = default!;
        public IFilenameParserService FilenameParser = default!;
        public IFileSystemService FileSystem = default!;

        /// <summary>Reads <c>MainWindow.libraryRoot</c> lazily so the service tracks root changes without a re-ctor.</summary>
        public Func<string> GetLibraryRoot = default!;

        public Func<bool> HasSteamGridDbApiToken = default!;

        public Func<string?, string> NormalizeTitle = default!;
        public Func<string?, string> NormalizeConsoleLabel = default!;
        public Func<string?, string> NormalizeGameId = default!;

        public Func<LibraryFolderInfo, string> BuildLibraryFolderMasterKey = default!;

        public Func<string, string> BuildLibraryFolderInventoryStamp = default!;
        public Func<string, string, List<LibraryFolderInfo>?> LoadLibraryFolderCache = default!;
        public Action<string, string, List<LibraryFolderInfo>> SaveLibraryFolderCache = default!;
        public Action<string> RefreshCachedLibraryFoldersFromGameIndex = default!;

        public Func<string, List<GameIndexEditorRow>> GetSavedGameIndexRowsForRoot = default!;
        public Func<IEnumerable<GameIndexEditorRow>, LibraryFolderInfo, GameIndexEditorRow?> FindSavedGameIndexRow = default!;
        public Action<string, LibraryFolderInfo> UpsertSavedGameIndexRow = default!;

        public Func<string?, IEnumerable<string>, string> ResolveLibraryFolderSteamAppId = default!;
        public Func<string, string, FilenameParseResult> ParseFilename = default!;

        public Action<string> Log = default!;
        public Action<IEnumerable<string>> RemoveCachedImageEntries = default!;
    }
}
