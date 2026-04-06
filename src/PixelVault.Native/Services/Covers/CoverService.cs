using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    /// <summary>Steam / SteamGridDB HTTP helpers; network entry points are <c>*Async</c> only.</summary>
    interface ICoverService
    {
        Task<string> TryResolveSteamGridDbIdBySteamAppIdAsync(string steamAppId, CancellationToken cancellationToken = default(CancellationToken));
        Task<string> TryResolveSteamGridDbIdByNameAsync(string title, CancellationToken cancellationToken = default(CancellationToken));
        Task<List<Tuple<string, string>>> SearchSteamAppMatchesAsync(string title, CancellationToken cancellationToken = default(CancellationToken));
        Task<string> TryResolveSteamAppIdAsync(string title, CancellationToken cancellationToken = default(CancellationToken));
        Task<string> SteamNameAsync(string appId, CancellationToken cancellationToken = default(CancellationToken));
        string CustomCoverPath(LibraryFolderInfo folder);
        void SaveCustomCover(LibraryFolderInfo folder, string sourcePath);
        void ClearCustomCover(LibraryFolderInfo folder);
        string CachedCoverPath(string title);
        void DeleteCachedCover(string title);
        bool HasDedicatedLibraryCover(LibraryFolderInfo folder);
        Task<string> TryDownloadSteamCoverAsync(string title, string appId, CancellationToken cancellationToken = default(CancellationToken));
        Task<string> TryDownloadSteamGridDbCoverAsync(string title, string steamGridDbId, CancellationToken cancellationToken = default(CancellationToken));
    }

    sealed class CoverServiceDependencies
    {
        /// <summary>Optional; when set, custom cover save uses the seam instead of raw <see cref="File.Copy"/>.</summary>
        public IFileSystemService FileSystem;
        public string AppVersion;
        public string CoversRoot;
        public int RequestTimeoutMilliseconds;
        public Func<string> GetSteamGridDbApiToken;
        public Func<string, string> NormalizeTitle;
        public Func<string, string> NormalizeConsoleLabel;
        public Func<string, string> SafeCacheName;
        public Func<string, string> StripTags;
        public Func<string, string> Sanitize;
        public Action<string> Log;
        public Action<string, Stopwatch, string, long> LogPerformanceSample;
        public Action ClearImageCache;
        /// <summary>When set, cover file changes evict only these paths from the host image LRU instead of clearing the entire cache.</summary>
        public Action<IEnumerable<string>> RemoveCachedImageEntries;
    }

    /// <summary>Steam / SteamGridDB HTTP and local cover file helpers (<c>*Async</c> for network).</summary>
    /// <remarks>Local file operations (<see cref="CustomCoverPath"/>, etc.) remain synchronous.</remarks>
    sealed class CoverService : ICoverService
    {
        readonly CoverServiceDependencies dependencies;
        readonly Dictionary<string, string> steamCache = new Dictionary<string, string>();
        readonly Dictionary<string, string> steamSearchCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, List<Tuple<string, string>>> steamSearchResultsCache = new Dictionary<string, List<Tuple<string, string>>>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, string> steamGridDbSearchCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, string> steamGridDbPlatformCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        readonly object _steamAppNameCacheLock = new object();
        readonly object _steamSearchIdCacheLock = new object();
        readonly object _steamSearchResultsCacheLock = new object();
        readonly object _steamGridDbResponseCacheLock = new object();

        public CoverService(CoverServiceDependencies dependencies)
        {
            this.dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
        }

        bool HasSteamGridDbApiToken()
        {
            return !string.IsNullOrWhiteSpace(CurrentSteamGridDbApiToken());
        }

        string CurrentSteamGridDbApiToken()
        {
            return dependencies.GetSteamGridDbApiToken == null ? string.Empty : (dependencies.GetSteamGridDbApiToken() ?? string.Empty).Trim();
        }

        TimeoutWebClient CreateSteamWebClient()
        {
            return new TimeoutWebClient
            {
                Encoding = Encoding.UTF8,
                TimeoutMilliseconds = dependencies.RequestTimeoutMilliseconds
            };
        }

        TimeoutWebClient CreateSteamGridDbWebClient()
        {
            var token = CurrentSteamGridDbApiToken();
            if (string.IsNullOrWhiteSpace(token)) return null;
            var client = new TimeoutWebClient
            {
                Encoding = Encoding.UTF8,
                TimeoutMilliseconds = dependencies.RequestTimeoutMilliseconds
            };
            client.Headers[HttpRequestHeader.Authorization] = "Bearer " + token;
            client.Headers[HttpRequestHeader.Accept] = "application/json";
            client.Headers[HttpRequestHeader.UserAgent] = "PixelVault/" + dependencies.AppVersion;
            return client;
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

        List<Tuple<string, string>> ParseSteamSearchResults(string html)
        {
            var results = new List<Tuple<string, string>>();
            if (string.IsNullOrWhiteSpace(html)) return results;
            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match match in Regex.Matches(html, @"https://store\.steampowered\.com/app/(?<id>\d+)/[^""']+.*?<div class=""match_name"">(?<name>.*?)</div>", RegexOptions.Singleline))
            {
                var id = match.Groups["id"].Value;
                var name = WebUtility.HtmlDecode(StripTags(match.Groups["name"].Value));
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name) || !seenIds.Add(id)) continue;
                results.Add(Tuple.Create(id, name.Trim()));
            }
            return results;
        }

        string NormalizeTitle(string title)
        {
            return dependencies.NormalizeTitle == null ? (title ?? string.Empty) : dependencies.NormalizeTitle(title ?? string.Empty);
        }

        string SafeCacheName(string title)
        {
            return dependencies.SafeCacheName == null ? (title ?? string.Empty) : dependencies.SafeCacheName(title ?? string.Empty);
        }

        string NormalizeConsoleLabel(string value)
        {
            return dependencies.NormalizeConsoleLabel == null ? (value ?? string.Empty) : dependencies.NormalizeConsoleLabel(value ?? string.Empty);
        }

        string StripTags(string html)
        {
            return dependencies.StripTags == null ? (html ?? string.Empty) : dependencies.StripTags(html ?? string.Empty);
        }

        string Sanitize(string value)
        {
            return dependencies.Sanitize == null ? (value ?? string.Empty) : dependencies.Sanitize(value ?? string.Empty);
        }

        void Log(string message)
        {
            if (dependencies.Log != null) dependencies.Log(message);
        }

        void LogPerformanceSample(string area, Stopwatch stopwatch, string detail, long thresholdMilliseconds)
        {
            if (dependencies.LogPerformanceSample != null) dependencies.LogPerformanceSample(area, stopwatch, detail, thresholdMilliseconds);
        }

        void ClearImageCache()
        {
            if (dependencies.ClearImageCache != null) dependencies.ClearImageCache();
        }

        void InvalidateCoverImageCache(IEnumerable<string> coverFilePaths)
        {
            if (dependencies.RemoveCachedImageEntries != null)
            {
                dependencies.RemoveCachedImageEntries(coverFilePaths ?? Enumerable.Empty<string>());
                return;
            }
            ClearImageCache();
        }

        public async Task<string> TryResolveSteamGridDbIdBySteamAppIdAsync(string steamAppId, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (string.IsNullOrWhiteSpace(steamAppId) || !HasSteamGridDbApiToken()) return null;
            cancellationToken.ThrowIfCancellationRequested();
            var platformKey = "steam:" + steamAppId;
            string cached;
            lock (_steamGridDbResponseCacheLock)
            {
                if (steamGridDbPlatformCache.TryGetValue(platformKey, out cached)) return cached;
            }
            var stopwatch = Stopwatch.StartNew();
            try
            {
                using (var wc = CreateSteamGridDbWebClient())
                {
                    if (wc == null) return null;
                    var json = await wc.DownloadStringAsync("https://www.steamgriddb.com/api/v2/games/steam/" + Uri.EscapeDataString(steamAppId), cancellationToken).ConfigureAwait(false);
                    cached = ParseSteamGridDbIdFromGamePayload(json);
                    lock (_steamGridDbResponseCacheLock)
                    {
                        steamGridDbPlatformCache[platformKey] = cached;
                    }
                    return cached;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log("SteamGridDB lookup failed for Steam AppID " + steamAppId + ". " + ex.Message);
            }
            finally
            {
                stopwatch.Stop();
                LogPerformanceSample("SteamGridDbIdByAppId", stopwatch, "appId=" + steamAppId, 120);
            }
            lock (_steamGridDbResponseCacheLock)
            {
                steamGridDbPlatformCache[platformKey] = null;
            }
            return null;
        }

        public async Task<string> TryResolveSteamGridDbIdByNameAsync(string title, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (string.IsNullOrWhiteSpace(title) || !HasSteamGridDbApiToken()) return null;
            cancellationToken.ThrowIfCancellationRequested();
            string cached;
            lock (_steamGridDbResponseCacheLock)
            {
                if (steamGridDbSearchCache.TryGetValue(title, out cached)) return cached;
            }
            var stopwatch = Stopwatch.StartNew();
            try
            {
                using (var wc = CreateSteamGridDbWebClient())
                {
                    if (wc == null) return null;
                    var json = await wc.DownloadStringAsync("https://www.steamgriddb.com/api/v2/search/autocomplete/" + Uri.EscapeDataString(title), cancellationToken).ConfigureAwait(false);
                    cached = FindBestSteamGridDbSearchMatch(title, ParseSteamGridDbSearchResults(json));
                    lock (_steamGridDbResponseCacheLock)
                    {
                        steamGridDbSearchCache[title] = cached;
                    }
                    return cached;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log("SteamGridDB title search failed for " + title + ". " + ex.Message);
            }
            finally
            {
                stopwatch.Stop();
                LogPerformanceSample("SteamGridDbSearch", stopwatch, "title=" + title, 120);
            }
            lock (_steamGridDbResponseCacheLock)
            {
                steamGridDbSearchCache[title] = null;
            }
            return null;
        }

        public async Task<List<Tuple<string, string>>> SearchSteamAppMatchesAsync(string title, CancellationToken cancellationToken = default(CancellationToken))
        {
            var query = (title ?? string.Empty).Trim();
            cancellationToken.ThrowIfCancellationRequested();
            List<Tuple<string, string>> cached;
            lock (_steamSearchResultsCacheLock)
            {
                if (steamSearchResultsCache.TryGetValue(query, out cached))
                {
                    return cached == null ? new List<Tuple<string, string>>() : new List<Tuple<string, string>>(cached);
                }
            }
            var stopwatch = Stopwatch.StartNew();
            var results = new List<Tuple<string, string>>();
            try
            {
                if (!string.IsNullOrWhiteSpace(query))
                {
                    if (Regex.IsMatch(query, @"^\d+$"))
                    {
                        var appName = await SteamNameAsync(query, cancellationToken).ConfigureAwait(false);
                        if (!string.IsNullOrWhiteSpace(appName)) results.Add(Tuple.Create(query, appName));
                    }
                    else
                    {
                        using (var wc = CreateSteamWebClient())
                        {
                            var html = await wc.DownloadStringAsync("https://store.steampowered.com/search/suggest?term=" + Uri.EscapeDataString(query) + "&f=games&cc=US&l=english", cancellationToken).ConfigureAwait(false);
                            results = ParseSteamSearchResults(html);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log("Steam store search suggest failed for \"" + query + "\". " + ex.Message);
            }
            finally
            {
                stopwatch.Stop();
                LogPerformanceSample("SteamSearch", stopwatch, "title=" + query + "; results=" + results.Count, 120);
            }
            lock (_steamSearchResultsCacheLock)
            {
                steamSearchResultsCache[query] = new List<Tuple<string, string>>(results);
            }
            return results;
        }

        public async Task<string> TryResolveSteamAppIdAsync(string title, CancellationToken cancellationToken = default(CancellationToken))
        {
            string cached;
            lock (_steamSearchIdCacheLock)
            {
                if (steamSearchCache.TryGetValue(title, out cached)) return cached;
            }
            foreach (var match in await SearchSteamAppMatchesAsync(title, cancellationToken).ConfigureAwait(false))
            {
                if (NormalizeTitle(match.Item2) == NormalizeTitle(title))
                {
                    cached = match.Item1;
                    lock (_steamSearchIdCacheLock)
                    {
                        steamSearchCache[title] = cached;
                    }
                    return cached;
                }
            }
            lock (_steamSearchIdCacheLock)
            {
                steamSearchCache[title] = null;
            }
            return null;
        }

        public async Task<string> SteamNameAsync(string appId, CancellationToken cancellationToken = default(CancellationToken))
        {
            string cached;
            lock (_steamAppNameCacheLock)
            {
                if (steamCache.TryGetValue(appId, out cached)) return cached;
            }
            cancellationToken.ThrowIfCancellationRequested();
            var stopwatch = Stopwatch.StartNew();
            try
            {
                using (var wc = CreateSteamWebClient())
                {
                    var json = await wc.DownloadStringAsync("https://store.steampowered.com/api/appdetails?appids=" + appId + "&l=english", cancellationToken).ConfigureAwait(false);
                    var match = Regex.Match(json, "\"name\"\\s*:\\s*\"(?<n>(?:\\\\.|[^\"])*)\"");
                    if (match.Success)
                    {
                        cached = Sanitize(Regex.Unescape(match.Groups["n"].Value));
                        lock (_steamAppNameCacheLock)
                        {
                            steamCache[appId] = cached;
                        }
                        return cached;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log("Steam lookup failed for AppID " + appId + ". " + ex.Message);
            }
            finally
            {
                stopwatch.Stop();
                LogPerformanceSample("SteamAppDetails", stopwatch, "appId=" + appId, 120);
            }
            lock (_steamAppNameCacheLock)
            {
                steamCache[appId] = null;
            }
            return null;
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

        public string CustomCoverPath(LibraryFolderInfo folder)
        {
            var key = CustomCoverKey(folder);
            if (string.IsNullOrWhiteSpace(key)) return null;
            foreach (var ext in new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif" })
            {
                var path = Path.Combine(dependencies.CoversRoot, "custom-" + key + ext);
                if (File.Exists(path)) return path;
            }
            return null;
        }

        public void SaveCustomCover(LibraryFolderInfo folder, string sourcePath)
        {
            if (folder == null || string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath)) return;
            var key = CustomCoverKey(folder);
            if (string.IsNullOrWhiteSpace(key)) return;
            Directory.CreateDirectory(dependencies.CoversRoot);
            var invalidated = new List<string>();
            foreach (var ext in new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif" })
            {
                var existing = Path.Combine(dependencies.CoversRoot, "custom-" + key + ext);
                if (File.Exists(existing))
                {
                    invalidated.Add(existing);
                    File.Delete(existing);
                }
            }
            var extension = Path.GetExtension(sourcePath);
            if (string.IsNullOrWhiteSpace(extension)) extension = ".png";
            var target = Path.Combine(dependencies.CoversRoot, "custom-" + key + extension.ToLowerInvariant());
            if (dependencies.FileSystem != null) dependencies.FileSystem.CopyFile(sourcePath, target, true);
            else File.Copy(sourcePath, target, true);
            invalidated.Add(target);
            InvalidateCoverImageCache(invalidated);
        }

        public void ClearCustomCover(LibraryFolderInfo folder)
        {
            var key = CustomCoverKey(folder);
            if (string.IsNullOrWhiteSpace(key)) return;
            var invalidated = new List<string>();
            foreach (var ext in new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif" })
            {
                var existing = Path.Combine(dependencies.CoversRoot, "custom-" + key + ext);
                if (File.Exists(existing))
                {
                    invalidated.Add(existing);
                    File.Delete(existing);
                }
            }
            InvalidateCoverImageCache(invalidated);
        }

        public string CachedCoverPath(string title)
        {
            var safe = SafeCacheName(title);
            foreach (var ext in new[] { ".jpg", ".jpeg", ".png" })
            {
                var path = Path.Combine(dependencies.CoversRoot, safe + ext);
                if (File.Exists(path)) return path;
            }
            return null;
        }

        public void DeleteCachedCover(string title)
        {
            var safe = SafeCacheName(title);
            var invalidated = new List<string>();
            foreach (var ext in new[] { ".jpg", ".jpeg", ".png" })
            {
                var path = Path.Combine(dependencies.CoversRoot, safe + ext);
                if (File.Exists(path))
                {
                    invalidated.Add(path);
                    File.Delete(path);
                }
            }
            InvalidateCoverImageCache(invalidated);
        }

        public bool HasDedicatedLibraryCover(LibraryFolderInfo folder)
        {
            if (folder == null) return false;
            return !string.IsNullOrWhiteSpace(CustomCoverPath(folder)) || CachedCoverPath(folder.Name) != null;
        }

        public async Task<string> TryDownloadSteamCoverAsync(string title, string appId, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(appId)) return null;
            cancellationToken.ThrowIfCancellationRequested();
            var stopwatch = Stopwatch.StartNew();
            try
            {
                Directory.CreateDirectory(dependencies.CoversRoot);
                using (var wc = CreateSteamWebClient())
                {
                    var portraitUrls = new[]
                    {
                        "https://shared.cloudflare.steamstatic.com/store_item_assets/steam/apps/" + appId + "/library_600x900_2x.jpg",
                        "https://shared.cloudflare.steamstatic.com/store_item_assets/steam/apps/" + appId + "/library_600x900.jpg",
                        "https://cdn.cloudflare.steamstatic.com/steam/apps/" + appId + "/library_600x900_2x.jpg",
                        "https://cdn.cloudflare.steamstatic.com/steam/apps/" + appId + "/library_600x900.jpg"
                    };
                    foreach (var portraitUrl in portraitUrls)
                    {
                        try
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            var ext = Path.GetExtension(new Uri(portraitUrl).AbsolutePath);
                            if (string.IsNullOrWhiteSpace(ext)) ext = ".jpg";
                            var target = Path.Combine(dependencies.CoversRoot, SafeCacheName(title) + ext);
                            await wc.DownloadFileAsync(portraitUrl, target, cancellationToken).ConfigureAwait(false);
                            if (File.Exists(target) && new FileInfo(target).Length > 0) return target;
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch
                        {
                        }
                    }

                    var json = await wc.DownloadStringAsync("https://store.steampowered.com/api/appdetails?appids=" + appId + "&l=english", cancellationToken).ConfigureAwait(false);
                    var match = Regex.Match(json, "\"header_image\"\\s*:\\s*\"(?<u>(?:\\\\.|[^\"])*)\"");
                    if (!match.Success) return null;
                    var url = Regex.Unescape(match.Groups["u"].Value).Replace("\\/", "/");
                    var fallbackExt = Path.GetExtension(new Uri(url).AbsolutePath);
                    if (string.IsNullOrWhiteSpace(fallbackExt)) fallbackExt = ".jpg";
                    var fallbackTarget = Path.Combine(dependencies.CoversRoot, SafeCacheName(title) + fallbackExt);
                    await wc.DownloadFileAsync(url, fallbackTarget, cancellationToken).ConfigureAwait(false);
                    return File.Exists(fallbackTarget) ? fallbackTarget : null;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
            }
            finally
            {
                stopwatch.Stop();
                LogPerformanceSample("SteamCoverDownload", stopwatch, "title=" + title + "; appId=" + appId, 180);
            }
            return null;
        }

        public async Task<string> TryDownloadSteamGridDbCoverAsync(string title, string steamGridDbId, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(steamGridDbId) || !HasSteamGridDbApiToken()) return null;
            cancellationToken.ThrowIfCancellationRequested();
            var stopwatch = Stopwatch.StartNew();
            try
            {
                Directory.CreateDirectory(dependencies.CoversRoot);
                using (var wc = CreateSteamGridDbWebClient())
                {
                    if (wc == null) return null;
                    var json = await wc.DownloadStringAsync("https://www.steamgriddb.com/api/v2/grids/game/" + Uri.EscapeDataString(steamGridDbId) + "?dimensions=600x900,342x482,660x930&types=static&nsfw=false&humor=false&limit=1", cancellationToken).ConfigureAwait(false);
                    var match = Regex.Match(json, "\"url\"\\s*:\\s*\"(?<u>(?:\\\\.|[^\"])*)\"");
                    if (!match.Success) return null;
                    var url = Regex.Unescape(match.Groups["u"].Value).Replace("\\/", "/");
                    var ext = Path.GetExtension(new Uri(url).AbsolutePath);
                    if (string.IsNullOrWhiteSpace(ext)) ext = ".png";
                    var target = Path.Combine(dependencies.CoversRoot, SafeCacheName(title) + ext);
                    await wc.DownloadFileAsync(url, target, cancellationToken).ConfigureAwait(false);
                    if (File.Exists(target) && new FileInfo(target).Length > 0) return target;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log("SteamGridDB cover download failed for " + title + ". " + ex.Message);
            }
            finally
            {
                stopwatch.Stop();
                LogPerformanceSample("SteamGridDbCoverDownload", stopwatch, "title=" + title + "; stid=" + steamGridDbId, 180);
            }
            return null;
        }
    }
}
