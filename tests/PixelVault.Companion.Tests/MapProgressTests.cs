using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PixelVaultNative;
using Xunit;

namespace PixelVault.Companion.Tests;

public sealed partial class CompanionTests
{
    const string MapProgressPath = "/api/v1/maps/steam/42/region-one/progress";
    static GameMapMarker ProgressPin(string id, string? item, string? tracker = TrackerId) => new()
    { Id = id, Label = id, X = .5, Y = .5, TrackerId = tracker!, ItemId = item! };

    [Fact]
    public async Task MapProgressUsesOnlyApprovedSameGameActiveItemsAndEffectiveSavedCounts()
    {
        using var fixture = new DirectCase(); var repo = fixture.Case.Desktop;
        repo.SetAchievementLink(TrackerId, "ONE");
        foreach (var foreign in new[] { ("foreign-game", "steam", "99"), ("foreign-provider", "retroachievements", "42") })
        {
            repo.AddDefinition(new() { Id = foreign.Item1, Provider = foreign.Item2, GameId = foreign.Item3,
                GameTitle = "Other game", Title = "Private tracker", Items = [new() { Id = "secret", Name = "Private" }] });
            repo.SetManualProgress(foreign.Item1, Approved, "secret", 1);
        }
        repo.SetManualProgress(TrackerId, Approved, "count", 5);
        var map = SavedMap(); map.Markers = [ProgressPin("zero", "first"), ProgressPin("done", "count"),
            ProgressPin("unknown", "unknown"), ProgressPin("archived", "archived"), ProgressPin("missing-item", "missing"),
            ProgressPin("unlinked", null, null), ProgressPin("tracker-only", null), ProgressPin("missing-tracker", "first", "missing"),
            ProgressPin("foreign-game", "secret", "foreign-game"), ProgressPin("foreign-provider", "secret", "foreign-provider")];
        WriteMap(fixture.App, map);
        using var host = fixture.Host(); using var client = host.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(MapProgressPath)).StatusCode);
        await Setup(host, client);
        var response = await client.GetAsync(MapProgressPath);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var result = (await response.Content.ReadFromJsonAsync<MapProgress>())!;
        Assert.Equal((await client.GetFromJsonAsync<JsonElement>("/api/v1/workspace")).GetProperty("id").GetString(), result.WorkspaceId);
        var pins = result.Pins.ToDictionary(p => p.Id);
        Assert.Equal("remaining", pins["zero"].Status); Assert.Equal(0, pins["zero"].Current);
        Assert.Equal("completed", pins["done"].Status); Assert.Equal(5, pins["done"].Current); Assert.Equal(5, pins["done"].Target);
        Assert.Equal("#tracker/" + TrackerId, pins["done"].TrackerUrl); Assert.Equal("#achievement/steam/42/ONE", pins["done"].AchievementUrl);
        Assert.Equal("unknown", pins["unknown"].Status); Assert.Null(pins["unknown"].Current);
        Assert.Equal("unlinked", pins["unlinked"].Status); Assert.Null(pins["unlinked"].TrackerUrl);
        Assert.Equal("unlinked", pins["tracker-only"].Status); Assert.NotNull(pins["tracker-only"].TrackerUrl);
        foreach (var id in new[] { "archived", "missing-item", "missing-tracker", "foreign-game", "foreign-provider" })
        { Assert.Equal("unavailable", pins[id].Status); Assert.Null(pins[id].Current); Assert.Null(pins[id].TrackerUrl); Assert.Null(pins[id].AchievementUrl); }
        Assert.Equal(result.Version, (await client.GetFromJsonAsync<MapProgress>(MapProgressPath + "?account=" + Other))!.Version);
        var json = await response.Content.ReadAsStringAsync();
        foreach (var secret in new[] { Approved, Other, "Private", "note", "observed", "source", "evidence" }) Assert.DoesNotContain(secret, json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/maps/steam/99/region-one/progress")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/maps/steam/42/missing/progress")).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PostAsJsonAsync(MapProgressPath, new { current = 1 })).StatusCode);
    }

    [Fact]
    public async Task MapProgressVersionTracksSavedCompletionAndReversalButIgnoresNotes()
    {
        using var fixture = new DirectCase(); WriteMap(fixture.App, SavedMap());
        using var host = fixture.Host(); using var client = host.Client(); await Setup(host, client);
        async Task<MapProgress> Read() => (await client.GetFromJsonAsync<MapProgress>(MapProgressPath))!;
        var original = await Read(); fixture.Case.Desktop.SaveNote(TrackerId, Approved, "first", "A private changed note");
        Assert.Equal(original.Version, (await Read()).Version);
        fixture.Case.Desktop.SetManualProgress(TrackerId, Approved, "first", 1);
        var completed = await Read(); Assert.NotEqual(original.Version, completed.Version); Assert.Equal("completed", completed.Pins[0].Status);
        fixture.Case.Desktop.SetManualProgress(TrackerId, Approved, "first", 0);
        var reversed = await Read(); Assert.Equal(original.Version, reversed.Version); Assert.Equal("remaining", reversed.Pins[0].Status);
        WriteMap(fixture.App, SavedMap(), revision: 2); var changedMap = await Read();
        Assert.Equal(2, changedMap.MapRevision); Assert.NotEqual(reversed.Version, changedMap.Version);
    }

    [Fact]
    public async Task MapProgressRejectsChangedConnectedAccountEvenWithCachedLibraryAndRecovers()
    {
        using var fixture = new DirectCase(); WriteMap(fixture.App, SavedMap());
        using var host = fixture.Host(); using var client = host.Client(); await Setup(host, client);
        var before = await client.GetFromJsonAsync<MapProgress>(MapProgressPath);
        var settings = Path.Combine(fixture.App, "PixelVault.settings.ini"); var original = File.ReadAllText(settings);
        File.WriteAllText(settings, original.Replace(Approved, Other));
        var response = await client.GetAsync(MapProgressPath); Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.DoesNotContain(Other, await response.Content.ReadAsStringAsync());
        File.WriteAllText(settings, original);
        Assert.Equal(before!.Version, (await client.GetFromJsonAsync<MapProgress>(MapProgressPath))!.Version);
    }

    [Fact]
    public async Task MapProgressDoesNotAuthorizeExtraConnectedProviderAccounts()
    {
        using var fixture = new DirectCase();
        using (var connection = new SqliteConnection("Data Source=" + fixture.Index + ";Pooling=False"))
        {
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = "UPDATE game_index SET steam_app_id='',retro_achievements_game_id='42',platform_label='Emulation'"; command.ExecuteNonQuery();
        }
        fixture.Case.Desktop.AddDefinition(new() { Id = "other-provider", Provider = "retroachievements", GameId = "42", GameTitle = "Retro", Title = "Other account tracker", Items = [new() { Id = "first", Name = "First" }] });
        fixture.Case.Desktop.SetManualProgress("other-provider", Other, "first", 1);
        var map = SavedMap(provider: "retroachievements"); map.Markers[0].TrackerId = "other-provider"; WriteMap(fixture.App, map);
        using var host = fixture.Host(); using var client = host.Client(); await Setup(host, client);
        var result = (await client.GetFromJsonAsync<MapProgress>("/api/v1/maps/retroachievements/42/region-one/progress"))!;
        Assert.Equal("unavailable", result.Pins[0].Status); Assert.Null(result.Pins[0].Current); Assert.Null(result.Pins[0].TrackerUrl);
        File.AppendAllText(Path.Combine(fixture.App, "PixelVault.settings.ini"), "retroachievements_username=" + Other + "\n");
        // Connecting an additional account cannot expand the running workspace's approved scope.
        var response = await client.GetAsync("/api/v1/maps/retroachievements/42/region-one/progress");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode); Assert.DoesNotContain(Other, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task MapProgressMissingTrackerStoreIsUnavailableWithoutCreatingReplacement()
    {
        using var fixture = new DirectCase(); WriteMap(fixture.App, SavedMap());
        using var host = fixture.Host(); using var client = host.Client(); await Setup(host, client);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(MapProgressPath)).StatusCode);
        var file = fixture.Case.Desktop.DatabasePath; File.Move(file, file + ".unavailable");
        try { Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync(MapProgressPath)).StatusCode); Assert.False(File.Exists(file)); }
        finally { File.Move(file + ".unavailable", file); }
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(MapProgressPath)).StatusCode);
    }

    [Fact]
    public void MapTrackerBatchReadsOnlyCommittedWalStateAndTreatsIdsLiterally()
    {
        using var fixture = new DirectCase(); var repo = fixture.Case.Desktop;
        using var writer = new SqliteConnection("Data Source=" + repo.DatabasePath + ";Pooling=False"); writer.Open();
        using var transaction = writer.BeginTransaction(); using var command = writer.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "UPDATE tracker_item_state SET manual_current=1 WHERE tracker_id=$t AND account=$a AND item_id='first'";
        command.Parameters.AddWithValue("$t", TrackerId); command.Parameters.AddWithValue("$a", Approved); command.ExecuteNonQuery();
        int? Read() => Assert.Single(repo.ReadGameTrackers("steam", "42", Approved, [TrackerId, TrackerId])).States.Single(s => s.Item.Id == "first").Current;
        Assert.Equal(0, Read()); transaction.Commit(); Assert.Equal(1, Read());
        Assert.Empty(repo.ReadGameTrackers("steam", "42' OR 1=1 --", Approved, [TrackerId]));
        Assert.Empty(repo.ReadGameTrackers("retroachievements", "42", Approved, [TrackerId]));
        Assert.Empty(repo.ReadGameTrackers("steam", "42", Approved, ["' OR 1=1 --"]));
    }

    [Fact]
    public async Task All56SeedPinsRetainCoordinatesAndApprovedSavedCompletionAcrossThreeRegions()
    {
        using var fixture = new DirectCase();
        var root = new DirectoryInfo(AppContext.BaseDirectory); while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "src"))) root = root.Parent;
        var seed = JsonSerializer.Deserialize<GameMapDefinition[]>(File.ReadAllText(Path.Combine(root!.FullName, "src", "PixelVault.Native", "Services", "Achievements", "TrackingData", "DeathStrandingMemoryMaps.json")))!;
        Assert.Equal(3, seed.Length); Assert.Equal(56, seed.Sum(m => m.Markers.Count));
        var completed = seed.SelectMany(m => m.Markers).Where((_, index) => index % 3 == 0).Select(m => m.ItemId).ToHashSet();
        foreach (var map in seed)
        {
            WriteMap(fixture.App, map);
            foreach (var pin in map.Markers)
            {
                fixture.Case.Desktop.SetManualProgress(pin.TrackerId, Other, pin.ItemId, 1);
                if (completed.Contains(pin.ItemId)) fixture.Case.Desktop.SetManualProgress(pin.TrackerId, Approved, pin.ItemId, 1);
            }
        }
        using var host = fixture.Host(); using var client = host.Client(); await Setup(host, client);
        foreach (var map in seed)
        {
            var route = $"/api/v1/maps/steam/1850570/{map.Id}";
            var detail = (await client.GetFromJsonAsync<MapDetail>(route))!;
            var progress = (await client.GetFromJsonAsync<MapProgress>(route + "/progress"))!;
            Assert.Equal(map.Markers.Count, detail.Pins.Count);
            foreach (var pin in map.Markers)
            {
                var projected = detail.Pins.Single(p => p.Id == pin.Id); Assert.Equal(pin.X, projected.X); Assert.Equal(pin.Y, projected.Y);
                Assert.Equal(pin.ItemId, projected.ItemId); Assert.Equal(pin.Label, projected.Label);
                var state = progress.Pins.Single(p => p.Id == pin.Id);
                Assert.Equal(completed.Contains(pin.ItemId) ? "completed" : "unknown", state.Status);
                Assert.Equal("#tracker/" + pin.TrackerId, state.TrackerUrl); Assert.Equal("#achievement/steam/1850570/035", state.AchievementUrl);
            }
        }
    }
}
