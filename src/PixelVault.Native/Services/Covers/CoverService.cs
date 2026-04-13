using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PixelVaultNative
{
    /// <summary>Steam, SteamGridDB, and RetroAchievements HTTP helpers; network entry points are <c>*Async</c> only.</summary>
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
        string CustomHeroPath(LibraryFolderInfo folder);
        void SaveCustomHero(LibraryFolderInfo folder, string sourcePath);
        void ClearCustomHero(LibraryFolderInfo folder);
        string CachedHeroPath(string title);
        /// <summary>Deletes downloaded <c>hero-*</c> cache files for <paramref name="title"/>; does not remove <c>custom-hero-*</c>.</summary>
        void PurgeCachedHeroDownloads(string title);
        Task<string> TryDownloadSteamGridDbHeroAsync(string title, string steamGridDbId, CancellationToken cancellationToken = default(CancellationToken));
        Task<string> TryDownloadSteamStoreHeaderHeroAsync(string title, string appId, CancellationToken cancellationToken = default(CancellationToken));
        /// <summary>Search RetroAchievements games by title for consoles that best match <paramref name="platformLabel"/>. Requires a web API key from Path Settings.</summary>
        Task<List<Tuple<string, string>>> SearchRetroAchievementsGameMatchesAsync(string title, string platformLabel, CancellationToken cancellationToken = default(CancellationToken));
    }

    sealed class CoverServiceDependencies
    {
        /// <summary>Optional; when set, custom cover save uses the seam instead of raw <see cref="File.Copy"/>.</summary>
        public IFileSystemService FileSystem;
        public string AppVersion;
        public string CoversRoot;
        public int RequestTimeoutMilliseconds;
        public Func<string> GetSteamGridDbApiToken;
        public Func<string> GetRetroAchievementsWebApiKey;
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

    /// <summary>Coalesces concurrent hero HTTP work for the same dedupe key (PV-PLN-RVW-001 Phase 2).</summary>
    internal static class HeroDownloadCoalesce
    {
        public static async Task<string> RunAsync(
            object gate,
            Dictionary<string, Task<string>> inflight,
            string key,
            Func<CancellationToken, Task<string>> inner,
            CancellationToken waitCancellation)
        {
            Task<string> run;
            lock (gate)
            {
                if (inflight.TryGetValue(key, out var existing)
                    && existing != null
                    && !existing.IsCompleted)
                {
                    run = existing;
                }
                else
                {
                    run = inner(CancellationToken.None);
                    inflight[key] = run;
                    _ = run.ContinueWith(
                        _ =>
                        {
                            lock (gate)
                            {
                                if (inflight.TryGetValue(key, out var cur) && ReferenceEquals(cur, run))
                                    inflight.Remove(key);
                            }
                        },
                        CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
                }
            }

            return await run.WaitAsync(waitCancellation).ConfigureAwait(false);
        }
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
        readonly object _retroAchievementsSearchLock = new object();
        readonly Dictionary<string, List<Tuple<string, string>>> _retroAchievementsSearchCache = new Dictionary<string, List<Tuple<string, string>>>(StringComparer.OrdinalIgnoreCase);
        List<Tuple<int, string>> _retroAchievementsConsolesCache;
        string _retroAchievementsConsolesCacheKey;

        readonly object _steamGridDbHeroDownloadCoalesceLock = new object();
        readonly Dictionary<string, Task<string>> _steamGridDbHeroDownloadsInFlight = new Dictionary<string, Task<string>>(StringComparer.OrdinalIgnoreCase);
        readonly object _steamStoreHeaderHeroDownloadCoalesceLock = new object();
        readonly Dictionary<string, Task<string>> _steamStoreHeaderHeroDownloadsInFlight = new Dictionary<string, Task<string>>(StringComparer.OrdinalIgnoreCase);

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

        string CurrentRetroAchievementsWebApiKey()
        {
            return dependencies.GetRetroAchievementsWebApiKey == null ? string.Empty : (dependencies.GetRetroAchievementsWebApiKey() ?? string.Empty).Trim();
        }

        TimeoutWebClient CreateRetroAchievementsWebClient(bool allowLargeJson, int timeoutMs)
        {
            var client = new TimeoutWebClient
            {
                Encoding = Encoding.UTF8,
                TimeoutMilliseconds = timeoutMs > 0 ? timeoutMs : dependencies.RequestTimeoutMilliseconds,
                MaxStringResponseBytes = allowLargeJson ? 0L : TimeoutWebClient.DefaultMaxStringResponseBytes
            };
            try
            {
                client.Headers[HttpRequestHeader.UserAgent] = "PixelVault/" + (dependencies.AppVersion ?? "1.0");
            }
            catch
            {
            }
            return client;
        }

        static int RetroAchievementsConsoleScoreForPlatform(string platformNormalized, string consoleName)
        {
            if (string.IsNullOrWhiteSpace(consoleName)) return 0;
            var p = (platformNormalized ?? string.Empty).Trim().ToLowerInvariant();
            var c = consoleName.ToLowerInvariant();
            if (c.IndexOf("event", StringComparison.Ordinal) >= 0) return 0;
            if (string.IsNullOrWhiteSpace(p) || string.Equals(p, "other", StringComparison.OrdinalIgnoreCase))
            {
                if (c.Contains("windows") && c.Contains("pc")) return 100;
                if (c.Contains("ms-dos") || c.Contains("dos")) return 80;
                return 0;
            }
            if (c.Contains(p) || p.Contains(c)) return 95;
            var score = 0;
            foreach (var token in Regex.Split(p, @"[^a-z0-9]+"))
            {
                if (token.Length < 2) continue;
                if (c.Contains(token)) score += 22;
            }
            if (score > 100) score = 100;
            var steamBoost = (p.Contains("steam") || p == "pc" || p.Contains("windows"))
                && (c.Contains("windows") || c.Contains("dos") || c.Contains("linux") || (c.Contains("pc") && c.Contains("win")));
            if (steamBoost) score = Math.Max(score, 88);
            return score;
        }

        IReadOnlyList<int> PickRetroAchievementsConsoleIds(IReadOnlyList<Tuple<int, string>> consoles, string platformLabel)
        {
            var normPlatform = NormalizeConsoleLabel(platformLabel);
            var ranked = (consoles ?? Array.Empty<Tuple<int, string>>())
                .Select(pair => new { Id = pair.Item1, Name = pair.Item2 ?? string.Empty, Score = RetroAchievementsConsoleScoreForPlatform(normPlatform, pair.Item2) })
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (ranked.Count > 0) return ranked.Take(6).Select(x => x.Id).ToList();
            var windows = (consoles ?? Array.Empty<Tuple<int, string>>()).FirstOrDefault(t =>
                (t.Item2 ?? string.Empty).IndexOf("windows", StringComparison.OrdinalIgnoreCase) >= 0
                && (t.Item2 ?? string.Empty).IndexOf("pc", StringComparison.OrdinalIgnoreCase) >= 0);
            if (windows != null) return new[] { windows.Item1 };
            return Array.Empty<int>();
        }

        async Task<List<Tuple<int, string>>> LoadRetroAchievementsConsolesAsync(string apiKey, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(apiKey)) return new List<Tuple<int, string>>();
            lock (_retroAchievementsSearchLock)
            {
                if (_retroAchievementsConsolesCache != null && string.Equals(_retroAchievementsConsolesCacheKey, apiKey, StringComparison.Ordinal))
                    return _retroAchievementsConsolesCache;
            }
            var list = new List<Tuple<int, string>>();
            using (var wc = CreateRetroAchievementsWebClient(false, Math.Max(dependencies.RequestTimeoutMilliseconds, 45000)))
            {
                var url = "https://retroachievements.org/API/API_GetConsoleIDs.php?y=" + Uri.EscapeDataString(apiKey) + "&g=1";
                var json = await wc.DownloadStringAsync(url, cancellationToken).ConfigureAwait(false);
                using (var doc = JsonDocument.Parse(json))
                {
                    if (doc.RootElement.ValueKind != JsonValueKind.Array) return list;
                    foreach (var row in doc.RootElement.EnumerateArray())
                    {
                        if (!TryGetJsonIntProperty(row, "ID", "id", out var id) || id <= 0) continue;
                        var name = TryGetJsonStringProperty(row, "Name", "name");
                        if (!string.IsNullOrWhiteSpace(name)) list.Add(Tuple.Create(id, name));
                    }
                }
            }
            lock (_retroAchievementsSearchLock)
            {
                _retroAchievementsConsolesCache = list;
                _retroAchievementsConsolesCacheKey = apiKey;
            }
            return list;
        }

        static bool RetroAchievementsTitleLooksLikeMatch(string queryNormalized, string gameTitle)
        {
            if (string.IsNullOrWhiteSpace(gameTitle)) return false;
            var q = queryNormalized ?? string.Empty;
            var t = gameTitle.Trim();
            if (q.Length == 0) return false;
            var tn = t.ToLowerInvariant();
            var qn = q.ToLowerInvariant();
            if (string.Equals(tn, qn, StringComparison.Ordinal)) return true;
            if (tn.IndexOf(qn, StringComparison.Ordinal) >= 0) return true;
            if (qn.IndexOf(tn, StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        static void TryAddRetroAchievementsSearchHit(
            int gameId,
            string title,
            string consoleName,
            string queryNormalized,
            List<RankedRaHit> scratch,
            ISet<string> seenIds)
        {
            if (gameId <= 0 || string.IsNullOrWhiteSpace(title)) return;
            var idText = gameId.ToString(CultureInfo.InvariantCulture);
            if (!seenIds.Add(idText)) return;
            if (!RetroAchievementsTitleLooksLikeMatch(queryNormalized, title)) return;
            var qn = (queryNormalized ?? string.Empty).Trim().ToLowerInvariant();
            var tn = title.Trim().ToLowerInvariant();
            var rank = 0;
            if (string.Equals(tn, qn, StringComparison.Ordinal)) rank = 1000;
            else if (tn.StartsWith(qn + " ", StringComparison.Ordinal) || tn.StartsWith(qn + ":", StringComparison.Ordinal)) rank = 800;
            else if (tn.IndexOf(qn, StringComparison.Ordinal) >= 0) rank = 500;
            else rank = 200;
            var label = title.Trim();
            if (!string.IsNullOrWhiteSpace(consoleName)) label = label + " · " + consoleName.Trim();
            scratch.Add(new RankedRaHit { GameId = idText, Label = label, Rank = rank });
        }

        sealed class RankedRaHit
        {
            public string GameId;
            public string Label;
            public int Rank;
        }

        static bool TryGetJsonIntProperty(JsonElement obj, string a, string b, out int value)
        {
            value = 0;
            JsonElement el;
            if (!obj.TryGetProperty(a, out el) && !obj.TryGetProperty(b, out el)) return false;
            if (el.ValueKind == JsonValueKind.Number) { value = el.GetInt32(); return true; }
            if (el.ValueKind == JsonValueKind.String) return int.TryParse(el.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
            return false;
        }

        static string TryGetJsonStringProperty(JsonElement obj, string a, string b)
        {
            JsonElement el;
            if (!obj.TryGetProperty(a, out el) && !obj.TryGetProperty(b, out el)) return string.Empty;
            return el.ValueKind == JsonValueKind.String ? el.GetString() : string.Empty;
        }

        static void ParseRetroAchievementsGameListPage(string json, string queryNormalized, string defaultConsoleName, List<RankedRaHit> scratch, ISet<string> seenIds)
        {
            if (string.IsNullOrWhiteSpace(json)) return;
            using (var doc = JsonDocument.Parse(json))
            {
                if (doc.RootElement.ValueKind != JsonValueKind.Array) return;
                foreach (var row in doc.RootElement.EnumerateArray())
                {
                    if (!TryGetJsonIntProperty(row, "ID", "id", out var gid) || gid <= 0) continue;
                    var title = TryGetJsonStringProperty(row, "Title", "title");
                    var consoleName = TryGetJsonStringProperty(row, "ConsoleName", "consoleName");
                    if (string.IsNullOrWhiteSpace(consoleName)) consoleName = TryGetJsonStringProperty(row, "Console", "console");
                    if (string.IsNullOrWhiteSpace(consoleName)) consoleName = defaultConsoleName;
                    TryAddRetroAchievementsSearchHit(gid, title, consoleName, queryNormalized, scratch, seenIds);
                }
            }
        }

        public async Task<List<Tuple<string, string>>> SearchRetroAchievementsGameMatchesAsync(string title, string platformLabel, CancellationToken cancellationToken = default(CancellationToken))
        {
            var query = (title ?? string.Empty).Trim();
            var results = new List<Tuple<string, string>>();
            if (string.IsNullOrWhiteSpace(query)) return results;
            var apiKey = CurrentRetroAchievementsWebApiKey();
            if (string.IsNullOrWhiteSpace(apiKey)) return results;

            cancellationToken.ThrowIfCancellationRequested();
            var cacheKey = query + "\n" + NormalizeConsoleLabel(platformLabel ?? string.Empty);
            lock (_retroAchievementsSearchLock)
            {
                List<Tuple<string, string>> cached;
                if (_retroAchievementsSearchCache.TryGetValue(cacheKey, out cached))
                    return cached == null ? new List<Tuple<string, string>>() : new List<Tuple<string, string>>(cached);
            }

            var queryNorm = NormalizeTitle(query);
            const int pageSize = 2000;
            const int maxPagesPerConsole = 12;
            const int maxHits = 24;
            var stopwatch = Stopwatch.StartNew();
            try
            {
                var consoles = await LoadRetroAchievementsConsolesAsync(apiKey, cancellationToken).ConfigureAwait(false);
                var consoleIds = PickRetroAchievementsConsoleIds(consoles, platformLabel ?? string.Empty);
                if (consoleIds.Count == 0)
                {
                    Log("RetroAchievements search: no console matched platform \"" + (platformLabel ?? string.Empty) + "\".");
                    lock (_retroAchievementsSearchLock)
                    {
                        _retroAchievementsSearchCache[cacheKey] = results;
                    }
                    return results;
                }
                var consoleNameById = consoles.ToDictionary(t => t.Item1, t => t.Item2 ?? string.Empty);
                var scratch = new List<RankedRaHit>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                using (var wc = CreateRetroAchievementsWebClient(true, Math.Max(dependencies.RequestTimeoutMilliseconds, 180000)))
                {
                    foreach (var consoleId in consoleIds)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var defaultName = string.Empty;
                        string mapName;
                        if (consoleNameById.TryGetValue(consoleId, out mapName)) defaultName = mapName;
                        for (var page = 0; page < maxPagesPerConsole && scratch.Count < maxHits * 4; page++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            var offset = page * pageSize;
                            var url = "https://retroachievements.org/API/API_GetGameList.php?y=" + Uri.EscapeDataString(apiKey)
                                + "&i=" + consoleId.ToString(CultureInfo.InvariantCulture)
                                + "&f=1&c=" + pageSize.ToString(CultureInfo.InvariantCulture)
                                + "&o=" + offset.ToString(CultureInfo.InvariantCulture);
                            string json;
                            try
                            {
                                json = await wc.DownloadStringAsync(url, cancellationToken).ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                Log("RetroAchievements game list failed for console " + consoleId + " offset " + offset + ". " + ex.Message);
                                break;
                            }
                            var trimmed = json == null ? string.Empty : json.Trim();
                            if (trimmed.Length == 0 || trimmed == "[]" || trimmed == "{\"success\":false}")
                                break;
                            ParseRetroAchievementsGameListPage(json, queryNorm, defaultName, scratch, seen);
                        }
                    }
                }
                foreach (var hit in scratch.OrderByDescending(h => h.Rank).ThenBy(h => h.Label, StringComparer.OrdinalIgnoreCase).Take(maxHits))
                {
                    results.Add(Tuple.Create(hit.GameId, hit.Label));
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log("RetroAchievements search failed for \"" + query + "\". " + ex.Message);
            }
            finally
            {
                stopwatch.Stop();
                LogPerformanceSample("RetroAchievementsSearch", stopwatch, "title=" + query + "; platform=" + (platformLabel ?? string.Empty), 300);
                lock (_retroAchievementsSearchLock)
                {
                    _retroAchievementsSearchCache[cacheKey] = results;
                }
            }
            return results;
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
            foreach (var ext in new[] { ".jpg", ".jpeg", ".png", ".jxr", ".bmp", ".gif" })
            {
                var path = Path.Combine(dependencies.CoversRoot, "custom-" + key + ext);
                if (File.Exists(path)) return path;
            }
            return null;
        }

        static string HeroCacheFileBase(string safeTitleBase)
        {
            return "hero-" + safeTitleBase;
        }

        string HeroCacheFileBaseFromTitle(string title)
        {
            return HeroCacheFileBase(SafeCacheName(title));
        }

        public string CustomHeroPath(LibraryFolderInfo folder)
        {
            var key = CustomCoverKey(folder);
            if (string.IsNullOrWhiteSpace(key)) return null;
            foreach (var ext in new[] { ".jpg", ".jpeg", ".png", ".jxr", ".bmp", ".gif" })
            {
                var path = Path.Combine(dependencies.CoversRoot, "custom-hero-" + key + ext);
                if (File.Exists(path)) return path;
            }
            return null;
        }

        public void SaveCustomHero(LibraryFolderInfo folder, string sourcePath)
        {
            if (folder == null || string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath)) return;
            var key = CustomCoverKey(folder);
            if (string.IsNullOrWhiteSpace(key)) return;
            Directory.CreateDirectory(dependencies.CoversRoot);
            var invalidated = new List<string>();
            foreach (var ext in new[] { ".jpg", ".jpeg", ".png", ".jxr", ".bmp", ".gif" })
            {
                var existing = Path.Combine(dependencies.CoversRoot, "custom-hero-" + key + ext);
                if (File.Exists(existing))
                {
                    invalidated.Add(existing);
                    File.Delete(existing);
                }
            }
            var extension = Path.GetExtension(sourcePath);
            if (string.IsNullOrWhiteSpace(extension)) extension = ".png";
            var target = Path.Combine(dependencies.CoversRoot, "custom-hero-" + key + extension.ToLowerInvariant());
            if (dependencies.FileSystem != null) dependencies.FileSystem.CopyFile(sourcePath, target, true);
            else File.Copy(sourcePath, target, true);
            invalidated.Add(target);
            InvalidateCoverImageCache(invalidated);
        }

        public void ClearCustomHero(LibraryFolderInfo folder)
        {
            var key = CustomCoverKey(folder);
            if (string.IsNullOrWhiteSpace(key)) return;
            var invalidated = new List<string>();
            foreach (var ext in new[] { ".jpg", ".jpeg", ".png", ".jxr", ".bmp", ".gif" })
            {
                var existing = Path.Combine(dependencies.CoversRoot, "custom-hero-" + key + ext);
                if (File.Exists(existing))
                {
                    invalidated.Add(existing);
                    File.Delete(existing);
                }
            }
            InvalidateCoverImageCache(invalidated);
        }

        public string CachedHeroPath(string title)
        {
            var safe = HeroCacheFileBaseFromTitle(title);
            foreach (var ext in new[] { ".jpg", ".jpeg", ".png", ".jxr" })
            {
                var path = Path.Combine(dependencies.CoversRoot, safe + ext);
                if (File.Exists(path)) return path;
            }
            return null;
        }

        public void PurgeCachedHeroDownloads(string title)
        {
            var safe = HeroCacheFileBaseFromTitle(title);
            var invalidated = new List<string>();
            foreach (var ext in new[] { ".jpg", ".jpeg", ".png", ".jxr", ".bmp", ".gif" })
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

        public void SaveCustomCover(LibraryFolderInfo folder, string sourcePath)
        {
            if (folder == null || string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath)) return;
            var key = CustomCoverKey(folder);
            if (string.IsNullOrWhiteSpace(key)) return;
            Directory.CreateDirectory(dependencies.CoversRoot);
            var invalidated = new List<string>();
            foreach (var ext in new[] { ".jpg", ".jpeg", ".png", ".jxr", ".bmp", ".gif" })
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
            foreach (var ext in new[] { ".jpg", ".jpeg", ".png", ".jxr", ".bmp", ".gif" })
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
            foreach (var ext in new[] { ".jpg", ".jpeg", ".png", ".jxr" })
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
            foreach (var ext in new[] { ".jpg", ".jpeg", ".png", ".jxr" })
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

        /// <summary>
        /// SteamGridDB <b>Heroes</b> (wide banner art — same category as <c>https://www.steamgriddb.com/hero/</c>… on the site), not portrait grids or Steam store capsules.
        /// </summary>
        public async Task<string> TryDownloadSteamGridDbHeroAsync(string title, string steamGridDbId, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(steamGridDbId) || !HasSteamGridDbApiToken()) return null;
            cancellationToken.ThrowIfCancellationRequested();
            var key = SafeCacheName(title) + "\u001f" + steamGridDbId.Trim();
            return await HeroDownloadCoalesce.RunAsync(
                _steamGridDbHeroDownloadCoalesceLock,
                _steamGridDbHeroDownloadsInFlight,
                key,
                ct => TryDownloadSteamGridDbHeroWorkAsync(title, steamGridDbId, ct),
                cancellationToken).ConfigureAwait(false);
        }

        async Task<string> TryDownloadSteamGridDbHeroWorkAsync(string title, string steamGridDbId, CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                Directory.CreateDirectory(dependencies.CoversRoot);
                using (var wc = CreateSteamGridDbWebClient())
                {
                    if (wc == null) return null;
                    var json = await wc.DownloadStringAsync(
                        "https://www.steamgriddb.com/api/v2/heroes/game/" + Uri.EscapeDataString(steamGridDbId) + "?dimensions=3840x1240,1920x620,1600x650,930x310&types=static,alternate&nsfw=false&humor=false&limit=1",
                        cancellationToken).ConfigureAwait(false);
                    var match = Regex.Match(json, "\"url\"\\s*:\\s*\"(?<u>(?:\\\\.|[^\"])*)\"");
                    if (!match.Success) return null;
                    var url = Regex.Unescape(match.Groups["u"].Value).Replace("\\/", "/");
                    var ext = Path.GetExtension(new Uri(url).AbsolutePath);
                    if (string.IsNullOrWhiteSpace(ext)) ext = ".png";
                    PurgeCachedHeroDownloads(title);
                    var target = Path.Combine(dependencies.CoversRoot, HeroCacheFileBaseFromTitle(title) + ext);
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
                Log("SteamGridDB hero download failed for " + title + ". " + ex.Message);
            }
            finally
            {
                stopwatch.Stop();
                LogPerformanceSample("SteamGridDbHeroDownload", stopwatch, "title=" + title + "; stid=" + steamGridDbId, 180);
            }
            return null;
        }

        /// <summary>
        /// Valve CDN / store fallback when <see cref="TryDownloadSteamGridDbHeroAsync"/> is unavailable — not the SteamGridDB Heroes category.
        /// </summary>
        public async Task<string> TryDownloadSteamStoreHeaderHeroAsync(string title, string appId, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(appId)) return null;
            cancellationToken.ThrowIfCancellationRequested();
            var key = SafeCacheName(title) + "\u001f" + appId.Trim();
            return await HeroDownloadCoalesce.RunAsync(
                _steamStoreHeaderHeroDownloadCoalesceLock,
                _steamStoreHeaderHeroDownloadsInFlight,
                key,
                ct => TryDownloadSteamStoreHeaderHeroWorkAsync(title, appId, ct),
                cancellationToken).ConfigureAwait(false);
        }

        async Task<string> TryDownloadSteamStoreHeaderHeroWorkAsync(string title, string appId, CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                Directory.CreateDirectory(dependencies.CoversRoot);
                using (var wc = CreateSteamWebClient())
                {
                    PurgeCachedHeroDownloads(title);

                    // Steam library_hero JPGs, then store header_image — runs only after SteamGridDB Heroes in the photo-banner resolver.
                    var libraryHeroUrls = new[]
                    {
                        "https://shared.cloudflare.steamstatic.com/store_item_assets/steam/apps/" + appId + "/library_hero_2x.jpg",
                        "https://shared.cloudflare.steamstatic.com/store_item_assets/steam/apps/" + appId + "/library_hero.jpg",
                        "https://cdn.cloudflare.steamstatic.com/steam/apps/" + appId + "/library_hero_2x.jpg",
                        "https://cdn.cloudflare.steamstatic.com/steam/apps/" + appId + "/library_hero.jpg"
                    };
                    foreach (var heroUrl in libraryHeroUrls)
                    {
                        try
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            var ext = Path.GetExtension(new Uri(heroUrl).AbsolutePath);
                            if (string.IsNullOrWhiteSpace(ext)) ext = ".jpg";
                            var target = Path.Combine(dependencies.CoversRoot, HeroCacheFileBaseFromTitle(title) + ext);
                            await wc.DownloadFileAsync(heroUrl, target, cancellationToken).ConfigureAwait(false);
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
                    var headerExt = Path.GetExtension(new Uri(url).AbsolutePath);
                    if (string.IsNullOrWhiteSpace(headerExt)) headerExt = ".jpg";
                    PurgeCachedHeroDownloads(title);
                    var headerTarget = Path.Combine(dependencies.CoversRoot, HeroCacheFileBaseFromTitle(title) + headerExt);
                    await wc.DownloadFileAsync(url, headerTarget, cancellationToken).ConfigureAwait(false);
                    return File.Exists(headerTarget) && new FileInfo(headerTarget).Length > 0 ? headerTarget : null;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log("Steam store library hero / header download failed for " + title + ". " + ex.Message);
            }
            finally
            {
                stopwatch.Stop();
                LogPerformanceSample("SteamStoreHeaderHeroDownload", stopwatch, "title=" + title + "; appId=" + appId, 180);
            }
            return null;
        }
    }
}
