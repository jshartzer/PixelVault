#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PixelVaultNative
{
    /// <summary>
    /// PV-PLN-UI-001 Step 8 Pass B: owns library cover / hero / logo resolution orchestration.
    /// Bodies are ported verbatim from <c>MainWindow.LibraryCoverResolution.cs</c>; behavior must stay
    /// byte-identical. Low-level HTTP + on-disk path helpers stay on <see cref="ICoverService"/>.
    /// </summary>
    internal sealed class LibraryCoverResolutionService : ILibraryCoverResolution
    {
        readonly LibraryCoverResolutionDependencies _d;

        public LibraryCoverResolutionService(LibraryCoverResolutionDependencies dependencies)
        {
            _d = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
            if (_d.CoverService == null) throw new ArgumentException("CoverService is required.", nameof(dependencies));
            if (_d.FilenameParser == null) throw new ArgumentException("FilenameParser is required.", nameof(dependencies));
            if (_d.FileSystem == null) throw new ArgumentException("FileSystem is required.", nameof(dependencies));
            if (_d.GetLibraryRoot == null) throw new ArgumentException("GetLibraryRoot is required.", nameof(dependencies));
            if (_d.HasSteamGridDbApiToken == null) throw new ArgumentException("HasSteamGridDbApiToken is required.", nameof(dependencies));
            if (_d.NormalizeTitle == null) throw new ArgumentException("NormalizeTitle is required.", nameof(dependencies));
            if (_d.NormalizeConsoleLabel == null) throw new ArgumentException("NormalizeConsoleLabel is required.", nameof(dependencies));
            if (_d.NormalizeGameId == null) throw new ArgumentException("NormalizeGameId is required.", nameof(dependencies));
            if (_d.BuildLibraryFolderMasterKey == null) throw new ArgumentException("BuildLibraryFolderMasterKey is required.", nameof(dependencies));
            if (_d.BuildLibraryFolderInventoryStamp == null) throw new ArgumentException("BuildLibraryFolderInventoryStamp is required.", nameof(dependencies));
            if (_d.LoadLibraryFolderCache == null) throw new ArgumentException("LoadLibraryFolderCache is required.", nameof(dependencies));
            if (_d.SaveLibraryFolderCache == null) throw new ArgumentException("SaveLibraryFolderCache is required.", nameof(dependencies));
            if (_d.RefreshCachedLibraryFoldersFromGameIndex == null) throw new ArgumentException("RefreshCachedLibraryFoldersFromGameIndex is required.", nameof(dependencies));
            if (_d.GetSavedGameIndexRowsForRoot == null) throw new ArgumentException("GetSavedGameIndexRowsForRoot is required.", nameof(dependencies));
            if (_d.FindSavedGameIndexRow == null) throw new ArgumentException("FindSavedGameIndexRow is required.", nameof(dependencies));
            if (_d.UpsertSavedGameIndexRow == null) throw new ArgumentException("UpsertSavedGameIndexRow is required.", nameof(dependencies));
            if (_d.ResolveLibraryFolderSteamAppId == null) throw new ArgumentException("ResolveLibraryFolderSteamAppId is required.", nameof(dependencies));
            if (_d.ParseFilename == null) throw new ArgumentException("ParseFilename is required.", nameof(dependencies));
            if (_d.Log == null) throw new ArgumentException("Log is required.", nameof(dependencies));
            if (_d.RemoveCachedImageEntries == null) throw new ArgumentException("RemoveCachedImageEntries is required.", nameof(dependencies));
        }

        string LibraryRoot => _d.GetLibraryRoot() ?? string.Empty;

        // region: name / folder helpers ------------------------------------------------

        public string GetGameNameFromFileName(string? fileNameOrPath)
        {
            if (string.IsNullOrWhiteSpace(fileNameOrPath)) return string.Empty;
            var fileName = fileNameOrPath!;
            if (fileName.IndexOf('\\') >= 0 || fileName.IndexOf('/') >= 0)
                fileName = Path.GetFileName(fileName.Trim());
            if (string.IsNullOrWhiteSpace(fileName)) return string.Empty;
            var parseInput = string.IsNullOrWhiteSpace(Path.GetExtension(fileName))
                ? fileName + ".png"
                : fileName;
            var parsed = _d.FilenameParser.Parse(parseInput, LibraryRoot);
            if (!string.IsNullOrWhiteSpace(parsed.GameTitleHint)) return parsed.GameTitleHint.Trim();
            return _d.FilenameParser.GetGameTitleHint(Path.GetFileNameWithoutExtension(parseInput), LibraryRoot) ?? string.Empty;
        }

        public string GetSafeGameFolderName(string? name)
        {
            var n = NormalizeGameFolderCapitalization(name ?? string.Empty);
            n = Regex.Replace(n, "[<>:\"/\\\\|?*\\x00-\\x1F]", string.Empty);
            n = n.Trim().TrimEnd('.');
            n = Regex.Replace(n, "\\s{2,}", " ").Trim();
            return string.IsNullOrWhiteSpace(n) ? "Unknown Game" : n;
        }

        static string NormalizeGameFolderCapitalization(string name)
        {
            if (Regex.IsMatch(name ?? string.Empty, "[A-Z]") && !Regex.IsMatch(name ?? string.Empty, "[a-z]"))
            {
                return System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase((name ?? string.Empty).ToLowerInvariant());
            }
            return name ?? string.Empty;
        }

        // region: ID resolution --------------------------------------------------------

        public async Task<string> ResolveBestLibraryFolderSteamAppIdAsync(string root, LibraryFolderInfo folder, bool allowLookup = true, CancellationToken cancellationToken = default)
        {
            if (folder == null || string.IsNullOrWhiteSpace(folder.Name)) return string.Empty;
            if (folder.SuppressSteamAppIdAutoResolve) return string.Empty;
            if (!string.IsNullOrWhiteSpace(folder.SteamAppId)) return folder.SteamAppId;
            cancellationToken.ThrowIfCancellationRequested();
            var saved = _d.FindSavedGameIndexRow(_d.GetSavedGameIndexRowsForRoot(root), folder);
            if (saved != null && saved.SuppressSteamAppIdAutoResolve)
            {
                folder.SteamAppId = string.Empty;
                folder.SuppressSteamAppIdAutoResolve = true;
                return string.Empty;
            }
            if (saved != null && !string.IsNullOrWhiteSpace(saved.SteamAppId))
            {
                folder.SteamAppId = saved.SteamAppId;
                folder.SuppressSteamAppIdAutoResolve = false;
                return folder.SteamAppId;
            }
            if (!ShouldUseSteamStoreLookups(folder)) return string.Empty;
            if (!allowLookup) return folder.SteamAppId ?? string.Empty;
            var appId = _d.ResolveLibraryFolderSteamAppId(folder.PlatformLabel, folder.FilePaths ?? new string[0]);
            if (string.IsNullOrWhiteSpace(appId))
            {
                cancellationToken.ThrowIfCancellationRequested();
                appId = await _d.CoverService.TryResolveSteamAppIdAsync(folder.Name, cancellationToken).ConfigureAwait(false);
            }
            if (!string.IsNullOrWhiteSpace(appId))
            {
                folder.SteamAppId = appId;
                _d.UpsertSavedGameIndexRow(root, folder);
            }
            return folder.SteamAppId ?? string.Empty;
        }

        public async Task<string> ResolveBestLibraryFolderSteamGridDbIdAsync(string root, LibraryFolderInfo folder, CancellationToken cancellationToken = default)
        {
            if (folder == null || string.IsNullOrWhiteSpace(folder.Name) || !_d.HasSteamGridDbApiToken()) return string.Empty;
            if (folder.SuppressSteamGridDbIdAutoResolve) return string.Empty;
            if (!string.IsNullOrWhiteSpace(folder.SteamGridDbId)) return folder.SteamGridDbId;
            cancellationToken.ThrowIfCancellationRequested();
            var saved = _d.FindSavedGameIndexRow(_d.GetSavedGameIndexRowsForRoot(root), folder);
            if (saved != null && saved.SuppressSteamGridDbIdAutoResolve)
            {
                folder.SteamGridDbId = string.Empty;
                folder.SuppressSteamGridDbIdAutoResolve = true;
                return string.Empty;
            }
            if (saved != null && !string.IsNullOrWhiteSpace(saved.SteamGridDbId))
            {
                folder.SteamGridDbId = saved.SteamGridDbId;
                folder.SuppressSteamGridDbIdAutoResolve = false;
                return folder.SteamGridDbId;
            }
            string? steamGridDbId = null;
            if (ShouldUseSteamStoreLookups(folder))
            {
                var appId = await ResolveBestLibraryFolderSteamAppIdAsync(root, folder, true, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                steamGridDbId = !string.IsNullOrWhiteSpace(appId)
                    ? await _d.CoverService.TryResolveSteamGridDbIdBySteamAppIdAsync(appId, cancellationToken).ConfigureAwait(false)
                    : null;
            }
            if (string.IsNullOrWhiteSpace(steamGridDbId))
            {
                cancellationToken.ThrowIfCancellationRequested();
                steamGridDbId = await _d.CoverService.TryResolveSteamGridDbIdByNameAsync(folder.Name, cancellationToken).ConfigureAwait(false);
            }
            if (!string.IsNullOrWhiteSpace(steamGridDbId))
            {
                folder.SteamGridDbId = steamGridDbId;
                _d.UpsertSavedGameIndexRow(root, folder);
            }
            return folder.SteamGridDbId ?? string.Empty;
        }

        async Task<string> ResolveBestLibraryFolderSteamGridDbIdForHeroAssetsAsync(string root, LibraryFolderInfo folder, CancellationToken cancellationToken = default)
        {
            if (folder == null || string.IsNullOrWhiteSpace(folder.Name) || !_d.HasSteamGridDbApiToken()) return string.Empty;
            if (!string.IsNullOrWhiteSpace(folder.SteamGridDbId)) return folder.SteamGridDbId;
            cancellationToken.ThrowIfCancellationRequested();

            var saved = _d.FindSavedGameIndexRow(_d.GetSavedGameIndexRowsForRoot(root), folder);
            if (saved != null && !string.IsNullOrWhiteSpace(saved.SteamGridDbId))
            {
                folder.SteamGridDbId = saved.SteamGridDbId;
                folder.SuppressSteamGridDbIdAutoResolve = false;
                return folder.SteamGridDbId;
            }

            string? steamGridDbId = null;
            if (ShouldUseSteamStoreLookups(folder))
            {
                var appId = await ResolveBestLibraryFolderSteamAppIdAsync(root, folder, true, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                steamGridDbId = !string.IsNullOrWhiteSpace(appId)
                    ? await _d.CoverService.TryResolveSteamGridDbIdBySteamAppIdAsync(appId, cancellationToken).ConfigureAwait(false)
                    : null;
            }
            if (string.IsNullOrWhiteSpace(steamGridDbId))
            {
                cancellationToken.ThrowIfCancellationRequested();
                steamGridDbId = await _d.CoverService.TryResolveSteamGridDbIdByNameAsync(folder.Name, cancellationToken).ConfigureAwait(false);
            }
            if (!string.IsNullOrWhiteSpace(steamGridDbId))
            {
                folder.SteamGridDbId = steamGridDbId;
                folder.SuppressSteamGridDbIdAutoResolve = false;
                UpdateCachedLibraryFolderInfo(root, folder);
            }
            return folder.SteamGridDbId ?? string.Empty;
        }

        bool ShouldUseSteamStoreLookups(LibraryFolderInfo? folder)
        {
            var platform = _d.NormalizeConsoleLabel(folder == null ? string.Empty : folder.PlatformLabel);
            if (string.Equals(platform, "Steam", StringComparison.OrdinalIgnoreCase)
                || string.Equals(platform, "PC", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            foreach (var file in (folder == null ? new string[0] : (folder.FilePaths ?? new string[0])).Where(File.Exists).Take(3))
            {
                var parsedPlatform = _d.NormalizeConsoleLabel(_d.ParseFilename(file, LibraryRoot).PlatformLabel);
                if (string.Equals(parsedPlatform, "Steam", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(parsedPlatform, "PC", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        // region: cover resolution orchestration ---------------------------------------

        public async Task<string?> ForceRefreshLibraryArtAsync(LibraryFolderInfo folder, CancellationToken cancellationToken = default)
        {
            if (folder == null) return null;
            var custom = _d.CoverService.CustomCoverPath(folder);
            if (!string.IsNullOrWhiteSpace(custom)) return custom;

            var existingCached = _d.CoverService.CachedCoverPath(folder.Name);
            string? backupPath = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(existingCached) && File.Exists(existingCached))
                {
                    backupPath = existingCached + ".bak-" + Guid.NewGuid().ToString("N");
                    _d.FileSystem.CopyFile(existingCached, backupPath, true);
                }

                var steamDownloaded = await TryDownloadSteamCoverAsync(folder, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(steamDownloaded) && File.Exists(steamDownloaded))
                {
                    if (!string.IsNullOrWhiteSpace(backupPath) && File.Exists(backupPath)) File.Delete(backupPath);
                    return steamDownloaded;
                }

                _d.CoverService.DeleteCachedCover(folder.Name);
                var steamGridDbDownloaded = await TryDownloadSteamGridDbCoverAsync(folder, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(steamGridDbDownloaded) && File.Exists(steamGridDbDownloaded))
                {
                    if (!string.IsNullOrWhiteSpace(backupPath) && File.Exists(backupPath)) File.Delete(backupPath);
                    return steamGridDbDownloaded;
                }

                if (!string.IsNullOrWhiteSpace(backupPath) && File.Exists(backupPath) && !string.IsNullOrWhiteSpace(existingCached))
                {
                    if (File.Exists(existingCached)) File.Delete(existingCached);
                    File.Move(backupPath, existingCached);
                    _d.RemoveCachedImageEntries(new[] { existingCached });
                    return existingCached;
                }
            }
            catch (Exception ex)
            {
                _d.Log("ForceRefreshLibraryArt failed mid-refresh. " + ex.Message);
                if (!string.IsNullOrWhiteSpace(backupPath) && File.Exists(backupPath) && !string.IsNullOrWhiteSpace(existingCached))
                {
                    try
                    {
                        if (File.Exists(existingCached)) File.Delete(existingCached);
                        File.Move(backupPath, existingCached);
                        _d.RemoveCachedImageEntries(new[] { existingCached });
                        return existingCached;
                    }
                    catch (Exception restoreEx)
                    {
                        _d.Log("Custom cover: could not promote backup over cached file. " + restoreEx.Message);
                    }
                }
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(backupPath) && File.Exists(backupPath))
                {
                    try
                    {
                        File.Delete(backupPath);
                    }
                    catch (Exception delEx)
                    {
                        _d.Log("Custom cover: could not delete temp backup. " + delEx.Message);
                    }
                }
            }

            return _d.CoverService.CachedCoverPath(folder.Name);
        }

        public async Task<(int resolvedIds, int coversReady)> RefreshLibraryCoversAsync(
            string root,
            List<LibraryFolderInfo> libraryFolders,
            List<LibraryFolderInfo> requestedFolders,
            Action<int, int, string>? progress,
            CancellationToken cancellationToken,
            bool forceRefreshExistingCovers,
            bool rebuildFullCacheAfterRefresh)
        {
            var resolvedIds = 0;
            var coversReady = 0;
            var allFolders = (libraryFolders ?? requestedFolders ?? new List<LibraryFolderInfo>())
                .Where(folder => folder != null && !string.IsNullOrWhiteSpace(folder.Name))
                .ToList();
            var targetFolders = (requestedFolders ?? libraryFolders ?? new List<LibraryFolderInfo>())
                .Where(folder => folder != null && !string.IsNullOrWhiteSpace(folder.Name))
                .GroupBy(_d.BuildLibraryFolderMasterKey, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(folder => folder.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var totalWork = Math.Max(targetFolders.Count * 2, 1);
            if (targetFolders.Count == 0)
            {
                if (progress != null) progress(0, 0, "No library folders available for cover refresh.");
                return (0, 0);
            }
            var completed = 0;
            for (int i = 0; i < targetFolders.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var folder = targetFolders[i];
                var itemLabel = "Game " + (i + 1) + " of " + targetFolders.Count + " | " + folder.Name;
                var hadAppId = !string.IsNullOrWhiteSpace(folder.SteamAppId);
                var hadSteamGridDbId = !string.IsNullOrWhiteSpace(folder.SteamGridDbId);
                var appId = await ResolveBestLibraryFolderSteamAppIdAsync(root, folder, true, cancellationToken).ConfigureAwait(false);
                var steamGridDbId = await ResolveBestLibraryFolderSteamGridDbIdAsync(root, folder, cancellationToken).ConfigureAwait(false);
                if ((!hadAppId && !string.IsNullOrWhiteSpace(appId)) || (!hadSteamGridDbId && !string.IsNullOrWhiteSpace(steamGridDbId)))
                {
                    resolvedIds++;
                    var matchKey = _d.BuildLibraryFolderMasterKey(folder);
                    foreach (var match in allFolders.Where(entry => entry != null && string.Equals(_d.BuildLibraryFolderMasterKey(entry), matchKey, StringComparison.OrdinalIgnoreCase)))
                    {
                        match.SteamAppId = appId;
                        match.SteamGridDbId = steamGridDbId;
                    }
                }
                completed++;
                var idDetail = !string.IsNullOrWhiteSpace(appId) && !string.IsNullOrWhiteSpace(steamGridDbId)
                    ? "AppID " + appId + " | STID " + steamGridDbId
                    : (!string.IsNullOrWhiteSpace(steamGridDbId)
                        ? "STID " + steamGridDbId
                        : (!string.IsNullOrWhiteSpace(appId) ? "AppID " + appId : "no external ID"));
                if (progress != null) progress(completed, totalWork, itemLabel + " | " + idDetail);
                cancellationToken.ThrowIfCancellationRequested();
                var hasCustomCover = !string.IsNullOrWhiteSpace(_d.CoverService.CustomCoverPath(folder));
                var hadCachedCover = _d.CoverService.CachedCoverPath(folder.Name) != null;
                var coverReady = hasCustomCover || hadCachedCover;
                var coverDetail = coverReady ? "cover already present" : "cover missing";
                if (forceRefreshExistingCovers && hadCachedCover && !hasCustomCover)
                {
                    var refreshedCover = await ForceRefreshLibraryArtAsync(folder, cancellationToken).ConfigureAwait(false);
                    coverReady = !string.IsNullOrWhiteSpace(refreshedCover) && File.Exists(refreshedCover);
                    coverDetail = coverReady ? "cover refreshed" : "cover refresh not available";
                }
                else if (forceRefreshExistingCovers && hasCustomCover)
                {
                    coverDetail = "custom cover preserved";
                }
                else if (!coverReady)
                {
                    await ResolveLibraryArtAsync(folder, true, cancellationToken).ConfigureAwait(false);
                    coverReady = _d.CoverService.HasDedicatedLibraryCover(folder);
                    coverDetail = coverReady ? "cover ready" : "cover not available";
                }
                if (coverReady) coversReady++;
                completed++;
                if (progress != null) progress(completed, totalWork, itemLabel + " | " + coverDetail);
            }
            if (rebuildFullCacheAfterRefresh)
            {
                _d.RefreshCachedLibraryFoldersFromGameIndex(root);
                return (resolvedIds, coversReady);
            }
            var stamp = _d.BuildLibraryFolderInventoryStamp(root);
            var cached = _d.LoadLibraryFolderCache(root, stamp);
            if (cached == null || cached.Count == 0)
            {
                _d.SaveLibraryFolderCache(root, stamp, allFolders);
                return (resolvedIds, coversReady);
            }
            foreach (var updated in allFolders.Where(entry => entry != null))
            {
                var normalizedGameId = _d.NormalizeGameId(updated.GameId);
                var match = !string.IsNullOrWhiteSpace(normalizedGameId)
                    ? cached.FirstOrDefault(entry => string.Equals(_d.NormalizeGameId(entry.GameId), normalizedGameId, StringComparison.OrdinalIgnoreCase))
                    : null;
                if (match == null)
                {
                    var folderKey = _d.BuildLibraryFolderMasterKey(updated);
                    match = cached.FirstOrDefault(entry => string.Equals(_d.BuildLibraryFolderMasterKey(entry), folderKey, StringComparison.OrdinalIgnoreCase));
                }
                if (match == null) continue;
                if (!string.IsNullOrWhiteSpace(updated.SteamAppId)) match.SteamAppId = updated.SteamAppId;
                if (!string.IsNullOrWhiteSpace(updated.SteamGridDbId)) match.SteamGridDbId = updated.SteamGridDbId;
            }
            _d.SaveLibraryFolderCache(root, stamp, cached);
            return (resolvedIds, coversReady);
        }

        bool TryGetCustomOrCachedCoverPath(LibraryFolderInfo? folder, out string? path)
        {
            path = null;
            if (folder == null) return false;
            var custom = _d.CoverService.CustomCoverPath(folder);
            if (!string.IsNullOrWhiteSpace(custom))
            {
                path = custom;
                return true;
            }

            var cached = _d.CoverService.CachedCoverPath(folder.Name);
            if (cached != null)
            {
                path = cached;
                return true;
            }

            return false;
        }

        public string? GetLibraryArtPathForDisplayOnly(LibraryFolderInfo? folder)
        {
            if (TryGetCustomOrCachedCoverPath(folder, out var early)) return early;
            return folder?.PreviewImagePath;
        }

        public Task<string?> ResolveLibraryArtAsync(LibraryFolderInfo? folder, bool allowDownload, CancellationToken cancellationToken = default)
        {
            if (folder == null) return Task.FromResult<string?>(null);
            if (!allowDownload) return Task.FromResult(GetLibraryArtPathForDisplayOnly(folder));
            return ResolveLibraryArtWithDownloadAsync(folder, cancellationToken);
        }

        async Task<string?> ResolveLibraryArtWithDownloadAsync(LibraryFolderInfo folder, CancellationToken cancellationToken)
        {
            if (TryGetCustomOrCachedCoverPath(folder, out var early)) return early;
            var downloaded = await TryDownloadSteamCoverAsync(folder, cancellationToken).ConfigureAwait(false);
            if (downloaded != null) return downloaded;
            var steamGridDbDownloaded = await TryDownloadSteamGridDbCoverAsync(folder, cancellationToken).ConfigureAwait(false);
            if (steamGridDbDownloaded != null) return steamGridDbDownloaded;
            return folder.PreviewImagePath;
        }

        // region: hero / logo resolution -----------------------------------------------

        bool TryGetCustomOrCachedHeroPath(LibraryFolderInfo? folder, out string? path)
        {
            path = null;
            if (folder == null) return false;
            var custom = _d.CoverService.CustomHeroPath(folder);
            if (!string.IsNullOrWhiteSpace(custom))
            {
                path = custom;
                return true;
            }

            var cached = _d.CoverService.CachedHeroPath(folder.Name);
            if (cached != null)
            {
                path = cached;
                return true;
            }

            return false;
        }

        bool TryGetCachedHeroLogoPath(LibraryFolderInfo? folder, out string? path)
        {
            path = null;
            if (folder == null) return false;
            var custom = _d.CoverService.CustomLogoPath(folder);
            if (custom != null)
            {
                path = custom;
                return true;
            }
            var cached = _d.CoverService.CachedLogoPath(folder.Name);
            if (cached != null)
            {
                path = cached;
                return true;
            }

            return false;
        }

        public string? GetLibraryHeroBannerPathForDisplayOnly(LibraryFolderInfo? folder)
        {
            return TryGetCustomOrCachedHeroPath(folder, out var p) ? p : null;
        }

        public string? GetLibraryHeroLogoPathForDisplayOnly(LibraryFolderInfo? folder)
        {
            return TryGetCachedHeroLogoPath(folder, out var p) ? p : null;
        }

        public async Task<string?> ResolveLibraryHeroBannerWithDownloadAsync(LibraryFolderInfo? folder, CancellationToken cancellationToken = default)
        {
            if (folder == null) return null;
            cancellationToken.ThrowIfCancellationRequested();
            if (TryGetCustomOrCachedHeroPath(folder, out var early)) return early;
            var steamGridDbHero = await TryDownloadSteamGridDbHeroAsync(folder, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(steamGridDbHero) && File.Exists(steamGridDbHero)) return steamGridDbHero;
            cancellationToken.ThrowIfCancellationRequested();
            var steamFallback = await TryDownloadSteamStoreHeaderHeroAsync(folder, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(steamFallback) && File.Exists(steamFallback)) return steamFallback;
            return null;
        }

        public async Task<string?> ResolveLibraryHeroLogoWithDownloadAsync(LibraryFolderInfo? folder, CancellationToken cancellationToken = default)
        {
            if (folder == null) return null;
            cancellationToken.ThrowIfCancellationRequested();
            if (TryGetCachedHeroLogoPath(folder, out var early)) return early;
            var steamGridDbLogo = await TryDownloadSteamGridDbLogoAsync(folder, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(steamGridDbLogo) && File.Exists(steamGridDbLogo)) return steamGridDbLogo;
            return null;
        }

        async Task<string?> TryDownloadSteamGridDbHeroAsync(LibraryFolderInfo folder, CancellationToken cancellationToken = default)
        {
            if (folder == null || !_d.HasSteamGridDbApiToken()) return null;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var steamGridDbId = await ResolveBestLibraryFolderSteamGridDbIdForHeroAssetsAsync(LibraryRoot, folder, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                var downloaded = await _d.CoverService.TryDownloadSteamGridDbHeroAsync(folder.Name, steamGridDbId, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(downloaded) && File.Exists(downloaded)) return downloaded;
                var fallbackId = await TryResolveSteamGridDbNameFallbackIdAsync(folder.Name, steamGridDbId, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(fallbackId))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    downloaded = await _d.CoverService.TryDownloadSteamGridDbHeroAsync(folder.Name, fallbackId, cancellationToken).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(downloaded) && File.Exists(downloaded))
                    {
                        folder.SteamGridDbId = fallbackId;
                        folder.SuppressSteamGridDbIdAutoResolve = false;
                        UpdateCachedLibraryFolderInfo(LibraryRoot, folder);
                        return downloaded;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _d.Log("SteamGridDB hero download failed for " + (folder.Name ?? "unknown title") + ". " + ex.Message);
            }
            return null;
        }

        async Task<string?> TryDownloadSteamGridDbLogoAsync(LibraryFolderInfo folder, CancellationToken cancellationToken = default)
        {
            if (folder == null || !_d.HasSteamGridDbApiToken()) return null;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var steamGridDbId = await ResolveBestLibraryFolderSteamGridDbIdForHeroAssetsAsync(LibraryRoot, folder, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                var downloaded = await _d.CoverService.TryDownloadSteamGridDbLogoAsync(folder.Name, steamGridDbId, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(downloaded) && File.Exists(downloaded)) return downloaded;
                var fallbackId = await TryResolveSteamGridDbNameFallbackIdAsync(folder.Name, steamGridDbId, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(fallbackId))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    downloaded = await _d.CoverService.TryDownloadSteamGridDbLogoAsync(folder.Name, fallbackId, cancellationToken).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(downloaded) && File.Exists(downloaded))
                    {
                        folder.SteamGridDbId = fallbackId;
                        folder.SuppressSteamGridDbIdAutoResolve = false;
                        UpdateCachedLibraryFolderInfo(LibraryRoot, folder);
                        return downloaded;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _d.Log("SteamGridDB logo download failed for " + (folder.Name ?? "unknown title") + ". " + ex.Message);
            }
            return null;
        }

        async Task<string?> TryResolveSteamGridDbNameFallbackIdAsync(string? title, string? currentSteamGridDbId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(title) || !_d.HasSteamGridDbApiToken()) return null;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fallbackId = await _d.CoverService.TryResolveSteamGridDbIdByNameAsync(title, cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(fallbackId)) return null;
                if (string.Equals(fallbackId.Trim(), (currentSteamGridDbId ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase)) return null;
                return fallbackId.Trim();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _d.Log("SteamGridDB fallback ID search failed for " + title + ". " + ex.Message);
            }
            return null;
        }

        async Task<string?> TryDownloadSteamStoreHeaderHeroAsync(LibraryFolderInfo folder, CancellationToken cancellationToken = default)
        {
            if (folder == null) return null;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var appId = await ResolveBestLibraryFolderSteamAppIdAsync(LibraryRoot, folder, true, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                return await _d.CoverService.TryDownloadSteamStoreHeaderHeroAsync(folder.Name, appId, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _d.Log("Steam header hero download failed for " + (folder.Name ?? "unknown title") + ". " + ex.Message);
            }
            return null;
        }

        async Task<string?> TryDownloadSteamCoverAsync(LibraryFolderInfo folder, CancellationToken cancellationToken = default)
        {
            if (folder == null) return null;
            try
            {
                var appId = await ResolveBestLibraryFolderSteamAppIdAsync(LibraryRoot, folder, true, cancellationToken).ConfigureAwait(false);
                var downloaded = await _d.CoverService.TryDownloadSteamCoverAsync(folder.Name, appId, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(downloaded) && File.Exists(downloaded))
                {
                    folder.PreviewImagePath = downloaded;
                    UpdateCachedLibraryFolderInfo(LibraryRoot, folder);
                    return downloaded;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _d.Log("Steam cover download failed for " + (folder == null ? "unknown" : folder.Name) + ". " + ex.Message);
            }
            return null;
        }

        async Task<string?> TryDownloadSteamGridDbCoverAsync(LibraryFolderInfo folder, CancellationToken cancellationToken = default)
        {
            if (folder == null || !_d.HasSteamGridDbApiToken()) return null;
            try
            {
                var steamGridDbId = await ResolveBestLibraryFolderSteamGridDbIdAsync(LibraryRoot, folder, cancellationToken).ConfigureAwait(false);
                var downloaded = await _d.CoverService.TryDownloadSteamGridDbCoverAsync(folder.Name, steamGridDbId, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(downloaded) && File.Exists(downloaded))
                {
                    folder.PreviewImagePath = downloaded;
                    UpdateCachedLibraryFolderInfo(LibraryRoot, folder);
                    return downloaded;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _d.Log("SteamGridDB cover download failed for " + (folder.Name ?? "unknown title") + ". " + ex.Message);
            }
            return null;
        }

        void UpdateCachedLibraryFolderInfo(string root, LibraryFolderInfo folder)
        {
            if (folder == null || string.IsNullOrWhiteSpace(root)) return;
            var stamp = _d.BuildLibraryFolderInventoryStamp(root);
            var cached = _d.LoadLibraryFolderCache(root, stamp);
            if (cached == null) return;
            var normalizedGameId = _d.NormalizeGameId(folder.GameId);
            var match = !string.IsNullOrWhiteSpace(normalizedGameId)
                ? cached.FirstOrDefault(entry => string.Equals(_d.NormalizeGameId(entry.GameId), normalizedGameId, StringComparison.OrdinalIgnoreCase))
                : null;
            if (match == null)
            {
                var folderKey = _d.BuildLibraryFolderMasterKey(folder);
                match = cached.FirstOrDefault(entry => string.Equals(_d.BuildLibraryFolderMasterKey(entry), folderKey, StringComparison.OrdinalIgnoreCase));
            }
            if (match == null) return;
            match.GameId = !string.IsNullOrWhiteSpace(normalizedGameId) ? normalizedGameId : match.GameId;
            match.NewestCaptureUtcTicks = folder.NewestCaptureUtcTicks;
            match.NewestRecentSortUtcTicks = folder.NewestRecentSortUtcTicks;
            match.SteamAppId = folder.SteamAppId ?? string.Empty;
            match.SteamGridDbId = folder.SteamGridDbId ?? string.Empty;
            match.SuppressSteamAppIdAutoResolve = folder.SuppressSteamAppIdAutoResolve;
            match.SuppressSteamGridDbIdAutoResolve = folder.SuppressSteamGridDbIdAutoResolve;
            if (!string.IsNullOrWhiteSpace(folder.PreviewImagePath) && File.Exists(folder.PreviewImagePath))
            {
                match.PreviewImagePath = folder.PreviewImagePath;
            }
            _d.SaveLibraryFolderCache(root, stamp, cached);
            _d.UpsertSavedGameIndexRow(root, folder);
        }
    }
}
