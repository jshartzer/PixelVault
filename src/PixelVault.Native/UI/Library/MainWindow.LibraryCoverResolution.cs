using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PixelVaultNative
{
    // PV-PLN-UI-001 Step 8 Pass B: orchestration bodies moved to
    // Services/Covers/LibraryCoverResolutionService.cs behind ILibraryCoverResolution.
    // This partial keeps only the thin forwarders so every existing caller (other MainWindow
    // partials, ILibrarySession.RefreshLibraryCoversAsync, ImportServiceDependencies, etc.)
    // keeps the same MainWindow-level signatures. Dead helpers (CustomCoverKey,
    // EnrichLibraryFoldersWithSteamAppIdsAsync, the three SteamGridDB-JSON parsers) were
    // dropped rather than carried over — CoverService already owns the live JSON parsing and
    // the others had zero call sites.
    public sealed partial class MainWindow
    {
        string GetGameNameFromFileName(string fileNameOrPath)
        {
            return libraryCoverResolutionService.GetGameNameFromFileName(fileNameOrPath);
        }

        string GetSafeGameFolderName(string name)
        {
            return libraryCoverResolutionService.GetSafeGameFolderName(name);
        }

        Task<string> ResolveBestLibraryFolderSteamAppIdAsync(string root, LibraryFolderInfo folder, bool allowLookup = true, CancellationToken cancellationToken = default(CancellationToken))
        {
            return libraryCoverResolutionService.ResolveBestLibraryFolderSteamAppIdAsync(root, folder, allowLookup, cancellationToken);
        }

        Task<string> ResolveBestLibraryFolderSteamGridDbIdAsync(string root, LibraryFolderInfo folder, CancellationToken cancellationToken = default(CancellationToken))
        {
            return libraryCoverResolutionService.ResolveBestLibraryFolderSteamGridDbIdAsync(root, folder, cancellationToken);
        }

        Task<string> ForceRefreshLibraryArtAsync(LibraryFolderInfo folder, CancellationToken cancellationToken = default(CancellationToken))
        {
            return libraryCoverResolutionService.ForceRefreshLibraryArtAsync(folder, cancellationToken);
        }

        Task<(int resolvedIds, int coversReady)> RefreshLibraryCoversAsync(string root, List<LibraryFolderInfo> libraryFolders, List<LibraryFolderInfo> requestedFolders, Action<int, int, string> progress, CancellationToken cancellationToken, bool forceRefreshExistingCovers, bool rebuildFullCacheAfterRefresh)
        {
            return libraryCoverResolutionService.RefreshLibraryCoversAsync(root, libraryFolders, requestedFolders, progress, cancellationToken, forceRefreshExistingCovers, rebuildFullCacheAfterRefresh);
        }

        /// <summary>Custom cover, on-disk cache entry, or folder preview path — no network (Library tiles / banner when downloads are off).</summary>
        internal string GetLibraryArtPathForDisplayOnly(LibraryFolderInfo folder)
        {
            return libraryCoverResolutionService.GetLibraryArtPathForDisplayOnly(folder);
        }

        /// <summary>Resolve a library folder cover path. When <paramref name="allowDownload"/> is false, the completed task mirrors <see cref="GetLibraryArtPathForDisplayOnly"/> (no network). When true, may download via Steam / SteamGridDB.</summary>
        Task<string> ResolveLibraryArtAsync(LibraryFolderInfo folder, bool allowDownload, CancellationToken cancellationToken = default(CancellationToken))
        {
            return libraryCoverResolutionService.ResolveLibraryArtAsync(folder, allowDownload, cancellationToken);
        }

        /// <summary>Photo-workspace banner: custom or cached wide art only — prefers SteamGridDB Heroes (same asset class as https://www.steamgriddb.com/hero/…), not library cover.</summary>
        internal string GetLibraryHeroBannerPathForDisplayOnly(LibraryFolderInfo folder)
        {
            return libraryCoverResolutionService.GetLibraryHeroBannerPathForDisplayOnly(folder);
        }

        /// <summary>Photo-workspace logo slot: custom logo or auto-cached SteamGridDB logo/icon; falls back to title text when neither asset exists.</summary>
        internal string GetLibraryHeroLogoPathForDisplayOnly(LibraryFolderInfo folder)
        {
            return libraryCoverResolutionService.GetLibraryHeroLogoPathForDisplayOnly(folder);
        }

        /// <summary>Resolves banner art: custom → <b>SteamGridDB Heroes</b> → Valve library_hero / store header fallback.</summary>
        Task<string> ResolveLibraryHeroBannerWithDownloadAsync(LibraryFolderInfo folder, CancellationToken cancellationToken = default(CancellationToken))
        {
            return libraryCoverResolutionService.ResolveLibraryHeroBannerWithDownloadAsync(folder, cancellationToken);
        }

        /// <summary>Resolves photo-workspace logo art: custom logo or cached SteamGridDB logo/icon, else network download; falls back to title text when unavailable.</summary>
        Task<string> ResolveLibraryHeroLogoWithDownloadAsync(LibraryFolderInfo folder, CancellationToken cancellationToken = default(CancellationToken))
        {
            return libraryCoverResolutionService.ResolveLibraryHeroLogoWithDownloadAsync(folder, cancellationToken);
        }

        // ICoverService path forwarders retained so other partials
        // (LibraryBrowserPhotoHero, LibraryBrowserRender.DetailPane, LibraryBrowserOrchestrator.FolderTile,
        // LibraryBrowserShellBridge) keep the same MainWindow-level call sites.
        string CustomCoverPath(LibraryFolderInfo folder) => coverService.CustomCoverPath(folder);
        void SaveCustomCover(LibraryFolderInfo folder, string sourcePath) => coverService.SaveCustomCover(folder, sourcePath);
        void ClearCustomCover(LibraryFolderInfo folder) => coverService.ClearCustomCover(folder);
        string CustomHeroPath(LibraryFolderInfo folder) => coverService.CustomHeroPath(folder);
        void SaveCustomHero(LibraryFolderInfo folder, string sourcePath) => coverService.SaveCustomHero(folder, sourcePath);
        void ClearCustomHero(LibraryFolderInfo folder) => coverService.ClearCustomHero(folder);
        string CustomLogoPath(LibraryFolderInfo folder) => coverService.CustomLogoPath(folder);
        void SaveCustomLogo(LibraryFolderInfo folder, string sourcePath) => coverService.SaveCustomLogo(folder, sourcePath);
        void ClearCustomLogo(LibraryFolderInfo folder) => coverService.ClearCustomLogo(folder);
    }
}
