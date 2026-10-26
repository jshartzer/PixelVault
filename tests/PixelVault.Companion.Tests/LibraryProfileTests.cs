using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace PixelVault.Companion.Tests;

public sealed partial class CompanionTests
{
    static readonly JsonSerializerOptions LibraryJson=new(JsonSerializerDefaults.Web);
    [Fact]
    public async Task BrowserShellAndScriptsCannotStayCachedAcrossAnUpgrade()
    {
        using var host=new Host();using var client=host.Client();
        foreach(var path in new[]{"/","/?v=0.13.1","/index.html","/app.js?v=0.13.1","/library.js?v=0.13.1","/navigation.js?v=0.13.1","/updates.js?v=0.13.1","/app.css?v=0.13.1","/profile.css?v=0.13.1","/screenshots.js?v=0.13.1","/screenshots.css?v=0.13.1","/manifest.webmanifest"})
        {
            using var response=await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK,response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore,path);
            Assert.True(response.Headers.CacheControl?.NoCache,path);
            Assert.True(response.Headers.CacheControl?.MustRevalidate,path);
        }
        var html=await client.GetStringAsync("/?v=0.13.1");
        Assert.Contains("/app.js?v=0.13.1",html);
        Assert.Contains("/profile.css?v=0.13.1",html);
        Assert.Contains("id=\"game-profile\"",html);
    }
    static void LibraryFixture(string root,string? scope=null,string? image=null)
    {
        var workspace=new TrackerWorkspace(new(){DataRoot=root});var folder=Path.Combine(root,"library");Directory.CreateDirectory(Path.Combine(folder,"assets"));
        var asset=image??new string('a',64)+".png";
        if(image is null)File.WriteAllBytes(Path.Combine(folder,"assets",asset),Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aPj0AAAAASUVORK5CYII="));
        File.WriteAllText(Path.Combine(folder,"catalog.json"),JsonSerializer.Serialize(new GameCatalogue(1,scope??workspace.Identity,DateTime.UtcNow,
        [new("steam","42","A library journey","Steam",asset,asset,new(150,2,8,null,DateTime.UtcNow.Ticks,12)),
         new("library","local-one","A game without trackers","Xbox",null,null,new())]),LibraryJson));
    }
    [Fact]
    public async Task LibraryIncludesCoversCompactStatsAndGamesWithoutTrackersWithAuthenticatedArtwork()
    {
        using var fixture=new WorkspaceCase();fixture.Prepare();LibraryFixture(fixture.Root);using var host=new Host(false,root:fixture.Root);using var client=host.Client();
        foreach(var path in new[]{"/api/v1/games","/api/v1/games/steam/42","/api/v1/games/steam/42/art/cover","/api/v1/preferences/game-sections"})
            Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync(path)).StatusCode);
        await Setup(host,client);var games=await client.GetFromJsonAsync<JsonElement>("/api/v1/games");
        Assert.Contains(games.EnumerateArray(),g=>g.GetProperty("gameId").GetString()=="local-one"&&g.GetProperty("trackerCount").GetInt32()==0);
        var game=await client.GetFromJsonAsync<JsonElement>("/api/v1/games/steam/42");
        Assert.Equal("A library journey",game.GetProperty("title").GetString());Assert.Equal(150,game.GetProperty("stats").GetProperty("playtimeMinutes").GetInt32());
        Assert.Equal(2,game.GetProperty("stats").GetProperty("achievementUnlocked").GetInt32());Assert.Equal(8,game.GetProperty("stats").GetProperty("achievementTotal").GetInt32());Assert.Equal(1,game.GetProperty("trackerCount").GetInt32());
        Assert.DoesNotContain(fixture.Root,game.GetRawText());Assert.DoesNotContain(Approved,game.GetRawText());
        var cover=await client.GetAsync(game.GetProperty("coverUrl").GetString());Assert.Equal(HttpStatusCode.OK,cover.StatusCode);Assert.Equal("image/png",cover.Content.Headers.ContentType!.MediaType);
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync("/api/v1/games/steam/42/art/database")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync("/api/v1/games/steam/999/art/cover")).StatusCode);
    }
    [Fact]
    public async Task DifferentWorkspaceCannotReadOldCatalogueStatsOrArtwork()
    {
        using var fixture=new WorkspaceCase();fixture.Prepare();LibraryFixture(fixture.Root,new string('0',64));using var host=new Host(false,root:fixture.Root);using var client=host.Client();await Setup(host,client);
        var games=await client.GetStringAsync("/api/v1/games");Assert.DoesNotContain("A library journey",games);Assert.DoesNotContain("local-one",games);
        var game=await client.GetFromJsonAsync<JsonElement>("/api/v1/games/steam/42");Assert.Equal(JsonValueKind.Null,game.GetProperty("coverUrl").ValueKind);Assert.Equal(JsonValueKind.Null,game.GetProperty("stats").GetProperty("playtimeMinutes").ValueKind);
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync("/api/v1/games/steam/42/art/cover")).StatusCode);
    }
    [Theory]
    [InlineData("../../companion.sqlite")]
    [InlineData("/etc/passwd")]
    [InlineData("https://untrusted.example/cover.jpg")]
    public async Task CatalogueCannotTurnArtworkRoutesIntoArbitraryFileOrNetworkAccess(string image)
    {
        using var fixture=new WorkspaceCase();fixture.Prepare();LibraryFixture(fixture.Root,image:image);using var host=new Host(false,root:fixture.Root);using var client=host.Client();await Setup(host,client);
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync("/api/v1/games/steam/42/art/cover")).StatusCode);
    }
    [Fact]
    public async Task SectionOrderIsOwnerAuthenticatedCsrfProtectedAndPersistsAcrossRestart()
    {
        using var fixture=new WorkspaceCase();fixture.Prepare();string cookie;
        using(var host=new Host(false,root:fixture.Root))
        using(var client=host.Client())
        {
            cookie=await Setup(host,client);var before=(await client.GetFromJsonAsync<GameSectionOrder>("/api/v1/preferences/game-sections"))!;
            Assert.Equal(new[]{"trackers","achievements","screenshots"},before.Sections);
            client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync("/api/v1/preferences/game-sections",new SaveGameSectionOrder(["screenshots","trackers","achievements"],before.Version))).StatusCode);
            await Token(client);var response=await client.PostAsJsonAsync("/api/v1/preferences/game-sections",new SaveGameSectionOrder(["screenshots","trackers","achievements"],before.Version));Assert.Equal(HttpStatusCode.OK,response.StatusCode);
            var saved=(await response.Content.ReadFromJsonAsync<GameSectionOrder>())!;Assert.NotEqual(before.Version,saved.Version);
            Assert.Equal("Original note",fixture.Repo().Load(TrackerId,Approved).Single(s=>s.Item.Id=="first").Note);
        }
        using var restarted=new Host(false,root:fixture.Root);using var next=restarted.Client();next.DefaultRequestHeaders.Add("Cookie",cookie);
        var order=await next.GetFromJsonAsync<GameSectionOrder>("/api/v1/preferences/game-sections");Assert.Equal(new[]{"screenshots","trackers","achievements"},order!.Sections);
    }
    [Fact]
    public async Task ConcurrentLayoutEditsConflictAndInvalidOrAbaWritesCannotReplaceAUsersOrder()
    {
        using var fixture=new WorkspaceCase();fixture.Prepare();using var host=new Host(false,root:fixture.Root);using var client=host.Client();await Setup(host,client);
        var initial=(await client.GetFromJsonAsync<GameSectionOrder>("/api/v1/preferences/game-sections"))!;
        foreach(var bad in new[]{new List<string>{"trackers","trackers","screenshots"},["trackers","screenshots"],["trackers","achievements","../anything"]})
            Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync("/api/v1/preferences/game-sections",new SaveGameSectionOrder(bad,initial.Version))).StatusCode);
        var writes=await Task.WhenAll(client.PostAsJsonAsync("/api/v1/preferences/game-sections",new SaveGameSectionOrder(["screenshots","trackers","achievements"],initial.Version)),client.PostAsJsonAsync("/api/v1/preferences/game-sections",new SaveGameSectionOrder(["achievements","screenshots","trackers"],initial.Version)));
        Assert.Single(writes,r=>r.StatusCode==HttpStatusCode.OK);Assert.Single(writes,r=>r.StatusCode==HttpStatusCode.Conflict);
        var current=(await client.GetFromJsonAsync<GameSectionOrder>("/api/v1/preferences/game-sections"))!;
        Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync("/api/v1/preferences/game-sections",new SaveGameSectionOrder(initial.Sections.ToList(),current.Version))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync("/api/v1/preferences/game-sections",new SaveGameSectionOrder(["screenshots","trackers","achievements"],initial.Version))).StatusCode);
    }
}
