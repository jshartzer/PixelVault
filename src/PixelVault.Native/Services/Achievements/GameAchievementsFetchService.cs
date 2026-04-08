using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PixelVaultNative
{
    static class GameAchievementsFetchService
    {
        public sealed class AchievementRow
        {
            public string Title;
            public string Description;
            public string Meta;
            public int SortKey;
            public bool ProgressKnown;
            public bool Unlocked;
            public long UnlockUtcTicks;
            /// <summary>Preferred color icon URL (badge / Steam unlocked art).</summary>
            public string IconUrlColor;
            /// <summary>Steam locked icon hash URL when available; otherwise empty (UI may grayscale <see cref="IconUrlColor"/>).</summary>
            public string IconUrlGray;
        }

        public sealed class FetchResult
        {
            public string SourceLabel;
            public string GameTitle;
            public string DetailLine;
            public List<AchievementRow> Rows;
            public string ErrorMessage;

            public bool IsError => !string.IsNullOrEmpty(ErrorMessage);
        }

        public static async Task<FetchResult> FetchAsync(
            string normalizedPlatform,
            LibraryFolderInfo folder,
            string steamWebApiKey,
            string retroAchievementsApiKey,
            string steamUserId64,
            string retroAchievementsUsername,
            string userAgent,
            CancellationToken cancellationToken)
        {
            var folderName = folder == null ? string.Empty : (folder.Name ?? string.Empty).Trim();
            var norm = (normalizedPlatform ?? string.Empty).Trim();
            if (string.Equals(norm, "Steam", StringComparison.OrdinalIgnoreCase))
                return await FetchSteamAsync(folder, folderName, steamWebApiKey, steamUserId64, userAgent, cancellationToken).ConfigureAwait(false);
            if (string.Equals(norm, "Emulation", StringComparison.OrdinalIgnoreCase))
                return await FetchRetroAsync(folder, folderName, retroAchievementsApiKey, retroAchievementsUsername, userAgent, cancellationToken).ConfigureAwait(false);

            return new FetchResult
            {
                ErrorMessage = "Achievements load for games tagged Steam (Steam Community schema) or Emulation (RetroAchievements). Use Edit Metadata to set the platform and the matching id."
            };
        }

        static FetchResult Err(string msg) => new FetchResult { ErrorMessage = msg };

        static bool TryParsePositiveInt(string raw, out int id)
        {
            id = 0;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            var t = raw.Trim();
            if (int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out id) && id > 0) return true;
            for (var i = 0; i < t.Length; i++)
            {
                if (!char.IsDigit(t[i])) continue;
                var j = i;
                while (j < t.Length && char.IsDigit(t[j])) j++;
                if (int.TryParse(t.Substring(i, j - i), NumberStyles.Integer, CultureInfo.InvariantCulture, out id) && id > 0)
                    return true;
                i = j;
            }
            return false;
        }

        static bool TryParseULong(string raw, out ulong id)
        {
            id = 0;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            return ulong.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out id) && id > 0UL;
        }

        static string BuildSteamAchievementIconUrl(int appId, string hash)
        {
            if (string.IsNullOrWhiteSpace(hash)) return null;
            var h = hash.Trim();
            return "https://cdn.akamai.steamstatic.com/steamcommunity/public/images/apps/"
                + appId.ToString(CultureInfo.InvariantCulture) + "/" + Uri.EscapeDataString(h) + ".jpg";
        }

        static async Task<Dictionary<string, (int achieved, int unlocktime)>> TryLoadSteamPlayerAchievementsAsync(
            int appId,
            string steamWebApiKey,
            ulong steamId64,
            string userAgent,
            CancellationToken cancellationToken)
        {
            var url = "https://api.steampowered.com/ISteamUserStats/GetPlayerAchievements/v0001/?key=" + Uri.EscapeDataString(steamWebApiKey.Trim())
                + "&appid=" + appId.ToString(CultureInfo.InvariantCulture)
                + "&steamid=" + steamId64.ToString(CultureInfo.InvariantCulture)
                + "&l=english";

            string json;
            using (var wc = new TimeoutWebClient
            {
                Encoding = Encoding.UTF8,
                TimeoutMilliseconds = 25000,
                MaxStringResponseBytes = TimeoutWebClient.DefaultMaxStringResponseBytes
            })
            {
                TrySetUa(wc, userAgent);
                try
                {
                    json = await wc.DownloadStringAsync(url, cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    return null;
                }
            }

            try
            {
                using (var doc = JsonDocument.Parse(json))
                {
                    var root = doc.RootElement;
                    if (!root.TryGetProperty("playerstats", out var ps))
                        return null;
                    if (ps.TryGetProperty("error", out var errEl) && errEl.ValueKind == JsonValueKind.String)
                    {
                        var err = errEl.GetString();
                        if (!string.IsNullOrWhiteSpace(err)) return null;
                    }
                    if (!ps.TryGetProperty("achievements", out var arr) || arr.ValueKind != JsonValueKind.Array)
                        return new Dictionary<string, (int, int)>(StringComparer.OrdinalIgnoreCase);

                    var map = new Dictionary<string, (int achieved, int unlocktime)>(StringComparer.OrdinalIgnoreCase);
                    foreach (var row in arr.EnumerateArray())
                    {
                        var apiName = ReadString(row, "apiname", "name");
                        if (string.IsNullOrWhiteSpace(apiName)) continue;
                        var achieved = 0;
                        if (row.TryGetProperty("achieved", out var ach))
                        {
                            if (ach.ValueKind == JsonValueKind.Number && ach.TryGetInt32(out var ai)) achieved = ai;
                            else if (ach.ValueKind == JsonValueKind.True) achieved = 1;
                        }
                        var unlock = 0;
                        if (row.TryGetProperty("unlocktime", out var ut) && ut.ValueKind == JsonValueKind.Number && ut.TryGetInt32(out var uti))
                            unlock = uti;
                        map[apiName.Trim()] = (achieved, unlock);
                    }
                    return map;
                }
            }
            catch (JsonException)
            {
                return null;
            }
        }

        static long UnlockTicksFromUnix(int unix)
        {
            if (unix <= 0) return 0;
            try
            {
                return DateTimeOffset.FromUnixTimeSeconds(unix).UtcTicks;
            }
            catch
            {
                return 0;
            }
        }

        static async Task<FetchResult> FetchSteamAsync(
            LibraryFolderInfo folder,
            string fallbackTitle,
            string steamWebApiKey,
            string steamUserId64Raw,
            string userAgent,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(steamWebApiKey))
                return Err("Add a Steam Web API key in Settings (Paths), or set PIXELVAULT_STEAM_WEB_API_KEY / STEAM_WEB_API_KEY.");
            if (folder == null || !TryParsePositiveInt(folder.SteamAppId, out var appId))
                return Err("This Steam game needs a Steam App ID. Use Edit Metadata to set it.");

            var url = "https://api.steampowered.com/ISteamUserStats/GetSchemaForGame/v2/?key=" + Uri.EscapeDataString(steamWebApiKey.Trim())
                + "&appid=" + appId.ToString(CultureInfo.InvariantCulture);

            string json;
            using (var wc = new TimeoutWebClient
            {
                Encoding = Encoding.UTF8,
                TimeoutMilliseconds = 25000,
                MaxStringResponseBytes = TimeoutWebClient.DefaultMaxStringResponseBytes
            })
            {
                TrySetUa(wc, userAgent);
                try
                {
                    json = await wc.DownloadStringAsync(url, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    return Err("Steam request failed: " + ex.Message);
                }
            }

            Dictionary<string, (int achieved, int unlocktime)> progress = null;
            var progressAttempted = false;
            if (TryParseULong(steamUserId64Raw, out var sid64))
            {
                progressAttempted = true;
                progress = await TryLoadSteamPlayerAchievementsAsync(appId, steamWebApiKey, sid64, userAgent, cancellationToken).ConfigureAwait(false);
            }

            try
            {
                using (var doc = JsonDocument.Parse(json))
                {
                    var root = doc.RootElement;
                    if (!root.TryGetProperty("game", out var game))
                        return Err("Unexpected Steam response (missing game).");

                    var gameTitle = fallbackTitle;
                    if (game.TryGetProperty("gameName", out var gn) && gn.ValueKind == JsonValueKind.String)
                    {
                        var n = gn.GetString();
                        if (!string.IsNullOrWhiteSpace(n)) gameTitle = n.Trim();
                    }

                    var rows = new List<AchievementRow>();
                    if (game.TryGetProperty("availableGameStats", out var stats)
                        && stats.TryGetProperty("achievements", out var ach)
                        && ach.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var a in ach.EnumerateArray())
                        {
                            var apiName = ReadString(a, "name") ?? string.Empty;
                            var title = ReadString(a, "displayName", "name");
                            if (string.IsNullOrWhiteSpace(title))
                                title = string.IsNullOrWhiteSpace(apiName) ? "(achievement)" : apiName;
                            var desc = ReadString(a, "description") ?? string.Empty;
                            var hidden = false;
                            if (a.TryGetProperty("hidden", out var h))
                            {
                                if (h.ValueKind == JsonValueKind.Number && h.TryGetInt32(out var hv)) hidden = hv != 0;
                                else if (h.ValueKind == JsonValueKind.True) hidden = true;
                            }

                            var icon = ReadString(a, "icon") ?? string.Empty;
                            var iconGray = ReadString(a, "icongray") ?? string.Empty;
                            var colorUrl = BuildSteamAchievementIconUrl(appId, icon);
                            var grayUrl = BuildSteamAchievementIconUrl(appId, iconGray);

                            var progressKnown = progress != null;
                            var unlocked = false;
                            var unlockTicks = 0L;
                            if (progress != null && !string.IsNullOrWhiteSpace(apiName)
                                && progress.TryGetValue(apiName.Trim(), out var pr))
                            {
                                unlocked = pr.achieved != 0;
                                unlockTicks = UnlockTicksFromUnix(pr.unlocktime);
                            }
                            else if (progress != null)
                            {
                                unlocked = false;
                            }

                            var metaParts = new List<string>();
                            if (hidden) metaParts.Add("Hidden");
                            if (progressKnown)
                                metaParts.Add(unlocked ? "Unlocked" : "Locked");

                            rows.Add(new AchievementRow
                            {
                                Title = title.Trim(),
                                Description = desc.Trim(),
                                Meta = string.Join(" · ", metaParts.Where(s => !string.IsNullOrWhiteSpace(s))),
                                SortKey = 0,
                                ProgressKnown = progressKnown,
                                Unlocked = unlocked,
                                UnlockUtcTicks = unlockTicks,
                                IconUrlColor = colorUrl ?? string.Empty,
                                IconUrlGray = grayUrl ?? string.Empty
                            });
                        }
                    }

                    var ordered = OrderAchievementRows(rows);
                    var unlockedCount = ordered.Count(r => r.ProgressKnown && r.Unlocked);
                    var detail = "App " + appId.ToString(CultureInfo.InvariantCulture)
                        + " · " + ordered.Count.ToString(CultureInfo.InvariantCulture) + " achievements";
                    if (progress != null)
                        detail += " · " + unlockedCount.ToString(CultureInfo.InvariantCulture) + " unlocked";
                    else if (progressAttempted)
                        detail += " · unlock status unavailable (check SteamID64 or game/profile privacy)";
                    else
                        detail += " · add SteamID64 in Path Settings for unlock status";

                    return new FetchResult
                    {
                        SourceLabel = "Steam",
                        GameTitle = gameTitle,
                        DetailLine = detail,
                        Rows = ordered
                    };
                }
            }
            catch (JsonException)
            {
                return Err("Could not read Steam achievement data (invalid JSON).");
            }
        }

        static List<AchievementRow> OrderAchievementRows(List<AchievementRow> rows)
        {
            if (rows == null || rows.Count == 0) return rows ?? new List<AchievementRow>();
            var unlocked = rows.Where(r => r.ProgressKnown && r.Unlocked)
                .OrderByDescending(r => r.UnlockUtcTicks)
                .ThenBy(r => r.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var locked = rows.Where(r => r.ProgressKnown && !r.Unlocked)
                .OrderBy(r => r.SortKey)
                .ThenBy(r => r.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var unknown = rows.Where(r => !r.ProgressKnown)
                .OrderBy(r => r.SortKey)
                .ThenBy(r => r.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var all = new List<AchievementRow>(rows.Count);
            all.AddRange(unlocked);
            all.AddRange(locked);
            all.AddRange(unknown);
            return all;
        }

        static string BuildRetroBadgeUrl(string badgeName)
        {
            if (string.IsNullOrWhiteSpace(badgeName)) return null;
            return "https://media.retroachievements.org/Badge/" + Uri.EscapeDataString(badgeName.Trim()) + ".png";
        }

        static bool TryParseRaDate(string raw, out long utcTicks)
        {
            utcTicks = 0;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            if (DateTime.TryParse(raw.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
            {
                utcTicks = dt.Ticks;
                return true;
            }
            if (DateTime.TryParse(raw.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
            {
                utcTicks = dt.ToUniversalTime().Ticks;
                return true;
            }
            return false;
        }

        static void ParseRaAchievementObject(JsonElement o, string fallbackKey, bool userProgressMode, List<AchievementRow> rows)
        {
            var title = ReadString(o, "Title", "title");
            if (string.IsNullOrWhiteSpace(title)) title = string.IsNullOrWhiteSpace(fallbackKey) ? "(achievement)" : fallbackKey;
            var desc = ReadString(o, "Description", "description") ?? string.Empty;
            var points = ReadIntString(o, "Points", "points");
            var metaPoints = string.IsNullOrWhiteSpace(points) ? string.Empty : points + " pts";
            var order = ReadDisplayOrder(o);
            var badge = ReadString(o, "BadgeName", "badgeName") ?? string.Empty;
            var badgeUrl = BuildRetroBadgeUrl(badge);

            var earnedRaw = ReadString(o, "DateEarned", "dateEarned")
                ?? ReadString(o, "DateEarnedHardcore", "dateEarnedHardcore");
            long ticks = 0;
            var unlocked = userProgressMode && !string.IsNullOrWhiteSpace(earnedRaw) && TryParseRaDate(earnedRaw, out ticks);
            var progressKnown = userProgressMode;

            var metaParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(metaPoints)) metaParts.Add(metaPoints);
            if (userProgressMode)
                metaParts.Add(unlocked ? "Unlocked" : "Locked");

            rows.Add(new AchievementRow
            {
                Title = title.Trim(),
                Description = desc.Trim(),
                Meta = string.Join(" · ", metaParts.Where(s => !string.IsNullOrWhiteSpace(s))),
                SortKey = order,
                ProgressKnown = progressKnown,
                Unlocked = unlocked,
                UnlockUtcTicks = ticks,
                IconUrlColor = badgeUrl ?? string.Empty,
                IconUrlGray = string.Empty
            });
        }

        static async Task<FetchResult> FetchRetroAsync(
            LibraryFolderInfo folder,
            string fallbackTitle,
            string apiKey,
            string retroUsername,
            string userAgent,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                return Err("Add a RetroAchievements API key in Settings (Paths), or set PIXELVAULT_RETROACHIEVEMENTS_API_KEY / RETROACHIEVEMENTS_API_KEY.");
            if (folder == null || !TryParsePositiveInt(folder.RetroAchievementsGameId, out var gameId))
                return Err("This game needs a RetroAchievements game id. Use Edit Metadata to set it.");

            var useUser = !string.IsNullOrWhiteSpace(retroUsername);
            var url = useUser
                ? "https://retroachievements.org/API/API_GetGameInfoAndUserProgress.php?y=" + Uri.EscapeDataString(apiKey.Trim())
                    + "&u=" + Uri.EscapeDataString(retroUsername.Trim())
                    + "&g=" + gameId.ToString(CultureInfo.InvariantCulture)
                : "https://retroachievements.org/API/API_GetGameExtended.php?y=" + Uri.EscapeDataString(apiKey.Trim())
                    + "&i=" + gameId.ToString(CultureInfo.InvariantCulture);

            string json;
            using (var wc = new TimeoutWebClient
            {
                Encoding = Encoding.UTF8,
                TimeoutMilliseconds = 25000,
                MaxStringResponseBytes = 0
            })
            {
                TrySetUa(wc, userAgent);
                try
                {
                    json = await wc.DownloadStringAsync(url, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    return Err("RetroAchievements request failed: " + ex.Message);
                }
            }

            try
            {
                using (var doc = JsonDocument.Parse(json))
                {
                    var root = doc.RootElement;
                    if (root.TryGetProperty("Success", out var succ) && succ.ValueKind == JsonValueKind.False)
                        return Err(ReadTopLevelError(root) ?? "RetroAchievements returned an error for this game id.");

                    var gameTitle = ReadString(root, "Title", "title");
                    if (string.IsNullOrWhiteSpace(gameTitle)) gameTitle = fallbackTitle;

                    var rows = new List<AchievementRow>();
                    if (!root.TryGetProperty("Achievements", out var ach) && !root.TryGetProperty("achievements", out ach))
                    {
                        return new FetchResult
                        {
                            SourceLabel = "RetroAchievements",
                            GameTitle = gameTitle ?? fallbackTitle,
                            DetailLine = "Game " + gameId.ToString(CultureInfo.InvariantCulture) + " · 0 achievements"
                                + (useUser ? string.Empty : " · add RetroAchievements username in Path Settings for unlock status"),
                            Rows = rows
                        };
                    }

                    if (ach.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var p in ach.EnumerateObject())
                            ParseRaAchievementObject(p.Value, p.Name, useUser, rows);
                    }
                    else if (ach.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var o in ach.EnumerateArray())
                            ParseRaAchievementObject(o, string.Empty, useUser, rows);
                    }

                    var ordered = OrderAchievementRows(rows);
                    var unlockedCount = useUser ? ordered.Count(x => x.ProgressKnown && x.Unlocked) : 0;
                    var detail = "Game " + gameId.ToString(CultureInfo.InvariantCulture)
                        + " · " + ordered.Count.ToString(CultureInfo.InvariantCulture) + " achievements";
                    if (useUser)
                        detail += " · " + unlockedCount.ToString(CultureInfo.InvariantCulture) + " unlocked";
                    else
                        detail += " · add RetroAchievements username in Path Settings for unlock status";

                    return new FetchResult
                    {
                        SourceLabel = "RetroAchievements",
                        GameTitle = gameTitle ?? fallbackTitle,
                        DetailLine = detail,
                        Rows = ordered
                    };
                }
            }
            catch (JsonException)
            {
                return Err("Could not read RetroAchievements data (invalid JSON).");
            }
        }

        static int ReadDisplayOrder(JsonElement o)
        {
            if (o.TryGetProperty("DisplayOrder", out var el)) return ReadIntLoose(el);
            if (o.TryGetProperty("displayOrder", out el)) return ReadIntLoose(el);
            return int.MaxValue;
        }

        static int ReadIntLoose(JsonElement el)
        {
            if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var i)) return i;
            if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out i))
                return i;
            return int.MaxValue;
        }

        static string ReadTopLevelError(JsonElement root)
        {
            return ReadString(root, "Error", "error");
        }

        static string ReadString(JsonElement o, params string[] names)
        {
            foreach (var n in names)
            {
                if (string.IsNullOrEmpty(n)) continue;
                if (!o.TryGetProperty(n, out var el)) continue;
                if (el.ValueKind == JsonValueKind.String) return el.GetString();
            }
            return null;
        }

        static string ReadIntString(JsonElement o, params string[] names)
        {
            foreach (var n in names)
            {
                if (string.IsNullOrEmpty(n)) continue;
                if (!o.TryGetProperty(n, out var el)) continue;
                if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var i))
                    return i.ToString(CultureInfo.InvariantCulture);
                if (el.ValueKind == JsonValueKind.String) return el.GetString();
            }
            return null;
        }

        static void TrySetUa(TimeoutWebClient wc, string userAgent)
        {
            try
            {
                wc.Headers[HttpRequestHeader.UserAgent] = string.IsNullOrWhiteSpace(userAgent) ? "PixelVault" : userAgent.Trim();
            }
            catch
            {
            }
        }
    }
}
