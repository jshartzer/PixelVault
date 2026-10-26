using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace PixelVault.Companion.Tests;

public sealed partial class CompanionTests
{
    [Fact]
    public async Task AutomaticChangesAreScopedDeduplicatedAndPauseBeforeApplyingInFlightResults()
    {
        using var fixture=new WorkspaceCase();SourceForTest(fixture);using var host=new Host(false,root:fixture.Root);using var owner=host.Client();await Setup(host,owner);var pair=PairForTest(fixture,host);
        using var pc=host.Client();pc.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",pair.Token);
        var tasks=pair.Sources.Select(s=>new ConnectorTask(s.Definition.Id,s.Binding.Id,s.Revision)).ToArray();var change=new AutomaticSaveChange(new string('A',64),tasks);
        Assert.Equal(HttpStatusCode.Conflict,(await pc.PostAsJsonAsync("/connector/v1/automatic",change)).StatusCode);
        host.Services.GetRequiredService<ConnectorSyncState>().EnableLibraryAndAutomatic();
        var response=await pc.PostAsJsonAsync("/connector/v1/automatic",change);var receipt=(await response.Content.ReadFromJsonAsync<AutomaticReceipt>())!;
        Assert.Equal("reading",receipt.Status);
        var again=(await (await pc.PostAsJsonAsync("/connector/v1/automatic",change)).Content.ReadFromJsonAsync<AutomaticReceipt>())!;Assert.Equal(receipt.Id,again.Id);
        var work=(await pc.GetFromJsonAsync<ConnectorWork>("/connector/v1/work"))!;
        var observation=new ConnectorObservation(work.Lease,new string('B',64),PcSnapshot(Approved,4),null);
        Assert.Equal(HttpStatusCode.OK,(await pc.PostAsJsonAsync($"/connector/v1/jobs/{work.Id}/{TrackerId}",observation)).StatusCode);
        var count=fixture.Repo().History(TrackerId,Approved).Count;
        var complete=(await (await pc.PostAsJsonAsync("/connector/v1/automatic",change)).Content.ReadFromJsonAsync<AutomaticReceipt>())!;Assert.Equal("complete",complete.Status);Assert.Equal(count,fixture.Repo().History(TrackerId,Approved).Count);
        var next=(await (await pc.PostAsJsonAsync("/connector/v1/automatic",change with{Signature=new string('C',64)})).Content.ReadFromJsonAsync<AutomaticReceipt>())!;
        var pending=(await pc.GetFromJsonAsync<ConnectorWork>("/connector/v1/work"))!;
        await owner.PostAsJsonAsync("/api/v1/refresh/automatic",new SetAutomaticUpdates(false));
        var paused=await pc.PostAsJsonAsync($"/connector/v1/jobs/{pending.Id}/{TrackerId}",new ConnectorObservation(pending.Lease,new string('D',64),PcSnapshot(Approved,5),null));
        Assert.Equal("paused",(await paused.Content.ReadFromJsonAsync<GameRefreshView>())!.Status);Assert.Equal(count,fixture.Repo().History(TrackerId,Approved).Count);
    }
    [Fact]
    public async Task AutomaticManualConflictPausesFurtherChangesUntilOwnerReviews()
    {
        using var fixture=new WorkspaceCase();SourceForTest(fixture);fixture.Repo().SetManualProgress(TrackerId,Approved,"count",3);
        using var host=new Host(false,root:fixture.Root);using var owner=host.Client();await Setup(host,owner);var pair=PairForTest(fixture,host);using var pc=host.Client();pc.DefaultRequestHeaders.Authorization=new("Bearer",pair.Token);
        host.Services.GetRequiredService<ConnectorSyncState>().EnableLibraryAndAutomatic();var tasks=pair.Sources.Select(s=>new ConnectorTask(s.Definition.Id,s.Binding.Id,s.Revision)).ToArray();
        await pc.PostAsJsonAsync("/connector/v1/automatic",new AutomaticSaveChange(new string('A',64),tasks));var work=(await pc.GetFromJsonAsync<ConnectorWork>("/connector/v1/work"))!;
        var response=await pc.PostAsJsonAsync($"/connector/v1/jobs/{work.Id}/{TrackerId}",new ConnectorObservation(work.Lease,new string('B',64),PcSnapshot(Approved,5),null));var review=(await response.Content.ReadFromJsonAsync<GameRefreshView>())!;Assert.Equal("review",review.Status);
        var paused=(await (await pc.PostAsJsonAsync("/connector/v1/automatic",new AutomaticSaveChange(new string('C',64),tasks))).Content.ReadFromJsonAsync<AutomaticReceipt>())!;Assert.Equal("paused-review",paused.Status);Assert.Equal(work.Id,paused.Id);
        await owner.PostAsJsonAsync($"/api/v1/refresh/{work.Id}/review",new ReviewGameRefresh(review.Version,false));
        Assert.Equal(3,fixture.Repo().Load(TrackerId,Approved).Single(s=>s.Item.Id=="count").Current);Assert.Equal("Original note",fixture.Repo().Load(TrackerId,Approved).Single(s=>s.Item.Id=="first").Note);
        var resumed=(await (await pc.PostAsJsonAsync("/connector/v1/automatic",new AutomaticSaveChange(new string('C',64),tasks))).Content.ReadFromJsonAsync<AutomaticReceipt>())!;Assert.Equal("reading",resumed.Status);Assert.NotEqual(work.Id,resumed.Id);
    }
    [Fact]
    public async Task LibraryUploadNeedsExplicitGrantAllAssetsAndMatchingWorkspaceThenReloadsWithoutRestart()
    {
        using var fixture=new WorkspaceCase();SourceForTest(fixture);using var host=new Host(false,root:fixture.Root);using var owner=host.Client();await Setup(host,owner);var pair=PairForTest(fixture,host);using var pc=host.Client();pc.DefaultRequestHeaders.Authorization=new("Bearer",pair.Token);
        var bytes=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aPj0AAAAASUVORK5CYII=");var asset=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()+".png";
        var catalog=new GameCatalogue(1,pair.WorkspaceId,DateTime.UtcNow,[new("library","new-game","A newly indexed game","Xbox",asset,null,new(LastScreenshotUtc:DateTime.UtcNow.Ticks))]);
        Assert.Equal(HttpStatusCode.Conflict,(await pc.PostAsJsonAsync("/connector/v1/library/prepare",catalog)).StatusCode);
        host.Services.GetRequiredService<ConnectorSyncState>().EnableLibraryAndAutomatic();
        Assert.Equal(HttpStatusCode.BadRequest,(await pc.PostAsJsonAsync("/connector/v1/library/prepare",catalog with{WorkspaceId="wrong"})).StatusCode);
        var unapproved=new LibraryGame("retroachievements","99","Emulated inventory game","Emulation",null,null,new(AchievementUnlocked:1,AchievementTotal:10));
        Assert.Equal(HttpStatusCode.BadRequest,(await pc.PostAsJsonAsync("/connector/v1/library/prepare",catalog with{Games=[unapproved]})).StatusCode);
        catalog.Games.Add(unapproved with{Stats=new(Captures:1)});
        _=await owner.GetStringAsync("/api/v1/games");
        var plan=(await (await pc.PostAsJsonAsync("/connector/v1/library/prepare",catalog)).Content.ReadFromJsonAsync<LibraryUploadPlan>())!;Assert.Contains(asset,plan.MissingAssets);
        Assert.Equal(HttpStatusCode.Conflict,(await pc.PostAsync($"/connector/v1/library/{plan.Id}/commit",null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await pc.PostAsync($"/connector/v1/library/{plan.Id}/assets/{asset}",new ByteArrayContent(new byte[20]))).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await pc.PostAsync($"/connector/v1/library/{plan.Id}/assets/{asset}",new ByteArrayContent(bytes))).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await pc.PostAsync($"/connector/v1/library/{plan.Id}/commit",null)).StatusCode);
        Assert.Contains("A newly indexed game",await owner.GetStringAsync("/api/v1/games"));
        var current=(await (await pc.PostAsJsonAsync("/connector/v1/library/prepare",catalog with{ExportedUtc=DateTime.UtcNow.AddSeconds(1)})).Content.ReadFromJsonAsync<LibraryUploadPlan>())!;Assert.True(current.Current);
        Assert.Equal(HttpStatusCode.OK,(await owner.GetAsync("/api/v1/games/library/new-game/art/cover")).StatusCode);
    }
}
