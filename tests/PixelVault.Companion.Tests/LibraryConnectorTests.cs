using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PixelVaultNative;
using Xunit;

namespace PixelVault.Companion.Tests;

public sealed partial class CompanionTests
{
    [Fact]
    public void ConsoleConsolidationNormalizesPunctuationButRetainsEditionAndSequelIdentity()
    {
        Assert.Equal(GameLibrary.GroupKey("A Plague Tale- Innocence"),GameLibrary.GroupKey("A Plague Tale: Innocence"));
        Assert.Equal(GameLibrary.GroupKey("Death Stranding Director’s Cut"),GameLibrary.GroupKey("DEATH STRANDING DIRECTOR'S CUT"));
        Assert.NotEqual(GameLibrary.GroupKey("Death Stranding"),GameLibrary.GroupKey("Death Stranding 2"));
        Assert.NotEqual(GameLibrary.GroupKey("BioShock"),GameLibrary.GroupKey("BioShock Remastered"));
    }
    [Fact]
    public async Task LibrarySortAndPinsAreAuthenticatedPersistentAndIndependentConcurrentChangesMerge()
    {
        using var fixture=new WorkspaceCase();fixture.Prepare();LibraryFixture(fixture.Root);string cookie;
        using(var host=new Host(false,root:fixture.Root))using(var client=host.Client())
        {
            Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync("/api/v1/preferences/library")).StatusCode);
            cookie=await Setup(host,client);var initial=await client.GetFromJsonAsync<LibraryLayout>("/api/v1/preferences/library");Assert.Equal("screenshot-newest",initial!.Sort);
            client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync("/api/v1/preferences/library/sort",new SaveLibrarySort("title-az"))).StatusCode);await Token(client);
            Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync("/api/v1/preferences/library/sort",new SaveLibrarySort("unknown"))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound,(await client.PostAsJsonAsync("/api/v1/preferences/library/pins",new SaveLibraryPin("not-a-game",true))).StatusCode);
            var first=GameLibrary.GroupKey("A library journey");var second=GameLibrary.GroupKey("A game without trackers");
            var replies=await Task.WhenAll(client.PostAsJsonAsync("/api/v1/preferences/library/pins",new SaveLibraryPin(first,true)),client.PostAsJsonAsync("/api/v1/preferences/library/pins",new SaveLibraryPin(second,true)),client.PostAsJsonAsync("/api/v1/preferences/library/sort",new SaveLibrarySort("title-za")));
            Assert.All(replies,r=>Assert.Equal(HttpStatusCode.OK,r.StatusCode));
            await client.PostAsJsonAsync("/api/v1/preferences/library/pins",new SaveLibraryPin(first,true));
            var saved=await client.GetFromJsonAsync<LibraryLayout>("/api/v1/preferences/library");Assert.Equal(2,saved!.Pins.Count);Assert.Equal("title-za",saved.Sort);
        }
        using var again=new Host(false,root:fixture.Root);using var next=again.Client();next.DefaultRequestHeaders.Add("Cookie",cookie);
        var layout=await next.GetFromJsonAsync<LibraryLayout>("/api/v1/preferences/library");Assert.Equal(2,layout!.Pins.Count);Assert.Equal("title-za",layout.Sort);
    }
    static ConnectorPairing PairForTest(WorkspaceCase fixture,Host host)
    {
        var file=Path.Combine(fixture.Base,"pc-pair.json");var connector=host.Services.GetRequiredService<PcConnector>();connector.Pair("http://localhost/",file);connector.Heartbeat();
        return JsonSerializer.Deserialize<ConnectorPairing>(File.ReadAllText(file),AchievementTrackingCatalog.Json)!;
    }
    static void SourceForTest(WorkspaceCase fixture)
    {
        fixture.Desktop.SaveSource(new(){TrackerId=TrackerId,Account=Approved,AdapterId="pixelvault.json-file",AdapterVersion="1",Location=Path.Combine(fixture.Base,"observations.json")});fixture.Prepare();
    }
    static string PcSnapshot(string account,int count,long? observed=null)=>JsonSerializer.Serialize(new AchievementTrackerSnapshot{
        TrackerId=TrackerId,Provider="steam",GameId="42",Account=account,Source="PC save",ObservedUtcTicks=observed??DateTime.UtcNow.Ticks,
        Items=new(){new(){ItemId="first",Current=1},new(){ItemId="count",Current=count}}},AchievementTrackingCatalog.Json);
    static async Task<GameRefreshView> QueueRefresh(HttpClient owner)=> (await (await owner.PostAsJsonAsync("/api/v1/refresh",new RequestGameRefresh(GameLibrary.GroupKey("Test journey"),Guid.NewGuid().ToString()))).Content.ReadFromJsonAsync<GameRefreshView>())!;
    [Fact]
    public async Task OfflinePcReturnsImmediatelyWithoutCreatingARefreshOrChangingProgress()
    {
        using var fixture=new WorkspaceCase();SourceForTest(fixture);using var host=new Host(false,root:fixture.Root);using var owner=host.Client();await Setup(host,owner);
        host.Services.GetRequiredService<PcConnector>().Pair("http://localhost/",Path.Combine(fixture.Base,"offline-pc.json"));
        var count=fixture.Repo().History(TrackerId,Approved).Count;
        Assert.Equal(HttpStatusCode.Conflict,(await owner.PostAsJsonAsync("/api/v1/refresh",new RequestGameRefresh(GameLibrary.GroupKey("Test journey"),Guid.NewGuid().ToString()))).StatusCode);
        Assert.Equal(count,fixture.Repo().History(TrackerId,Approved).Count);
    }
    [Fact]
    public async Task PairedPcRefreshIsScopedRetrySafeAndPreservesNotesAndOtherAccounts()
    {
        using var fixture=new WorkspaceCase();SourceForTest(fixture);using var host=new Host(false,root:fixture.Root);using var owner=host.Client();await Setup(host,owner);var pair=PairForTest(fixture,host);
        using var pc=host.Client();Assert.Equal(HttpStatusCode.Unauthorized,(await owner.GetAsync("/connector/v1/work")).StatusCode);
        pc.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",pair.Token);Assert.Equal(HttpStatusCode.Unauthorized,(await pc.GetAsync("/api/v1/games")).StatusCode);
        var job=await QueueRefresh(owner);var work=await pc.GetFromJsonAsync<ConnectorWork>("/connector/v1/work");Assert.Equal(job.Id,work!.Id);
        var replay=await pc.GetFromJsonAsync<ConnectorWork>("/connector/v1/work");Assert.Equal(work.Lease,replay!.Lease);
        var request=new ConnectorObservation(work.Lease,new string('A',64),PcSnapshot(Approved,4),null);
        var response=await pc.PostAsJsonAsync($"/connector/v1/jobs/{work.Id}/{TrackerId}",request);Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var result=await response.Content.ReadFromJsonAsync<GameRefreshView>();Assert.Equal("complete",result!.Status);
        var history=fixture.Repo().History(TrackerId,Approved).Count;
        Assert.Equal(HttpStatusCode.OK,(await pc.PostAsJsonAsync($"/connector/v1/jobs/{work.Id}/{TrackerId}",request)).StatusCode);Assert.Equal(history,fixture.Repo().History(TrackerId,Approved).Count);
        Assert.Equal(4,fixture.Repo().Load(TrackerId,Approved).Single(s=>s.Item.Id=="count").Current);Assert.Equal("Original note",fixture.Repo().Load(TrackerId,Approved).Single(s=>s.Item.Id=="first").Note);
        Assert.Equal("Private other account note",fixture.Repo().Load(TrackerId,Other).Single(s=>s.Item.Id=="first").Note);
        Assert.DoesNotContain(Approved,await owner.GetStringAsync("/api/v1/refresh/"+work.Id));Assert.DoesNotContain(fixture.Base,await owner.GetStringAsync("/api/v1/refresh/"+work.Id));
    }
    [Fact]
    public async Task ConnectorRejectsWrongAccountChangedBindingAndRevokedTokenWithoutProgressWrites()
    {
        using var fixture=new WorkspaceCase();SourceForTest(fixture);using var host=new Host(false,root:fixture.Root);using var owner=host.Client();await Setup(host,owner);var pair=PairForTest(fixture,host);using var pc=host.Client();pc.DefaultRequestHeaders.Authorization=new("Bearer",pair.Token);
        var job=await QueueRefresh(owner);var work=(await pc.GetFromJsonAsync<ConnectorWork>("/connector/v1/work"))!;var history=fixture.Repo().History(TrackerId,Approved).Count;
        var invalid=new ConnectorObservation(work.Lease,new string('B',64),PcSnapshot(Other,5),null);
        Assert.Equal(HttpStatusCode.BadRequest,(await pc.PostAsJsonAsync($"/connector/v1/jobs/{work.Id}/{TrackerId}",invalid)).StatusCode);Assert.Equal(history,fixture.Repo().History(TrackerId,Approved).Count);
        fixture.Repo().SaveSource(new(){TrackerId=TrackerId,Account=Approved,AdapterId="pixelvault.json-file",AdapterVersion="1",Location=Path.Combine(fixture.Base,"changed.json")});
        Assert.Equal(HttpStatusCode.Conflict,(await pc.PostAsJsonAsync($"/connector/v1/jobs/{work.Id}/{TrackerId}",invalid with{SnapshotJson=PcSnapshot(Approved,5)})).StatusCode);
        host.Services.GetRequiredService<PcConnector>().Revoke();Assert.Equal(HttpStatusCode.Unauthorized,(await pc.GetAsync("/connector/v1/work")).StatusCode);Assert.Equal(history,fixture.Repo().History(TrackerId,Approved).Count);
    }
    [Fact]
    public async Task ManualConflictsRequireReviewAndOlderOrUndoneRefreshesCannotSilentlyOverrideProgress()
    {
        using var fixture=new WorkspaceCase();SourceForTest(fixture);fixture.Repo().SetManualProgress(TrackerId,Approved,"count",3);
        using var host=new Host(false,root:fixture.Root);using var owner=host.Client();await Setup(host,owner);var pair=PairForTest(fixture,host);using var pc=host.Client();pc.DefaultRequestHeaders.Authorization=new("Bearer",pair.Token);
        var job=await QueueRefresh(owner);var work=(await pc.GetFromJsonAsync<ConnectorWork>("/connector/v1/work"))!;
        var response=await pc.PostAsJsonAsync($"/connector/v1/jobs/{work.Id}/{TrackerId}",new ConnectorObservation(work.Lease,new string('C',64),PcSnapshot(Approved,5),null));
        var review=(await response.Content.ReadFromJsonAsync<GameRefreshView>())!;Assert.Equal("review",review.Status);Assert.Equal(2,fixture.Repo().Load(TrackerId,Approved).Single(s=>s.Item.Id=="count").ReportedCurrent);
        fixture.Repo().SaveNote(TrackerId,Approved,"count","A note added after preview");
        var stale=await owner.PostAsJsonAsync($"/api/v1/refresh/{work.Id}/review",new ReviewGameRefresh(review.Version,false));var newer=(await stale.Content.ReadFromJsonAsync<GameRefreshView>())!;Assert.Equal("review",newer.Status);
        var apply=await owner.PostAsJsonAsync($"/api/v1/refresh/{work.Id}/review",new ReviewGameRefresh(newer.Version,false));Assert.Equal("complete",(await apply.Content.ReadFromJsonAsync<GameRefreshView>())!.Status);
        var final=fixture.Repo().Load(TrackerId,Approved).Single(s=>s.Item.Id=="count");Assert.Equal(3,final.Current);Assert.Equal(5,final.ReportedCurrent);Assert.Equal("A note added after preview",final.Note);
        var second=await QueueRefresh(owner);var next=(await pc.GetFromJsonAsync<ConnectorWork>("/connector/v1/work"))!;
        var older=await pc.PostAsJsonAsync($"/connector/v1/jobs/{next.Id}/{TrackerId}",new ConnectorObservation(next.Lease,new string('D',64),PcSnapshot(Approved,0,DateTime.UtcNow.AddDays(-5).Ticks),null));
        Assert.Equal("review",(await older.Content.ReadFromJsonAsync<GameRefreshView>())!.Status);Assert.Equal(5,fixture.Repo().Load(TrackerId,Approved).Single(s=>s.Item.Id=="count").ReportedCurrent);
    }
}
