using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PixelVaultNative
{
    /// <summary>Current Roblox desktop captures only; the obsolete RobloxScreenShot date format is excluded.</summary>
    internal static class RobloxCaptureNaming
    {
        internal const string RawConvention = "roblox_capture_ids";
        internal const string NamedConvention = "roblox_capture_named";
        internal const string GameTitle = "Roblox";
        internal const string RawPattern = @"^[1-9][0-9]{0,18}_(?<placeId>[1-9][0-9]{0,18})_(?<capture>[0-9]{13})\.png$";
        // Keep recognizing captures named by the previous experience-first importer.
        internal const string NamedPattern = @"^(?:Roblox_(?<title>.+)|(?<title>.+)_Roblox)_(?<capture>[0-9]{13})(?: \([0-9]+\)|[_-][0-9]+)?\.png$";

        internal static bool IsCapture(FilenameParseResult parsed) => parsed != null
            && (parsed.ConventionId == RawConvention || parsed.ConventionId == NamedConvention);

        internal static string GetExperienceName(string file)
        {
            var match = Regex.Match(Path.GetFileName(file ?? string.Empty), NamedPattern,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            return match.Success ? match.Groups["title"].Value.Trim() : string.Empty;
        }

        internal static bool TryBuildRenameBase(string file, string title, out string renamed)
        {
            renamed = null;
            var match = Regex.Match(Path.GetFileName(file ?? string.Empty), RawPattern,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            var safeTitle = TextAndPathHelpers.Sanitize(title ?? string.Empty).Trim().TrimEnd('.');
            if (!match.Success || string.IsNullOrWhiteSpace(safeTitle)) return false;
            // The final token is retained verbatim for uniqueness, never converted into a capture date.
            renamed = GameTitle + "_" + safeTitle + "_" + match.Groups["capture"].Value;
            return true;
        }
    }

    /// <summary>Public place -> universe -> experience lookup. No credentials or capture images are sent.</summary>
    internal sealed class RobloxExperienceService
    {
        static readonly HttpClient Client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10),
            MaxResponseContentBufferSize = 1024 * 1024
        };
        internal static readonly RobloxExperienceService Shared = new RobloxExperienceService(Client);
        readonly HttpClient client;
        readonly ConcurrentDictionary<string, (string Title, DateTime Expires)> cache = new();
        readonly SemaphoreSlim gate = new(1, 1);

        internal RobloxExperienceService(HttpClient client) => this.client = client ?? throw new ArgumentNullException(nameof(client));

        internal string GetCachedTitle(string placeId) => cache.TryGetValue(placeId ?? string.Empty, out var entry)
            && entry.Expires > DateTime.UtcNow ? entry.Title : string.Empty;

        internal async Task<string> ResolveTitleAsync(string placeId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!long.TryParse(placeId, NumberStyles.None, CultureInfo.InvariantCulture, out var place) || place <= 0)
                return string.Empty;
            if (cache.TryGetValue(placeId, out var entry) && entry.Expires > DateTime.UtcNow) return entry.Title;
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (cache.TryGetValue(placeId, out entry) && entry.Expires > DateTime.UtcNow) return entry.Title;
                var title = string.Empty;
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                try
                {
                    using var universe = await GetJsonAsync("https://apis.roblox.com/universes/v1/places/"
                        + place.ToString(CultureInfo.InvariantCulture) + "/universe", timeout.Token).ConfigureAwait(false);
                    if (universe.RootElement.ValueKind == JsonValueKind.Object
                        && universe.RootElement.TryGetProperty("universeId", out var id)
                        && id.ValueKind == JsonValueKind.Number && id.TryGetInt64(out var universeId) && universeId > 0)
                    {
                        using var game = await GetJsonAsync("https://games.roblox.com/v1/games?universeIds="
                            + universeId.ToString(CultureInfo.InvariantCulture), timeout.Token).ConfigureAwait(false);
                        if (game.RootElement.ValueKind == JsonValueKind.Object
                            && game.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in data.EnumerateArray())
                            {
                                if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("id", out var gameId)
                                    && gameId.ValueKind == JsonValueKind.Number && gameId.TryGetInt64(out var returnedId) && returnedId == universeId
                                    && item.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
                                {
                                    var value = name.GetString()?.Trim() ?? string.Empty;
                                    if (value.Length <= 200) title = value;
                                    break;
                                }
                            }
                        }
                    }
                }
                catch (HttpRequestException) { }
                catch (JsonException) { }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
                cancellationToken.ThrowIfCancellationRequested();
                cache[placeId] = (title, DateTime.UtcNow.Add(string.IsNullOrWhiteSpace(title) ? TimeSpan.FromSeconds(30) : TimeSpan.FromDays(1)));
                return title;
            }
            finally { gate.Release(); }
        }

        async Task<JsonDocument> GetJsonAsync(string url, CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("PixelVault");
            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        }
    }
}
