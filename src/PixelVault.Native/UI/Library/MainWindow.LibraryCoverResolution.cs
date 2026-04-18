using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PixelVaultNative
{
    // PV-PLN-UI-001 Step 8 Pass A: moved verbatim from PixelVault.Native.cs to localise Steam /
    // SteamGridDB / cover resolution before Pass B extracts these methods into
    // Services/Covers/LibraryCoverResolutionService.cs behind ICoverService. No behaviour change.
    public sealed partial class MainWindow
    {
        /// <summary>Resolves a game title for sorting/organize. Pass <see cref="Path.GetFileName(string)"/> (with extension) when available so convention rules match; bare stems still work.</summary>
        string GetGameNameFromFileName(string fileNameOrPath)
        {
            if (string.IsNullOrWhiteSpace(fileNameOrPath)) return string.Empty;
            var fileName = fileNameOrPath;
            if (fileName.IndexOf('\\') >= 0 || fileName.IndexOf('/') >= 0)
                fileName = Path.GetFileName(fileName.Trim());
            if (string.IsNullOrWhiteSpace(fileName)) return string.Empty;
            var parseInput = string.IsNullOrWhiteSpace(Path.GetExtension(fileName))
                ? fileName + ".png"
                : fileName;
            var parsed = filenameParserService.Parse(parseInput, libraryRoot);
            if (!string.IsNullOrWhiteSpace(parsed.GameTitleHint)) return parsed.GameTitleHint.Trim();
            return filenameParserService.GetGameTitleHint(Path.GetFileNameWithoutExtension(parseInput), libraryRoot) ?? string.Empty;
        }

        string GetSafeGameFolderName(string name)
        {
            name = NormalizeGameFolderCapitalization(name ?? string.Empty);
            name = Regex.Replace(name, "[<>:\"/\\\\|?*\\x00-\\x1F]", string.Empty);
            name = name.Trim().TrimEnd('.');
            name = Regex.Replace(name, "\\s{2,}", " ").Trim();
            return string.IsNullOrWhiteSpace(name) ? "Unknown Game" : name;
        }

        string NormalizeGameFolderCapitalization(string name)
        {
            if (Regex.IsMatch(name ?? string.Empty, "[A-Z]") && !Regex.IsMatch(name ?? string.Empty, "[a-z]"))
            {
                return System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase((name ?? string.Empty).ToLowerInvariant());
            }
            return name ?? string.Empty;
        }

        string ParseSteamGridDbIdFromGamePayload(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            var match = Regex.Match(json, "\"id\"\\s*:\\s*(?<id>\\d+)");
            return match.Success ? match.Groups["id"].Value : null;
        }

        List<Tuple<string, string>> ParseSteamGridDbSearchResults(string json)
        {
            var matches = new List<Tuple<string, string>>();
            if (string.IsNullOrWhiteSpace(json)) return matches;
            foreach (Match match in Regex.Matches(json, "\"id\"\\s*:\\s*(?<id>\\d+)\\s*,\\s*\"name\"\\s*:\\s*\"(?<name>(?:\\\\.|[^\"])*)\"", RegexOptions.Singleline))
            {
                var id = match.Groups["id"].Value;
                var name = Regex.Unescape(match.Groups["name"].Value).Replace("\\/", "/");
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name)) continue;
                matches.Add(Tuple.Create(id, WebUtility.HtmlDecode(name)));
            }
            return matches;
        }

        string FindBestSteamGridDbSearchMatch(string title, IEnumerable<Tuple<string, string>> matches)
        {
            var candidates = (matches ?? Enumerable.Empty<Tuple<string, string>>()).ToList();
            if (string.IsNullOrWhiteSpace(title) || candidates.Count == 0) return null;
            var wanted = NormalizeTitle(title);
            var exact = candidates.FirstOrDefault(candidate => NormalizeTitle(candidate.Item2) == wanted);
            if (exact != null) return exact.Item1;
            var loose = candidates.Where(candidate =>
            {
                var normalized = NormalizeTitle(candidate.Item2);
                return normalized.StartsWith(wanted + " ", StringComparison.Ordinal)
                    || wanted.StartsWith(normalized + " ", StringComparison.Ordinal)
                    || normalized.Contains(wanted)
                    || wanted.Contains(normalized);
            }).ToList();
            return loose.Count == 1 ? loose[0].Item1 : null;
        }

        async Task<string> ResolveBestLibraryFolderSteamAppIdAsync(string root, LibraryFolderInfo folder, bool allowLookup = true, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (folder == null || string.IsNullOrWhiteSpace(folder.Name)) return string.Empty;
            if (folder.SuppressSteamAppIdAutoResolve) return string.Empty;
            if (!string.IsNullOrWhiteSpace(folder.SteamAppId)) return folder.SteamAppId;
            cancellationToken.ThrowIfCancellationRequested();
            var saved = FindSavedGameIndexRow(GetSavedGameIndexRowsForRoot(root), folder);
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
            var appId = ResolveLibraryFolderSteamAppId(folder.PlatformLabel, folder.FilePaths ?? new string[0]);
            if (string.IsNullOrWhiteSpace(appId))
            {
                cancellationToken.ThrowIfCancellationRequested();
                appId = await coverService.TryResolveSteamAppIdAsync(folder.Name, cancellationToken).ConfigureAwait(false);
            }
            if (!string.IsNullOrWhiteSpace(appId))
            {
                folder.SteamAppId = appId;
                UpsertSavedGameIndexRow(root, folder);
            }
            return folder.SteamAppId ?? string.Empty;
        }

        async Task<string> ResolveBestLibraryFolderSteamGridDbIdAsync(string root, LibraryFolderInfo folder, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (folder == null || string.IsNullOrWhiteSpace(folder.Name) || !HasSteamGridDbApiToken()) return string.Empty;
            if (folder.SuppressSteamGridDbIdAutoResolve) return string.Empty;
            if (!string.IsNullOrWhiteSpace(folder.SteamGridDbId)) return folder.SteamGridDbId;
            cancellationToken.ThrowIfCancellationRequested();
            var saved = FindSavedGameIndexRow(GetSavedGameIndexRowsForRoot(root), folder);
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
            string steamGridDbId = null;
            if (ShouldUseSteamStoreLookups(folder))
            {
                var appId = await ResolveBestLibraryFolderSteamAppIdAsync(root, folder, true, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                steamGridDbId = !string.IsNullOrWhiteSpace(appId)
                    ? await coverService.TryResolveSteamGridDbIdBySteamAppIdAsync(appId, cancellationToken).ConfigureAwait(false)
                    : null;
            }
            if (string.IsNullOrWhiteSpace(steamGridDbId))
            {
                cancellationToken.ThrowIfCancellationRequested();
                steamGridDbId = await coverService.TryResolveSteamGridDbIdByNameAsync(folder.Name, cancellationToken).ConfigureAwait(false);
            }
            if (!string.IsNullOrWhiteSpace(steamGridDbId))
            {
                folder.SteamGridDbId = steamGridDbId;
                UpsertSavedGameIndexRow(root, folder);
            }
            return folder.SteamGridDbId ?? string.Empty;
        }

        async Task<string> ResolveBestLibraryFolderSteamGridDbIdForHeroAssetsAsync(string root, LibraryFolderInfo folder, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (folder == null || string.IsNullOrWhiteSpace(folder.Name) || !HasSteamGridDbApiToken()) return string.Empty;
            if (!string.IsNullOrWhiteSpace(folder.SteamGridDbId)) return folder.SteamGridDbId;
            cancellationToken.ThrowIfCancellationRequested();

            var saved = FindSavedGameIndexRow(GetSavedGameIndexRowsForRoot(root), folder);
            if (saved != null && !string.IsNullOrWhiteSpace(saved.SteamGridDbId))
            {
                folder.SteamGridDbId = saved.SteamGridDbId;
                folder.SuppressSteamGridDbIdAutoResolve = false;
                return folder.SteamGridDbId;
            }

            string steamGridDbId = null;
            if (ShouldUseSteamStoreLookups(folder))
            {
                var appId = await ResolveBestLibraryFolderSteamAppIdAsync(root, folder, true, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                steamGridDbId = !string.IsNullOrWhiteSpace(appId)
                    ? await coverService.TryResolveSteamGridDbIdBySteamAppIdAsync(appId, cancellationToken).ConfigureAwait(false)
                    : null;
            }
            if (string.IsNullOrWhiteSpace(steamGridDbId))
            {
                cancellationToken.ThrowIfCancellationRequested();
                steamGridDbId = await coverService.TryResolveSteamGridDbIdByNameAsync(folder.Name, cancellationToken).ConfigureAwait(false);
            }
            if (!string.IsNullOrWhiteSpace(steamGridDbId))
            {
                folder.SteamGridDbId = steamGridDbId;
                folder.SuppressSteamGridDbIdAutoResolve = false;
                UpdateCachedLibraryFolderInfo(root, folder);
            }
            return folder.SteamGridDbId ?? string.Empty;
        }

        bool ShouldUseSteamStoreLookups(LibraryFolderInfo folder)
        {
            var platform = NormalizeConsoleLabel(folder == null ? string.Empty : folder.PlatformLabel);
            if (string.Equals(platform, "Steam", StringComparison.OrdinalIgnoreCase)
                || string.Equals(platform, "PC", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            foreach (var file in (folder == null ? new string[0] : (folder.FilePaths ?? new string[0])).Where(File.Exists).Take(3))
            {
                var parsedPlatform = NormalizeConsoleLabel(ParseFilename(file, libraryRoot).PlatformLabel);
                if (string.Equals(parsedPlatform, "Steam", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(parsedPlatform, "PC", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Resolves missing Steam App IDs using async Steam lookups (no synchronous blocking on async work). Await from UI; <paramref name="progress"/> may run on a thread-pool continuation after network waits.</summary>
        async Task<int> EnrichLibraryFoldersWithSteamAppIdsAsync(string root, List<LibraryFolderInfo> folders, Action<int, int, string> progress, CancellationToken cancellationToken = default(CancellationToken))
        {
            var targetFolders = (folders ?? new List<LibraryFolderInfo>())
                .Where(folder => folder != null && !string.IsNullOrWhiteSpace(folder.Name))
                .GroupBy(BuildLibraryFolderMasterKey, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(folder => folder.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (targetFolders.Count == 0) return 0;
            int resolved = 0;
            for (int i = 0; i < targetFolders.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var folder = targetFolders[i];
                var detailPrefix = "Steam AppID " + (i + 1) + " of " + targetFolders.Count + " | " + folder.Name;
                if (!string.IsNullOrWhiteSpace(folder.SteamAppId))
                {
                    if (progress != null) progress(i + 1, targetFolders.Count, detailPrefix + " | already cached as " + folder.SteamAppId);
                    continue;
                }

                var appId = await ResolveBestLibraryFolderSteamAppIdAsync(root, folder, true, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(appId))
                {
                    var matchKey = BuildLibraryFolderMasterKey(folder);
                    foreach (var match in folders.Where(entry => entry != null && string.Equals(BuildLibraryFolderMasterKey(entry), matchKey, StringComparison.OrdinalIgnoreCase)))
                    {
                        match.SteamAppId = appId;
                    }
                    resolved++;
                    if (progress != null) progress(i + 1, targetFolders.Count, detailPrefix + " | resolved " + appId);
                }
                else
                {
                    if (progress != null) progress(i + 1, targetFolders.Count, detailPrefix + " | no match");
                }
            }

            var stamp = BuildLibraryFolderInventoryStamp(root);
            var cached = LoadLibraryFolderCache(root, stamp);
            if (cached == null || cached.Count == 0)
            {
                RefreshCachedLibraryFoldersFromGameIndex(root);
                return resolved;
            }

            foreach (var updated in folders.Where(entry => entry != null))
            {
                var normalizedGameId = NormalizeGameId(updated.GameId);
                var match = !string.IsNullOrWhiteSpace(normalizedGameId)
                    ? cached.FirstOrDefault(entry => string.Equals(NormalizeGameId(entry.GameId), normalizedGameId, StringComparison.OrdinalIgnoreCase))
                    : null;
                if (match == null)
                {
                    var folderKey = BuildLibraryFolderMasterKey(updated);
                    match = cached.FirstOrDefault(entry => string.Equals(BuildLibraryFolderMasterKey(entry), folderKey, StringComparison.OrdinalIgnoreCase));
                }
                if (match == null) continue;
                if (!string.IsNullOrWhiteSpace(updated.SteamAppId)) match.SteamAppId = updated.SteamAppId;
            }
            SaveLibraryFolderCache(root, stamp, cached);
            return resolved;
        }

        bool HasDedicatedLibraryCover(LibraryFolderInfo folder)
        {
            return coverService.HasDedicatedLibraryCover(folder);
        }

        void DeleteCachedCover(string title)
        {
            coverService.DeleteCachedCover(title);
        }

        async Task<string> ForceRefreshLibraryArtAsync(LibraryFolderInfo folder, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (folder == null) return null;
            var custom = CustomCoverPath(folder);
            if (!string.IsNullOrWhiteSpace(custom)) return custom;

            var existingCached = CachedCoverPath(folder.Name);
            string backupPath = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(existingCached) && File.Exists(existingCached))
                {
                    backupPath = existingCached + ".bak-" + Guid.NewGuid().ToString("N");
                    fileSystemService.CopyFile(existingCached, backupPath, true);
                }

                var steamDownloaded = await TryDownloadSteamCoverAsync(folder, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(steamDownloaded) && File.Exists(steamDownloaded))
                {
                    if (!string.IsNullOrWhiteSpace(backupPath) && File.Exists(backupPath)) File.Delete(backupPath);
                    return steamDownloaded;
                }

                DeleteCachedCover(folder.Name);
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
                    RemoveCachedImageEntries(new[] { existingCached });
                    return existingCached;
                }
            }
            catch (Exception ex)
            {
                Log("ForceRefreshLibraryArt failed mid-refresh. " + ex.Message);
                if (!string.IsNullOrWhiteSpace(backupPath) && File.Exists(backupPath) && !string.IsNullOrWhiteSpace(existingCached))
                {
                    try
                    {
                        if (File.Exists(existingCached)) File.Delete(existingCached);
                        File.Move(backupPath, existingCached);
                        RemoveCachedImageEntries(new[] { existingCached });
                        return existingCached;
                    }
                    catch (Exception restoreEx)
                    {
                        Log("Custom cover: could not promote backup over cached file. " + restoreEx.Message);
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
                        Log("Custom cover: could not delete temp backup. " + delEx.Message);
                    }
                }
            }

            return CachedCoverPath(folder.Name);
        }

        async Task<(int resolvedIds, int coversReady)> RefreshLibraryCoversAsync(string root, List<LibraryFolderInfo> libraryFolders, List<LibraryFolderInfo> requestedFolders, Action<int, int, string> progress, CancellationToken cancellationToken, bool forceRefreshExistingCovers, bool rebuildFullCacheAfterRefresh)
        {
            var resolvedIds = 0;
            var coversReady = 0;
            var allFolders = (libraryFolders ?? requestedFolders ?? new List<LibraryFolderInfo>())
                .Where(folder => folder != null && !string.IsNullOrWhiteSpace(folder.Name))
                .ToList();
            var targetFolders = (requestedFolders ?? libraryFolders ?? new List<LibraryFolderInfo>())
                .Where(folder => folder != null && !string.IsNullOrWhiteSpace(folder.Name))
                .GroupBy(BuildLibraryFolderMasterKey, StringComparer.OrdinalIgnoreCase)
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
                    var matchKey = BuildLibraryFolderMasterKey(folder);
                    foreach (var match in allFolders.Where(entry => entry != null && string.Equals(BuildLibraryFolderMasterKey(entry), matchKey, StringComparison.OrdinalIgnoreCase)))
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
                var hasCustomCover = !string.IsNullOrWhiteSpace(CustomCoverPath(folder));
                var hadCachedCover = CachedCoverPath(folder.Name) != null;
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
                    coverReady = HasDedicatedLibraryCover(folder);
                    coverDetail = coverReady ? "cover ready" : "cover not available";
                }
                if (coverReady) coversReady++;
                completed++;
                if (progress != null) progress(completed, totalWork, itemLabel + " | " + coverDetail);
            }
            if (rebuildFullCacheAfterRefresh)
            {
                RefreshCachedLibraryFoldersFromGameIndex(root);
                return (resolvedIds, coversReady);
            }
            var stamp = BuildLibraryFolderInventoryStamp(root);
            var cached = LoadLibraryFolderCache(root, stamp);
            if (cached == null || cached.Count == 0)
            {
                SaveLibraryFolderCache(root, stamp, allFolders);
                return (resolvedIds, coversReady);
            }
            foreach (var updated in allFolders.Where(entry => entry != null))
            {
                var normalizedGameId = NormalizeGameId(updated.GameId);
                var match = !string.IsNullOrWhiteSpace(normalizedGameId)
                    ? cached.FirstOrDefault(entry => string.Equals(NormalizeGameId(entry.GameId), normalizedGameId, StringComparison.OrdinalIgnoreCase))
                    : null;
                if (match == null)
                {
                    var folderKey = BuildLibraryFolderMasterKey(updated);
                    match = cached.FirstOrDefault(entry => string.Equals(BuildLibraryFolderMasterKey(entry), folderKey, StringComparison.OrdinalIgnoreCase));
                }
                if (match == null) continue;
                if (!string.IsNullOrWhiteSpace(updated.SteamAppId)) match.SteamAppId = updated.SteamAppId;
                if (!string.IsNullOrWhiteSpace(updated.SteamGridDbId)) match.SteamGridDbId = updated.SteamGridDbId;
            }
            SaveLibraryFolderCache(root, stamp, cached);
            return (resolvedIds, coversReady);
        }

        bool TryGetCustomOrCachedCoverPath(LibraryFolderInfo folder, out string path)
        {
            path = null;
            if (folder == null) return false;
            var custom = CustomCoverPath(folder);
            if (!string.IsNullOrWhiteSpace(custom))
            {
                path = custom;
                return true;
            }

            var cached = CachedCoverPath(folder.Name);
            if (cached != null)
            {
                path = cached;
                return true;
            }

            return false;
        }

        /// <summary>Custom cover, on-disk cache entry, or folder preview path — no network (Library tiles / banner when downloads are off).</summary>
        internal string GetLibraryArtPathForDisplayOnly(LibraryFolderInfo folder)
        {
            if (TryGetCustomOrCachedCoverPath(folder, out var early)) return early;
            return folder?.PreviewImagePath;
        }

        /// <summary>
        /// Resolve a library folder cover path. When <paramref name="allowDownload"/> is false, returns a <b>completed</b> task
        /// (<see cref="GetLibraryArtPathForDisplayOnly"/> only — no network). When true, may download via Steam / SteamGridDB.
        /// </summary>
        Task<string> ResolveLibraryArtAsync(LibraryFolderInfo folder, bool allowDownload, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (folder == null) return Task.FromResult<string>(null);
            if (!allowDownload) return Task.FromResult(GetLibraryArtPathForDisplayOnly(folder));
            return ResolveLibraryArtWithDownloadAsync(folder, cancellationToken);
        }

        async Task<string> ResolveLibraryArtWithDownloadAsync(LibraryFolderInfo folder, CancellationToken cancellationToken)
        {
            if (TryGetCustomOrCachedCoverPath(folder, out var early)) return early;
            var downloaded = await TryDownloadSteamCoverAsync(folder, cancellationToken).ConfigureAwait(false);
            if (downloaded != null) return downloaded;
            var steamGridDbDownloaded = await TryDownloadSteamGridDbCoverAsync(folder, cancellationToken).ConfigureAwait(false);
            if (steamGridDbDownloaded != null) return steamGridDbDownloaded;
            return folder.PreviewImagePath;
        }

        string CustomCoverKey(LibraryFolderInfo folder)
        {
            if (folder == null) return null;
            try
            {
                var source = (folder.FolderPath ?? string.Empty) + "|" + NormalizeConsoleLabel(folder.PlatformLabel) + "|" + (folder.Name ?? string.Empty);
                using (var md5 = MD5.Create())
                {
                    var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(source));
                    return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
                }
            }
            catch
            {
                return null;
            }
        }

        string CustomCoverPath(LibraryFolderInfo folder)
        {
            return coverService.CustomCoverPath(folder);
        }

        void SaveCustomCover(LibraryFolderInfo folder, string sourcePath)
        {
            coverService.SaveCustomCover(folder, sourcePath);
        }

        void ClearCustomCover(LibraryFolderInfo folder)
        {
            coverService.ClearCustomCover(folder);
        }

        string CachedCoverPath(string title)
        {
            return coverService.CachedCoverPath(title);
        }

        string CustomHeroPath(LibraryFolderInfo folder)
        {
            return coverService.CustomHeroPath(folder);
        }

        void SaveCustomHero(LibraryFolderInfo folder, string sourcePath)
        {
            coverService.SaveCustomHero(folder, sourcePath);
        }

        void ClearCustomHero(LibraryFolderInfo folder)
        {
            coverService.ClearCustomHero(folder);
        }

        string CustomLogoPath(LibraryFolderInfo folder)
        {
            return coverService.CustomLogoPath(folder);
        }

        void SaveCustomLogo(LibraryFolderInfo folder, string sourcePath)
        {
            coverService.SaveCustomLogo(folder, sourcePath);
        }

        void ClearCustomLogo(LibraryFolderInfo folder)
        {
            coverService.ClearCustomLogo(folder);
        }

        string CachedHeroPath(string title)
        {
            return coverService.CachedHeroPath(title);
        }

        string CachedLogoPath(string title)
        {
            return coverService.CachedLogoPath(title);
        }

        bool TryGetCustomOrCachedHeroPath(LibraryFolderInfo folder, out string path)
        {
            path = null;
            if (folder == null) return false;
            var custom = CustomHeroPath(folder);
            if (!string.IsNullOrWhiteSpace(custom))
            {
                path = custom;
                return true;
            }

            var cached = CachedHeroPath(folder.Name);
            if (cached != null)
            {
                path = cached;
                return true;
            }

            return false;
        }

        bool TryGetCachedHeroLogoPath(LibraryFolderInfo folder, out string path)
        {
            path = null;
            if (folder == null) return false;
            var custom = CustomLogoPath(folder);
            if (custom != null)
            {
                path = custom;
                return true;
            }
            var cached = CachedLogoPath(folder.Name);
            if (cached != null)
            {
                path = cached;
                return true;
            }

            return false;
        }

        /// <summary>Photo-workspace banner: custom or cached wide art only — prefers SteamGridDB Heroes (same asset class as https://www.steamgriddb.com/hero/…), not library cover.</summary>
        internal string GetLibraryHeroBannerPathForDisplayOnly(LibraryFolderInfo folder)
        {
            return TryGetCustomOrCachedHeroPath(folder, out var p) ? p : null;
        }

        /// <summary>Photo-workspace logo slot: custom logo or auto-cached SteamGridDB logo/icon; falls back to title text when neither asset exists.</summary>
        internal string GetLibraryHeroLogoPathForDisplayOnly(LibraryFolderInfo folder)
        {
            return TryGetCachedHeroLogoPath(folder, out var p) ? p : null;
        }

        /// <summary>Resolves banner art: custom → <b>SteamGridDB Heroes</b> → Valve library_hero / store header fallback.</summary>
        /// <remarks>Per-step HTTP coalescing lives in <see cref="CoverService"/>; <paramref name="cancellationToken"/> is observed between Steam/SteamGridDB ID resolution and fallback steps (shared in-flight hero HTTP uses <see cref="CancellationToken.None"/> inside coalesced work).</remarks>
        async Task<string> ResolveLibraryHeroBannerWithDownloadAsync(LibraryFolderInfo folder, CancellationToken cancellationToken = default(CancellationToken))
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

        /// <summary>Resolves photo-workspace logo art: custom logo or cached SteamGridDB logo/icon, else network download; falls back to title text when unavailable.</summary>
        async Task<string> ResolveLibraryHeroLogoWithDownloadAsync(LibraryFolderInfo folder, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (folder == null) return null;
            cancellationToken.ThrowIfCancellationRequested();
            if (TryGetCachedHeroLogoPath(folder, out var early)) return early;
            var steamGridDbLogo = await TryDownloadSteamGridDbLogoAsync(folder, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(steamGridDbLogo) && File.Exists(steamGridDbLogo)) return steamGridDbLogo;
            return null;
        }

        async Task<string> TryDownloadSteamGridDbHeroAsync(LibraryFolderInfo folder, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (folder == null || !HasSteamGridDbApiToken()) return null;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var steamGridDbId = await ResolveBestLibraryFolderSteamGridDbIdForHeroAssetsAsync(libraryRoot, folder, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                var downloaded = await coverService.TryDownloadSteamGridDbHeroAsync(folder.Name, steamGridDbId, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(downloaded) && File.Exists(downloaded)) return downloaded;
                var fallbackId = await TryResolveSteamGridDbNameFallbackIdAsync(folder.Name, steamGridDbId, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(fallbackId))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    downloaded = await coverService.TryDownloadSteamGridDbHeroAsync(folder.Name, fallbackId, cancellationToken).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(downloaded) && File.Exists(downloaded))
                    {
                        folder.SteamGridDbId = fallbackId;
                        folder.SuppressSteamGridDbIdAutoResolve = false;
                        UpdateCachedLibraryFolderInfo(libraryRoot, folder);
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
                Log("SteamGridDB hero download failed for " + (folder.Name ?? "unknown title") + ". " + ex.Message);
            }
            return null;
        }

        async Task<string> TryDownloadSteamGridDbLogoAsync(LibraryFolderInfo folder, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (folder == null || !HasSteamGridDbApiToken()) return null;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var steamGridDbId = await ResolveBestLibraryFolderSteamGridDbIdForHeroAssetsAsync(libraryRoot, folder, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                var downloaded = await coverService.TryDownloadSteamGridDbLogoAsync(folder.Name, steamGridDbId, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(downloaded) && File.Exists(downloaded)) return downloaded;
                var fallbackId = await TryResolveSteamGridDbNameFallbackIdAsync(folder.Name, steamGridDbId, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(fallbackId))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    downloaded = await coverService.TryDownloadSteamGridDbLogoAsync(folder.Name, fallbackId, cancellationToken).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(downloaded) && File.Exists(downloaded))
                    {
                        folder.SteamGridDbId = fallbackId;
                        folder.SuppressSteamGridDbIdAutoResolve = false;
                        UpdateCachedLibraryFolderInfo(libraryRoot, folder);
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
                Log("SteamGridDB logo download failed for " + (folder.Name ?? "unknown title") + ". " + ex.Message);
            }
            return null;
        }

        async Task<string> TryResolveSteamGridDbNameFallbackIdAsync(string title, string currentSteamGridDbId, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (string.IsNullOrWhiteSpace(title) || !HasSteamGridDbApiToken()) return null;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fallbackId = await coverService.TryResolveSteamGridDbIdByNameAsync(title, cancellationToken).ConfigureAwait(false);
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
                Log("SteamGridDB fallback ID search failed for " + title + ". " + ex.Message);
            }
            return null;
        }

        async Task<string> TryDownloadSteamStoreHeaderHeroAsync(LibraryFolderInfo folder, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (folder == null) return null;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var appId = await ResolveBestLibraryFolderSteamAppIdAsync(libraryRoot, folder, true, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                return await coverService.TryDownloadSteamStoreHeaderHeroAsync(folder.Name, appId, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log("Steam header hero download failed for " + (folder.Name ?? "unknown title") + ". " + ex.Message);
            }
            return null;
        }

        async Task<string> TryDownloadSteamCoverAsync(LibraryFolderInfo folder, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (folder == null) return null;
            try
            {
                var appId = await ResolveBestLibraryFolderSteamAppIdAsync(libraryRoot, folder, true, cancellationToken).ConfigureAwait(false);
                var downloaded = await coverService.TryDownloadSteamCoverAsync(folder.Name, appId, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(downloaded) && File.Exists(downloaded))
                {
                    folder.PreviewImagePath = downloaded;
                    UpdateCachedLibraryFolderInfo(libraryRoot, folder);
                    return downloaded;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log("Steam cover download failed for " + (folder == null ? "unknown" : folder.Name) + ". " + ex.Message);
            }
            return null;
        }

        async Task<string> TryDownloadSteamGridDbCoverAsync(LibraryFolderInfo folder, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (folder == null || !HasSteamGridDbApiToken()) return null;
            try
            {
                var steamGridDbId = await ResolveBestLibraryFolderSteamGridDbIdAsync(libraryRoot, folder, cancellationToken).ConfigureAwait(false);
                var downloaded = await coverService.TryDownloadSteamGridDbCoverAsync(folder.Name, steamGridDbId, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(downloaded) && File.Exists(downloaded))
                {
                    folder.PreviewImagePath = downloaded;
                    UpdateCachedLibraryFolderInfo(libraryRoot, folder);
                    return downloaded;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log("SteamGridDB cover download failed for " + (folder.Name ?? "unknown title") + ". " + ex.Message);
            }
            return null;
        }

        void UpdateCachedLibraryFolderInfo(string root, LibraryFolderInfo folder)
        {
            if (folder == null || string.IsNullOrWhiteSpace(root)) return;
            var stamp = BuildLibraryFolderInventoryStamp(root);
            var cached = LoadLibraryFolderCache(root, stamp);
            if (cached == null) return;
            var normalizedGameId = NormalizeGameId(folder.GameId);
            var match = !string.IsNullOrWhiteSpace(normalizedGameId)
                ? cached.FirstOrDefault(entry => string.Equals(NormalizeGameId(entry.GameId), normalizedGameId, StringComparison.OrdinalIgnoreCase))
                : null;
            if (match == null)
            {
                var folderKey = BuildLibraryFolderMasterKey(folder);
                match = cached.FirstOrDefault(entry => string.Equals(BuildLibraryFolderMasterKey(entry), folderKey, StringComparison.OrdinalIgnoreCase));
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
            SaveLibraryFolderCache(root, stamp, cached);
            UpsertSavedGameIndexRow(root, folder);
        }
    }
}
