using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PixelVaultNative;
using Xunit;

namespace PixelVault.Companion.Tests;

public sealed partial class CompanionTests
{
    static GameMapDefinition SavedMap(string id = "region-one", string provider = "steam", string game = "42") => new()
    {
        Id = id, Provider = provider, GameId = game, GameTitle = "Test journey", Title = "Eastern region", Region = "East",
        ImageFile = "private-image.png", Source = @"C:\Private\source.png", Calibration = "Private extraction evidence",
        PixelWidth = 8192, PixelHeight = 4096, Revision = 0, AchievementIds = ["ONE"], TrackerIds = [TrackerId],
        Markers = [new() { Id = "pin-one", Label = "First chip", Detail = "Behind the building", X = .25, Y = .75,
            WorldX = -100, WorldY = 200, TrackerId = TrackerId, ItemId = "first" }]
    };

    static string MapDatabase(string app) => Path.Combine(app, "game-maps", "maps.sqlite");
    static void WriteMap(string app, GameMapDefinition map, long revision = 1)
    {
        Directory.CreateDirectory(Path.Combine(app, "game-maps"));
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = MapDatabase(app), Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        // Exact Native schema and JSON, including its pre-write JSON revision.
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS game_map(id TEXT PRIMARY KEY,provider TEXT NOT NULL,game_id TEXT NOT NULL,revision INTEGER NOT NULL,definition TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS game_map_game ON game_map(provider,game_id);
            INSERT INTO game_map VALUES($id,$provider,$game,$revision,$definition)
            ON CONFLICT(id) DO UPDATE SET revision=$revision,definition=$definition;
            """;
        command.Parameters.AddWithValue("$id", map.Id); command.Parameters.AddWithValue("$provider", map.Provider);
        command.Parameters.AddWithValue("$game", map.GameId); command.Parameters.AddWithValue("$revision", revision);
        command.Parameters.AddWithValue("$definition", JsonSerializer.Serialize(map)); command.ExecuteNonQuery();
    }

    [Fact]
    public async Task MapsRequireAuthenticationAndExposeOnlyScopedPublicFields()
    {
        using var fixture = new DirectCase(); WriteMap(fixture.App, SavedMap());
        WriteMap(fixture.App, SavedMap("other-game", game: "99"));
        WriteMap(fixture.App, SavedMap("other-provider", provider: "retroachievements"));
        using var host = fixture.Host(); using var client = host.Client();
        const string list = "/api/v1/maps/steam/42";
        foreach (var path in new[] { list, list + "/region-one" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path)).StatusCode);
        await Setup(host, client);
        var response = await client.GetAsync(list);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var listing = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(listing.GetProperty("enabled").GetBoolean());
        var map = Assert.Single(listing.GetProperty("maps").EnumerateArray());
        Assert.Equal("region-one", map.GetProperty("id").GetString());
        Assert.Equal(1, map.GetProperty("revision").GetInt64());
        Assert.Equal(1, map.GetProperty("pinCount").GetInt32());
        var detail = await client.GetFromJsonAsync<JsonElement>(list + "/region-one");
        var pin = Assert.Single(detail.GetProperty("pins").EnumerateArray());
        Assert.Equal(.25, pin.GetProperty("x").GetDouble()); Assert.Equal(.75, pin.GetProperty("y").GetDouble());
        Assert.Equal("Behind the building", pin.GetProperty("detail").GetString());
        Assert.Equal(TrackerId, pin.GetProperty("trackerId").GetString());
        foreach (var value in new[] { "private-image", "Private extraction", "C:\\Private", "imageFile", "worldX", "calibration", Approved, Other })
            Assert.DoesNotContain(value, listing.GetRawText() + detail.GetRawText(), StringComparison.OrdinalIgnoreCase);
        foreach (var path in new[] { list + "/other-game", list + "/other-provider", list + "/missing", "/api/v1/maps/steam/99", "/api/v1/maps/retroachievements/42" })
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PostAsJsonAsync(list + "/region-one", new { title = "Overwrite" })).StatusCode);
    }

    [Fact]
    public async Task MapLinkFiltersAndDesktopChangesAreReadWithoutExportOrRestart()
    {
        using var fixture = new DirectCase(); var original = SavedMap(); WriteMap(fixture.App, original);
        var generic = SavedMap("unlinked"); generic.AchievementIds.Clear(); generic.TrackerIds.Clear(); generic.Markers.Clear(); WriteMap(fixture.App, generic);
        using var host = fixture.Host(); using var client = host.Client(); await Setup(host, client);
        const string list = "/api/v1/maps/steam/42";
        foreach (var suffix in new[] { "?achievement=ONE", "?tracker=" + TrackerId, "?achievement=ONE&tracker=" + TrackerId })
            Assert.Single((await client.GetFromJsonAsync<JsonElement>(list + suffix)).GetProperty("maps").EnumerateArray());
        foreach (var suffix in new[] { "?achievement=missing", "?tracker=missing", "?achievement=ONE&tracker=missing", "?achievement=one" })
            Assert.Empty((await client.GetFromJsonAsync<JsonElement>(list + suffix)).GetProperty("maps").EnumerateArray());
        Assert.Equal(2, (await client.GetFromJsonAsync<JsonElement>(list)).GetProperty("maps").GetArrayLength());
        original.Title = "Updated from desktop"; original.Markers[0].X = .5; WriteMap(fixture.App, original, revision: 2);
        var updated = await client.GetFromJsonAsync<JsonElement>(list + "/region-one");
        Assert.Equal(2, updated.GetProperty("map").GetProperty("revision").GetInt64());
        Assert.Equal(original.Title, updated.GetProperty("map").GetProperty("title").GetString());
        Assert.Equal(.5, updated.GetProperty("pins")[0].GetProperty("x").GetDouble());
        Assert.False(Directory.Exists(Path.Combine(fixture.State, "game-maps")));
    }

    [Fact]
    public async Task MapReadsPreserveDatabaseAndBothAccountsProgressHistoryAndBindings()
    {
        using var fixture = new DirectCase(); WriteMap(fixture.App, SavedMap());
        using var host = fixture.Host(); using var client = host.Client(); await Setup(host, client);
        string Digest(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        var mapBefore = Digest(MapDatabase(fixture.App));
        var trackerBefore = Digest(fixture.Case.Desktop.DatabasePath);
        var tracker = new TrackerRepository(fixture.App, pooling: false, initializeSchema: false);
        string State() => JsonSerializer.Serialize(new[] { Approved, Other }.Select(account => new
        {
            states = tracker.Load(TrackerId, account), history = tracker.History(TrackerId, account),
            source = tracker.Source(TrackerId, account), status = tracker.SourceStatus(TrackerId, account),
            automation = tracker.AutomationHistory(TrackerId, account)
        }));
        var stateBefore = State();
        var first = await client.GetStringAsync("/api/v1/maps/steam/42/region-one");
        Assert.Equal(first, await client.GetStringAsync("/api/v1/maps/steam/42/region-one?account=" + Other));
        var progress = await client.GetStringAsync("/api/v1/maps/steam/42/region-one/progress");
        Assert.Equal(progress, await client.GetStringAsync("/api/v1/maps/steam/42/region-one/progress?account=" + Other));
        Assert.Equal(mapBefore, Digest(MapDatabase(fixture.App)));
        Assert.Equal(trackerBefore, Digest(fixture.Case.Desktop.DatabasePath));
        Assert.Equal(stateBefore, State());
        Assert.False(Directory.Exists(Path.Combine(fixture.App, "game-maps", "images"))); // Metadata does not open/decode images.
        Assert.False(Directory.Exists(Path.Combine(fixture.Cache, "maps")));
    }

    [Fact]
    public async Task MissingMapLibraryIsEmptyWithoutCreatingAStoreAndAppearsAfterImport()
    {
        using var fixture = new DirectCase(); using var host = fixture.Host(); using var client = host.Client(); await Setup(host, client);
        var listing = await client.GetFromJsonAsync<JsonElement>("/api/v1/maps/steam/42");
        Assert.True(listing.GetProperty("enabled").GetBoolean()); Assert.Empty(listing.GetProperty("maps").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/maps/steam/42/missing")).StatusCode);
        Assert.False(Directory.Exists(Path.Combine(fixture.App, "game-maps")));
        WriteMap(fixture.App, SavedMap());
        Assert.Single((await client.GetFromJsonAsync<JsonElement>("/api/v1/maps/steam/42")).GetProperty("maps").EnumerateArray());
        // Move only a known fixture file within its isolated map directory, then recover it.
        var database = MapDatabase(fixture.App); var unavailable = database + ".unavailable";
        File.Move(database, unavailable);
        try
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/api/v1/maps/steam/42")).StatusCode);
            Assert.False(File.Exists(database));
        }
        finally { File.Move(unavailable, database); }
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/maps/steam/42/region-one")).StatusCode);
    }

    [Theory]
    [InlineData("missing-database")]
    [InlineData("missing-schema")]
    [InlineData("corrupt-database")]
    [InlineData("invalid-json")]
    [InlineData("wrong-game")]
    [InlineData("wrong-id")]
    [InlineData("bad-coordinate")]
    public async Task InvalidMapStoresReturnUnavailableWithoutRepairOrPrivateDetails(string failure)
    {
        using var fixture = new DirectCase(); Directory.CreateDirectory(Path.Combine(fixture.App, "game-maps"));
        if (failure != "missing-database")
        {
            WriteMap(fixture.App, SavedMap());
            if (failure == "corrupt-database") File.WriteAllText(MapDatabase(fixture.App), "Not SQLite");
            else
            {
                using var connection = new SqliteConnection("Data Source=" + MapDatabase(fixture.App) + ";Pooling=False"); connection.Open();
                using var command = connection.CreateCommand();
                var map = SavedMap();
                if (failure == "wrong-game") map.GameId = "99";
                if (failure == "wrong-id") map.Id = "unexpected";
                if (failure == "bad-coordinate") map.Markers[0].Y = 2;
                command.CommandText = failure == "missing-schema" ? "DROP TABLE game_map" : "UPDATE game_map SET definition=$json";
                if (failure != "missing-schema") command.Parameters.AddWithValue("$json", failure == "invalid-json" ? "{" : JsonSerializer.Serialize(map));
                command.ExecuteNonQuery();
            }
        }
        var before = File.Exists(MapDatabase(fixture.App)) ? File.ReadAllBytes(MapDatabase(fixture.App)) : null;
        using var host = fixture.Host(); using var client = host.Client(); await Setup(host, client);
        foreach (var suffix in new[] { "", "/region-one" })
        {
            var response = await client.GetAsync("/api/v1/maps/steam/42" + suffix);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            var text = await response.Content.ReadAsStringAsync(); Assert.DoesNotContain(fixture.App, text); Assert.DoesNotContain("SQLite", text);
        }
        if (before is null) Assert.False(File.Exists(MapDatabase(fixture.App)));
        else Assert.Equal(before, File.ReadAllBytes(MapDatabase(fixture.App)));
    }

    [Fact]
    public async Task IsolatedWorkspacesDoNotUseAnImplicitMapStore()
    {
        using var host = new Host(); using var client = host.Client(); await Setup(host, client);
        var game = (await client.GetFromJsonAsync<JsonElement>("/api/v1/games"))[0];
        var provider = game.GetProperty("provider").GetString()!; var id = game.GetProperty("gameId").GetString()!;
        WriteMap(host.Root, SavedMap(provider: provider, game: id));
        var listing = await client.GetFromJsonAsync<JsonElement>($"/api/v1/maps/{provider}/{id}");
        Assert.False(listing.GetProperty("enabled").GetBoolean()); Assert.Empty(listing.GetProperty("maps").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/maps/{provider}/{id}/region-one")).StatusCode);
    }

    [Fact]
    public void SharedReaderSupportsNonProviderMapsAndDoesNotInitializeMissingStores()
    {
        using var fixture = new DirectCase(); var reader = new GameMapReader(fixture.App);
        Assert.Empty(reader.List("library", "local-game")); Assert.False(Directory.Exists(Path.Combine(fixture.App, "game-maps")));
        var map = SavedMap(provider: "library", game: "local-game"); map.AchievementIds.Clear(); map.TrackerIds.Clear(); map.Markers.Clear(); WriteMap(fixture.App, map);
        Assert.Single(reader.List("library", "local-game")); Assert.Empty(reader.List("steam", "local-game"));
        Assert.Null(reader.Find("library", "other-game", map.Id));
        Assert.Empty(reader.List("library", "local-game' OR 1=1 --"));
    }

    [Fact]
    public void SharedMapReadsSeeCommittedWalUpdatesButNotInProgressDesktopEdits()
    {
        using var fixture = new DirectCase(); WriteMap(fixture.App, SavedMap());
        using var writer = new SqliteConnection("Data Source=" + MapDatabase(fixture.App) + ";Pooling=False"); writer.Open();
        var reader = new GameMapReader(fixture.App);
        using var transaction = writer.BeginTransaction();
        using var command = writer.CreateCommand(); command.Transaction = transaction;
        var map = SavedMap(); map.Title = "Committed desktop edit";
        command.CommandText = "UPDATE game_map SET definition=$json,revision=2";
        command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(map)); command.ExecuteNonQuery();
        Assert.Equal(1, reader.Find("steam", "42", map.Id).Revision);
        transaction.Commit();
        var current = reader.Find("steam", "42", map.Id);
        Assert.Equal(2, current.Revision); Assert.Equal(map.Title, current.Title);
    }

    [Fact]
    public async Task NonProviderLibraryGamesCanBrowseUnlinkedMaps()
    {
        using var fixture = new DirectCase();
        using (var connection = new SqliteConnection("Data Source=" + fixture.Index + ";Pooling=False"))
        {
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = "UPDATE game_index SET steam_app_id='',platform_label='Switch'"; command.ExecuteNonQuery();
        }
        var map = SavedMap(provider: "library", game: "G1");
        map.AchievementIds.Clear(); map.TrackerIds.Clear(); map.Markers[0].TrackerId = null!; map.Markers[0].ItemId = null!;
        WriteMap(fixture.App, map);
        using var host = fixture.Host(); using var client = host.Client(); await Setup(host, client);
        var detail = await client.GetFromJsonAsync<JsonElement>("/api/v1/maps/library/G1/region-one");
        Assert.Equal("library", detail.GetProperty("map").GetProperty("provider").GetString());
        Assert.Empty(detail.GetProperty("map").GetProperty("achievementIds").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("pins")[0].GetProperty("trackerId").ValueKind);
    }
}
