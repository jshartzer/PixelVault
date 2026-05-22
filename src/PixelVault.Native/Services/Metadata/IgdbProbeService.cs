using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PixelVaultNative
{
    sealed class IgdbGameMetadata
    {
        public string Id = string.Empty;
        public string Name = string.Empty;
        public string Slug = string.Empty;
        public string Summary = string.Empty;
        public string ReleaseDate = string.Empty;
        public string Genres = string.Empty;
        public string Platforms = string.Empty;
        public string GameTypeId = string.Empty;
        public string GameTypeName = string.Empty;
        public string ParentGameId = string.Empty;
        public string VersionParentId = string.Empty;
        public string DeveloperId = string.Empty;
        public string Developer = string.Empty;
        public string Publisher = string.Empty;
        public string CoverImageId = string.Empty;
        public string CollectionId = string.Empty;
        public string CollectionName = string.Empty;
        public string FranchiseId = string.Empty;
        public string FranchiseName = string.Empty;
    }

    sealed class IgdbProbeService
    {
        readonly string appVersion;
        string cachedTokenClientId = string.Empty;
        string cachedAccessToken = string.Empty;
        DateTime cachedAccessTokenExpiresUtc = DateTime.MinValue;

        public IgdbProbeService(string appVersion)
        {
            this.appVersion = string.IsNullOrWhiteSpace(appVersion) ? "1.0" : appVersion.Trim();
        }

        public async Task<IgdbGameMetadata> ResolveGameMetadataAsync(
            string twitchClientId,
            string twitchClientSecret,
            string igdbId,
            string title,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            twitchClientId = (twitchClientId ?? string.Empty).Trim();
            twitchClientSecret = (twitchClientSecret ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(twitchClientId) || string.IsNullOrWhiteSpace(twitchClientSecret)) return null;
            using (var client = CreateClient())
            {
                var token = await FetchAppAccessTokenCachedAsync(client, twitchClientId, twitchClientSecret, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(igdbId))
                {
                    var byId = await QuerySingleGameAsync(client, twitchClientId, token, "where id = " + CleanNumericId(igdbId) + ";", cancellationToken).ConfigureAwait(false);
                    if (byId != null) return byId;
                }

                var slug = BuildIgdbSlug(title);
                if (!string.IsNullOrWhiteSpace(slug))
                {
                    var bySlug = await QuerySingleGameAsync(client, twitchClientId, token, "where slug = " + JsonSerializer.Serialize(slug) + ";", cancellationToken).ConfigureAwait(false);
                    if (bySlug != null && IsConfidentTitleMatch(title, bySlug.Name)) return bySlug;
                }

                var bySearch = await QuerySingleGameAsync(
                    client,
                    twitchClientId,
                    token,
                    "search " + JsonSerializer.Serialize((title ?? string.Empty).Trim()) + ";",
                    cancellationToken).ConfigureAwait(false);
                return bySearch != null && IsConfidentTitleMatch(title, bySearch.Name) ? bySearch : null;
            }
        }

        public async Task<List<IgdbGameMetadata>> LoadSeriesGamesAsync(
            string twitchClientId,
            string twitchClientSecret,
            string collectionId,
            string franchiseId,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            twitchClientId = (twitchClientId ?? string.Empty).Trim();
            twitchClientSecret = (twitchClientSecret ?? string.Empty).Trim();
            collectionId = CleanNumericId(collectionId);
            franchiseId = CleanNumericId(franchiseId);
            if (string.IsNullOrWhiteSpace(twitchClientId) || string.IsNullOrWhiteSpace(twitchClientSecret)
                || (string.IsNullOrWhiteSpace(collectionId) && string.IsNullOrWhiteSpace(franchiseId)))
                return new List<IgdbGameMetadata>();

            using (var client = CreateClient())
            {
                var token = await FetchAppAccessTokenCachedAsync(client, twitchClientId, twitchClientSecret, cancellationToken).ConfigureAwait(false);
                var where = !string.IsNullOrWhiteSpace(collectionId)
                    ? "where collection = " + collectionId + " & " + RelatedGamesGameTypeFilter() + ";"
                    : "where franchises = (" + franchiseId + ") & " + RelatedGamesGameTypeFilter() + ";";
                var json = await PostGamesQueryAsync(
                    client,
                    twitchClientId,
                    token,
                    GameFields() + Environment.NewLine + where + Environment.NewLine + "sort first_release_date asc;" + Environment.NewLine + "limit 100;",
                    cancellationToken).ConfigureAwait(false);
                return ParseGameList(json);
            }
        }

        public async Task<List<IgdbGameMetadata>> LoadDeveloperGamesAsync(
            string twitchClientId,
            string twitchClientSecret,
            string developerCompanyId,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            twitchClientId = (twitchClientId ?? string.Empty).Trim();
            twitchClientSecret = (twitchClientSecret ?? string.Empty).Trim();
            developerCompanyId = CleanNumericId(developerCompanyId);
            if (string.IsNullOrWhiteSpace(twitchClientId)
                || string.IsNullOrWhiteSpace(twitchClientSecret)
                || string.IsNullOrWhiteSpace(developerCompanyId))
                return new List<IgdbGameMetadata>();

            using (var client = CreateClient())
            {
                var token = await FetchAppAccessTokenCachedAsync(client, twitchClientId, twitchClientSecret, cancellationToken).ConfigureAwait(false);
                var json = await PostGamesQueryAsync(
                    client,
                    twitchClientId,
                    token,
                    GameFields() + Environment.NewLine
                    + "where involved_companies.company = " + developerCompanyId + " & involved_companies.developer = true & " + RelatedGamesGameTypeFilter() + ";"
                    + Environment.NewLine
                    + "sort first_release_date desc;"
                    + Environment.NewLine
                    + "limit 100;",
                    cancellationToken).ConfigureAwait(false);
                return ParseGameList(json);
            }
        }

        HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "PixelVault/" + appVersion);
            return client;
        }

        public async Task<string> ProbeGameFieldsAsync(
            string twitchClientId,
            string twitchClientSecret,
            string searchText,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            twitchClientId = (twitchClientId ?? string.Empty).Trim();
            twitchClientSecret = (twitchClientSecret ?? string.Empty).Trim();
            searchText = string.IsNullOrWhiteSpace(searchText) ? "Portal" : searchText.Trim();
            if (string.IsNullOrWhiteSpace(twitchClientId) || string.IsNullOrWhiteSpace(twitchClientSecret))
                return "IGDB probe skipped: add Twitch Client ID and Client Secret in Path Settings first.";

            using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) })
            {
                client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "PixelVault/" + appVersion);
                var token = await FetchAppAccessTokenCachedAsync(client, twitchClientId, twitchClientSecret, cancellationToken).ConfigureAwait(false);
                var json = await SearchGamesAsync(client, twitchClientId, token, searchText, cancellationToken).ConfigureAwait(false);
                return SummarizeProbeResult(json, searchText);
            }
        }

        async Task<string> FetchAppAccessTokenCachedAsync(
            HttpClient client,
            string twitchClientId,
            string twitchClientSecret,
            CancellationToken cancellationToken)
        {
            if (!string.IsNullOrWhiteSpace(cachedAccessToken)
                && string.Equals(cachedTokenClientId, twitchClientId ?? string.Empty, StringComparison.Ordinal)
                && DateTime.UtcNow < cachedAccessTokenExpiresUtc)
                return cachedAccessToken;

            var token = await FetchAppAccessTokenAsync(client, twitchClientId, twitchClientSecret, cancellationToken).ConfigureAwait(false);
            cachedTokenClientId = twitchClientId ?? string.Empty;
            cachedAccessToken = token ?? string.Empty;
            cachedAccessTokenExpiresUtc = DateTime.UtcNow.AddMinutes(45);
            return cachedAccessToken;
        }

        async Task<IgdbGameMetadata> QuerySingleGameAsync(
            HttpClient client,
            string twitchClientId,
            string accessToken,
            string whereOrSearchClause,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(whereOrSearchClause)) return null;
            var query = GameFields() + Environment.NewLine + whereOrSearchClause + Environment.NewLine + "limit 5;";
            var json = await PostGamesQueryAsync(client, twitchClientId, accessToken, query, cancellationToken).ConfigureAwait(false);
            return ParseGameList(json).FirstOrDefault();
        }

        static async Task<string> PostGamesQueryAsync(
            HttpClient client,
            string twitchClientId,
            string accessToken,
            string query,
            CancellationToken cancellationToken)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Post, "https://api.igdb.com/v4/games"))
            {
                request.Headers.TryAddWithoutValidation("Client-ID", twitchClientId);
                request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + accessToken);
                request.Content = new StringContent(query ?? string.Empty, Encoding.UTF8, "text/plain");
                using (var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false))
                {
                    var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();
                    return body;
                }
            }
        }

        static async Task<string> FetchAppAccessTokenAsync(
            HttpClient client,
            string twitchClientId,
            string twitchClientSecret,
            CancellationToken cancellationToken)
        {
            var url =
                "https://id.twitch.tv/oauth2/token?client_id=" + Uri.EscapeDataString(twitchClientId)
                + "&client_secret=" + Uri.EscapeDataString(twitchClientSecret)
                + "&grant_type=client_credentials";
            using (var response = await client.PostAsync(url, new StringContent(string.Empty, Encoding.UTF8), cancellationToken).ConfigureAwait(false))
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                using (var doc = JsonDocument.Parse(body))
                {
                    JsonElement token;
                    if (doc.RootElement.TryGetProperty("access_token", out token) && token.ValueKind == JsonValueKind.String)
                        return token.GetString() ?? string.Empty;
                }
            }
            return string.Empty;
        }

        static async Task<string> SearchGamesAsync(
            HttpClient client,
            string twitchClientId,
            string accessToken,
            string searchText,
            CancellationToken cancellationToken)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Post, "https://api.igdb.com/v4/games"))
            {
                request.Headers.TryAddWithoutValidation("Client-ID", twitchClientId);
                request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + accessToken);
                request.Content = new StringContent(BuildGameProbeQuery(searchText), Encoding.UTF8, "text/plain");
                using (var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false))
                {
                    var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();
                    return body;
                }
            }
        }

        static string BuildGameProbeQuery(string searchText)
        {
            return GameFields().Replace("collection.id,collection.name", "collection.name")
                + Environment.NewLine
                + "search " + JsonSerializer.Serialize(searchText) + ";"
                + Environment.NewLine
                + "limit 1;";
        }

        static string GameFields()
        {
            return
                "fields id,name,slug,summary,storyline,first_release_date,game_type.type,parent_game.id,version_parent.id,"
                + "genres.name,platforms.name,cover.image_id,cover.url,screenshots.image_id,"
                + "websites.url,involved_companies.company.id,involved_companies.company.name,involved_companies.developer,involved_companies.publisher,"
                + "rating,total_rating,aggregated_rating,themes.name,game_modes.name,franchises.name,collection.id,collection.name,"
                + "similar_games.name,external_games.category,external_games.uid,external_games.url;";
        }

        static List<IgdbGameMetadata> ParseGameList(string json)
        {
            var list = new List<IgdbGameMetadata>();
            if (string.IsNullOrWhiteSpace(json)) return list;
            using (var doc = JsonDocument.Parse(json))
            {
                if (doc.RootElement.ValueKind != JsonValueKind.Array) return list;
                foreach (var game in doc.RootElement.EnumerateArray())
                {
                    var item = new IgdbGameMetadata
                    {
                        Id = TryGetInt64(game, "id").ToString(CultureInfo.InvariantCulture),
                        Name = TryGetString(game, "name"),
                        Slug = TryGetString(game, "slug"),
                        Summary = TryGetString(game, "summary"),
                        ReleaseDate = TryGetUnixDate(game, "first_release_date"),
                        Genres = JoinNames(game, "genres"),
                        Platforms = JoinNames(game, "platforms"),
                        CoverImageId = ReadNestedString(game, "cover", "image_id")
                    };
                    JsonElement gameType;
                    if (game.TryGetProperty("game_type", out gameType) && gameType.ValueKind == JsonValueKind.Object)
                    {
                        item.GameTypeId = TryGetInt64(gameType, "id").ToString(CultureInfo.InvariantCulture);
                        item.GameTypeName = TryGetString(gameType, "type");
                    }
                    item.ParentGameId = ReadNestedInt64(game, "parent_game", "id").ToString(CultureInfo.InvariantCulture);
                    if (item.ParentGameId == "0") item.ParentGameId = string.Empty;
                    item.VersionParentId = ReadNestedInt64(game, "version_parent", "id").ToString(CultureInfo.InvariantCulture);
                    if (item.VersionParentId == "0") item.VersionParentId = string.Empty;
                    JsonElement collection;
                    if (game.TryGetProperty("collection", out collection) && collection.ValueKind == JsonValueKind.Object)
                    {
                        item.CollectionId = TryGetInt64(collection, "id").ToString(CultureInfo.InvariantCulture);
                        if (item.CollectionId == "0") item.CollectionId = string.Empty;
                        item.CollectionName = TryGetString(collection, "name");
                    }
                    JsonElement franchises;
                    if (game.TryGetProperty("franchises", out franchises) && franchises.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var first in franchises.EnumerateArray())
                        {
                            if (first.ValueKind != JsonValueKind.Object) continue;
                            item.FranchiseId = TryGetInt64(first, "id").ToString(CultureInfo.InvariantCulture);
                            if (item.FranchiseId == "0") item.FranchiseId = string.Empty;
                            item.FranchiseName = TryGetString(first, "name");
                            break;
                        }
                    }
                    ReadCompanies(game, out item.DeveloperId, out item.Developer, out item.Publisher);
                    if (!string.IsNullOrWhiteSpace(item.Id) && item.Id != "0") list.Add(item);
                }
            }
            return list;
        }

        static string RelatedGamesGameTypeFilter()
        {
            return "game_type = (0,9)";
        }

        static void ReadCompanies(JsonElement game, out string developerId, out string developer, out string publisher)
        {
            developerId = string.Empty;
            developer = string.Empty;
            publisher = string.Empty;
            JsonElement companies;
            if (!game.TryGetProperty("involved_companies", out companies) || companies.ValueKind != JsonValueKind.Array) return;
            foreach (var row in companies.EnumerateArray())
            {
                var name = ReadNestedString(row, "company", "name");
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (developer.Length == 0 && ReadBool(row, "developer"))
                {
                    developer = name;
                    var id = ReadNestedInt64(row, "company", "id");
                    if (id > 0) developerId = id.ToString(CultureInfo.InvariantCulture);
                }
                if (publisher.Length == 0 && ReadBool(row, "publisher")) publisher = name;
            }
        }

        static long ReadNestedInt64(JsonElement element, string objectName, string propertyName)
        {
            JsonElement obj;
            if (!element.TryGetProperty(objectName, out obj) || obj.ValueKind != JsonValueKind.Object) return 0;
            return TryGetInt64(obj, propertyName);
        }

        static bool ReadBool(JsonElement element, string name)
        {
            JsonElement value;
            return element.TryGetProperty(name, out value) && value.ValueKind == JsonValueKind.True;
        }

        static string JoinNames(JsonElement element, string arrayName)
        {
            JsonElement array;
            if (!element.TryGetProperty(arrayName, out array) || array.ValueKind != JsonValueKind.Array) return string.Empty;
            return string.Join(", ", array.EnumerateArray().Select(row => TryGetString(row, "name")).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase));
        }

        static string ReadNestedString(JsonElement element, string objectName, string propertyName)
        {
            JsonElement obj;
            if (!element.TryGetProperty(objectName, out obj) || obj.ValueKind != JsonValueKind.Object) return string.Empty;
            return TryGetString(obj, propertyName);
        }

        static string CleanNumericId(string value)
        {
            var raw = (value ?? string.Empty).Trim();
            return new string(raw.Where(char.IsDigit).ToArray());
        }

        public static string BuildIgdbCoverUrl(string imageId, string size)
        {
            imageId = (imageId ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(imageId)) return string.Empty;
            size = string.IsNullOrWhiteSpace(size) ? "t_cover_small" : size.Trim();
            return "https://images.igdb.com/igdb/image/upload/" + size + "/" + imageId + ".jpg";
        }

        public static string BuildIgdbGameUrl(string slug)
        {
            slug = (slug ?? string.Empty).Trim();
            return string.IsNullOrWhiteSpace(slug) ? "https://www.igdb.com/" : "https://www.igdb.com/games/" + slug;
        }

        static string BuildIgdbSlug(string title)
        {
            var raw = (title ?? string.Empty).Trim().ToLowerInvariant();
            if (raw.Length == 0) return string.Empty;
            var sb = new StringBuilder();
            var lastDash = false;
            foreach (var ch in raw)
            {
                if ((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9'))
                {
                    sb.Append(ch);
                    lastDash = false;
                }
                else if (!lastDash)
                {
                    sb.Append('-');
                    lastDash = true;
                }
            }
            return sb.ToString().Trim('-');
        }

        static bool IsConfidentTitleMatch(string requested, string candidate)
        {
            var a = NormalizeTitleForMatch(requested);
            var b = NormalizeTitleForMatch(candidate);
            return a.Length > 0 && (string.Equals(a, b, StringComparison.OrdinalIgnoreCase) || b.StartsWith(a + " ", StringComparison.OrdinalIgnoreCase));
        }

        static string NormalizeTitleForMatch(string title)
        {
            var raw = (title ?? string.Empty).ToLowerInvariant();
            var sb = new StringBuilder();
            foreach (var ch in raw)
            {
                if (char.IsLetterOrDigit(ch)) sb.Append(ch);
                else if (sb.Length > 0 && sb[sb.Length - 1] != ' ') sb.Append(' ');
            }
            return sb.ToString().Trim();
        }

        static string SummarizeProbeResult(string json, string searchText)
        {
            if (string.IsNullOrWhiteSpace(json)) return "IGDB probe returned an empty response.";
            using (var doc = JsonDocument.Parse(json))
            {
                if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
                    return "IGDB probe connected, but no game matched \"" + searchText + "\".";

                var game = doc.RootElement[0];
                var fields = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                CollectFieldPaths(game, string.Empty, fields, 2);
                var title = TryGetString(game, "name");
                var id = TryGetInt64(game, "id");
                var release = TryGetUnixDate(game, "first_release_date");
                var lines = new List<string>
                {
                    "IGDB probe connected.",
                    "Sample: " + (string.IsNullOrWhiteSpace(title) ? "(untitled)" : title) + (id > 0 ? " [" + id.ToString(CultureInfo.InvariantCulture) + "]" : string.Empty) + (string.IsNullOrWhiteSpace(release) ? string.Empty : " | released " + release),
                    "Returned field paths: " + string.Join(", ", fields.Take(80))
                };
                return string.Join(Environment.NewLine, lines);
            }
        }

        static void CollectFieldPaths(JsonElement element, string prefix, ISet<string> fields, int depth)
        {
            if (depth < 0) return;
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in element.EnumerateObject())
                {
                    var path = string.IsNullOrWhiteSpace(prefix) ? property.Name : prefix + "." + property.Name;
                    fields.Add(path);
                    CollectFieldPaths(property.Value, path, fields, depth - 1);
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray().Take(3))
                    CollectFieldPaths(item, prefix + "[]", fields, depth - 1);
            }
        }

        static string TryGetString(JsonElement element, string name)
        {
            JsonElement value;
            return element.TryGetProperty(name, out value) && value.ValueKind == JsonValueKind.String ? value.GetString() : string.Empty;
        }

        static long TryGetInt64(JsonElement element, string name)
        {
            JsonElement value;
            if (!element.TryGetProperty(name, out value)) return 0;
            return value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var parsed) ? parsed : 0;
        }

        static string TryGetUnixDate(JsonElement element, string name)
        {
            var seconds = TryGetInt64(element, name);
            if (seconds <= 0) return string.Empty;
            try
            {
                return DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
