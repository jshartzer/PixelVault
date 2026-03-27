using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace PixelVaultNative
{
    interface ICoverService
    {
        string TryResolveSteamGridDbIdBySteamAppId(string steamAppId);
        string TryResolveSteamGridDbIdByName(string title);
        string TryResolveSteamAppId(string title);
        string SteamName(string appId);
        string CustomCoverPath(LibraryFolderInfo folder);
        void SaveCustomCover(LibraryFolderInfo folder, string sourcePath);
        void ClearCustomCover(LibraryFolderInfo folder);
        string CachedCoverPath(string title);
        void DeleteCachedCover(string title);
        bool HasDedicatedLibraryCover(LibraryFolderInfo folder);
        string TryDownloadSteamCover(string title, string appId);
        string TryDownloadSteamGridDbCover(string title, string steamGridDbId);
    }

    sealed class CoverServiceDependencies
    {
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
    }

    sealed class CoverService : ICoverService
    {
        readonly CoverServiceDependencies dependencies;
        readonly Dictionary<string, string> steamCache = new Dictionary<string, string>();
        readonly Dictionary<string, string> steamSearchCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, string> steamGridDbSearchCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, string> steamGridDbPlatformCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

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

        public string TryResolveSteamGridDbIdBySteamAppId(string steamAppId)
        {
            if (string.IsNullOrWhiteSpace(steamAppId) || !HasSteamGridDbApiToken()) return null;
            string cached;
            if (steamGridDbPlatformCache.TryGetValue("steam:" + steamAppId, out cached)) return cached;
            var stopwatch = Stopwatch.StartNew();
            try
            {
                using (var wc = CreateSteamGridDbWebClient())
                {
                    if (wc == null) return null;
                    var json = wc.DownloadString("https://www.steamgriddb.com/api/v2/games/steam/" + Uri.EscapeDataString(steamAppId));
                    cached = ParseSteamGridDbIdFromGamePayload(json);
                    steamGridDbPlatformCache["steam:" + steamAppId] = cached;
                    return cached;
                }
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
            steamGridDbPlatformCache["steam:" + steamAppId] = null;
            return null;
        }

        public string TryResolveSteamGridDbIdByName(string title)
        {
            if (string.IsNullOrWhiteSpace(title) || !HasSteamGridDbApiToken()) return null;
            string cached;
            if (steamGridDbSearchCache.TryGetValue(title, out cached)) return cached;
            var stopwatch = Stopwatch.StartNew();
            try
            {
                using (var wc = CreateSteamGridDbWebClient())
                {
                    if (wc == null) return null;
                    var json = wc.DownloadString("https://www.steamgriddb.com/api/v2/search/autocomplete/" + Uri.EscapeDataString(title));
                    cached = FindBestSteamGridDbSearchMatch(title, ParseSteamGridDbSearchResults(json));
                    steamGridDbSearchCache[title] = cached;
                    return cached;
                }
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
            steamGridDbSearchCache[title] = null;
            return null;
        }

        public string TryResolveSteamAppId(string title)
        {
            string cached;
            if (steamSearchCache.TryGetValue(title, out cached)) return cached;
            var stopwatch = Stopwatch.StartNew();
            try
            {
                using (var wc = CreateSteamWebClient())
                {
                    var html = wc.DownloadString("https://store.steampowered.com/search/suggest?term=" + Uri.EscapeDataString(title) + "&f=games&cc=US&l=english");
                    var matches = Regex.Matches(html, @"https://store\.steampowered\.com/app/(?<id>\d+)/[^""']+.*?<div class=""match_name"">(?<name>.*?)</div>", RegexOptions.Singleline);
                    var wanted = NormalizeTitle(title);
                    foreach (Match match in matches)
                    {
                        var candidate = NormalizeTitle(WebUtility.HtmlDecode(StripTags(match.Groups["name"].Value)));
                        if (candidate == wanted)
                        {
                            cached = match.Groups["id"].Value;
                            steamSearchCache[title] = cached;
                            return cached;
                        }
                    }
                }
            }
            catch
            {
            }
            finally
            {
                stopwatch.Stop();
                LogPerformanceSample("SteamSearch", stopwatch, "title=" + title, 120);
            }
            steamSearchCache[title] = null;
            return null;
        }

        public string SteamName(string appId)
        {
            string cached;
            if (steamCache.TryGetValue(appId, out cached)) return cached;
            var stopwatch = Stopwatch.StartNew();
            try
            {
                using (var wc = CreateSteamWebClient())
                {
                    var json = wc.DownloadString("https://store.steampowered.com/api/appdetails?appids=" + appId + "&l=english");
                    var match = Regex.Match(json, "\"name\"\\s*:\\s*\"(?<n>(?:\\\\.|[^\"])*)\"");
                    if (match.Success)
                    {
                        cached = Sanitize(Regex.Unescape(match.Groups["n"].Value));
                        steamCache[appId] = cached;
                        return cached;
                    }
                }
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
            steamCache[appId] = null;
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
            foreach (var ext in new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif" })
            {
                var existing = Path.Combine(dependencies.CoversRoot, "custom-" + key + ext);
                if (File.Exists(existing)) File.Delete(existing);
            }
            var extension = Path.GetExtension(sourcePath);
            if (string.IsNullOrWhiteSpace(extension)) extension = ".png";
            var target = Path.Combine(dependencies.CoversRoot, "custom-" + key + extension.ToLowerInvariant());
            File.Copy(sourcePath, target, true);
            ClearImageCache();
        }

        public void ClearCustomCover(LibraryFolderInfo folder)
        {
            var key = CustomCoverKey(folder);
            if (string.IsNullOrWhiteSpace(key)) return;
            foreach (var ext in new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif" })
            {
                var existing = Path.Combine(dependencies.CoversRoot, "custom-" + key + ext);
                if (File.Exists(existing)) File.Delete(existing);
            }
            ClearImageCache();
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
            foreach (var ext in new[] { ".jpg", ".jpeg", ".png" })
            {
                var path = Path.Combine(dependencies.CoversRoot, safe + ext);
                if (File.Exists(path)) File.Delete(path);
            }
            ClearImageCache();
        }

        public bool HasDedicatedLibraryCover(LibraryFolderInfo folder)
        {
            if (folder == null) return false;
            return !string.IsNullOrWhiteSpace(CustomCoverPath(folder)) || CachedCoverPath(folder.Name) != null;
        }

        public string TryDownloadSteamCover(string title, string appId)
        {
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(appId)) return null;
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
                            var ext = Path.GetExtension(new Uri(portraitUrl).AbsolutePath);
                            if (string.IsNullOrWhiteSpace(ext)) ext = ".jpg";
                            var target = Path.Combine(dependencies.CoversRoot, SafeCacheName(title) + ext);
                            wc.DownloadFile(portraitUrl, target);
                            if (File.Exists(target) && new FileInfo(target).Length > 0) return target;
                        }
                        catch
                        {
                        }
                    }

                    var json = wc.DownloadString("https://store.steampowered.com/api/appdetails?appids=" + appId + "&l=english");
                    var match = Regex.Match(json, "\"header_image\"\\s*:\\s*\"(?<u>(?:\\\\.|[^\"])*)\"");
                    if (!match.Success) return null;
                    var url = Regex.Unescape(match.Groups["u"].Value).Replace("\\/", "/");
                    var fallbackExt = Path.GetExtension(new Uri(url).AbsolutePath);
                    if (string.IsNullOrWhiteSpace(fallbackExt)) fallbackExt = ".jpg";
                    var fallbackTarget = Path.Combine(dependencies.CoversRoot, SafeCacheName(title) + fallbackExt);
                    wc.DownloadFile(url, fallbackTarget);
                    return File.Exists(fallbackTarget) ? fallbackTarget : null;
                }
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

        public string TryDownloadSteamGridDbCover(string title, string steamGridDbId)
        {
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(steamGridDbId) || !HasSteamGridDbApiToken()) return null;
            var stopwatch = Stopwatch.StartNew();
            try
            {
                Directory.CreateDirectory(dependencies.CoversRoot);
                using (var wc = CreateSteamGridDbWebClient())
                {
                    if (wc == null) return null;
                    var json = wc.DownloadString("https://www.steamgriddb.com/api/v2/grids/game/" + Uri.EscapeDataString(steamGridDbId) + "?dimensions=600x900,342x482,660x930&types=static&nsfw=false&humor=false&limit=1");
                    var match = Regex.Match(json, "\"url\"\\s*:\\s*\"(?<u>(?:\\\\.|[^\"])*)\"");
                    if (!match.Success) return null;
                    var url = Regex.Unescape(match.Groups["u"].Value).Replace("\\/", "/");
                    var ext = Path.GetExtension(new Uri(url).AbsolutePath);
                    if (string.IsNullOrWhiteSpace(ext)) ext = ".png";
                    var target = Path.Combine(dependencies.CoversRoot, SafeCacheName(title) + ext);
                    wc.DownloadFile(url, target);
                    if (File.Exists(target) && new FileInfo(target).Length > 0) return target;
                }
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
