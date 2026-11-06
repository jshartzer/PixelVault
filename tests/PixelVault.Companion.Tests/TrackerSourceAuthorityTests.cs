using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PixelVaultNative;
using Xunit;

namespace PixelVault.Companion.Tests;
public sealed partial class CompanionTests
{
    [Fact] public void SourceRevisionUsesCanonicalNewlinesAndRetainsDefaultPolicyShape()
    {
        var binding=new TrackerSourceBinding{Id="existing",TrackerId="fixture",Account=Approved,AdapterId="pixelvault.json-file",AdapterVersion="1",Location=@"C:\approved\observations.json"};
        var canonical=JsonSerializer.Serialize(binding,new JsonSerializerOptions(AchievementTrackingCatalog.Json){NewLine="\n"});
        var expected=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(canonical)));
        Assert.DoesNotContain("ReplaceManualProgress",canonical);Assert.Equal(expected,PcConnector.Revision(binding));
        binding.ReplaceManualProgress=true;Assert.NotEqual(expected,PcConnector.Revision(binding));
    }
    static void AuthoritativeSource(WorkspaceCase fixture)
    {
        fixture.Desktop.SaveSource(new(){TrackerId=TrackerId,Account=Approved,AdapterId="pixelvault.json-file",AdapterVersion="1",Location=Path.Combine(fixture.Base,"observations.json"),ReplaceManualProgress=true});fixture.Prepare();
    }
    [Fact] public async Task BoundAuthorityReplacesWrongChecksWhileUnknownNotesAndOtherAccountsRemainUntouched()
    {
        using var fixture=new WorkspaceCase();AuthoritativeSource(fixture);var repo=fixture.Repo();repo.SetManualProgress(TrackerId,Approved,"count",3);repo.SetManualProgress(TrackerId,Approved,"first",1);
        using var host=new Host(false,root:fixture.Root);using var owner=host.Client();await Setup(host,owner);var pair=PairForTest(fixture,host);Assert.True(pair.Sources.Single().Binding.ReplaceManualProgress);
        using var pc=host.Client();pc.DefaultRequestHeaders.Authorization=new("Bearer",pair.Token);await QueueRefresh(owner);var work=(await pc.GetFromJsonAsync<ConnectorWork>("/connector/v1/work"))!;
        var snapshot=JsonSerializer.Deserialize<AchievementTrackerSnapshot>(PcSnapshot(Approved,0))!;snapshot.Items.Single(i=>i.ItemId=="first").Current=null;
        var response=await pc.PostAsJsonAsync($"/connector/v1/jobs/{work.Id}/{TrackerId}",new ConnectorObservation(work.Lease,new string('A',64),JsonSerializer.Serialize(snapshot),null));
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.Equal("complete",(await response.Content.ReadFromJsonAsync<GameRefreshView>())!.Status);
        var count=repo.Load(TrackerId,Approved).Single(i=>i.Item.Id=="count");Assert.Equal(0,count.Current);Assert.Null(count.ManualCurrent);
        var unknown=repo.Load(TrackerId,Approved).Single(i=>i.Item.Id=="first");Assert.Equal(1,unknown.ManualCurrent);Assert.Equal("Original note",unknown.Note);
        Assert.Equal("Private other account note",repo.Load(TrackerId,Other).Single(i=>i.Item.Id=="first").Note);
        var view=await owner.GetFromJsonAsync<JsonElement>("/api/v1/trackers/"+TrackerId);Assert.True(view.GetProperty("replaceManualProgress").GetBoolean());
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task SourceAuthorityStillRequiresReviewForOlderAndUndoneRefreshes(bool undo)
    {
        using var fixture=new WorkspaceCase();AuthoritativeSource(fixture);using var host=new Host(false,root:fixture.Root);using var owner=host.Client();await Setup(host,owner);var pair=PairForTest(fixture,host);
        using var pc=host.Client();pc.DefaultRequestHeaders.Authorization=new("Bearer",pair.Token);await QueueRefresh(owner);var work=(await pc.GetFromJsonAsync<ConnectorWork>("/connector/v1/work"))!;
        var json=PcSnapshot(Approved,4);var first=(await pc.PostAsJsonAsync($"/connector/v1/jobs/{work.Id}/{TrackerId}",new ConnectorObservation(work.Lease,new string('B',64),json,null)));
        Assert.Equal("complete",(await first.Content.ReadFromJsonAsync<GameRefreshView>())!.Status);
        var repo=fixture.Repo();if(undo)repo.Undo(TrackerId,Approved,repo.History(TrackerId,Approved).First(h=>h.Kind=="refresh").Id);
        var before=repo.Load(TrackerId,Approved).Single(i=>i.Item.Id=="count").Current;
        await QueueRefresh(owner);var next=(await pc.GetFromJsonAsync<ConnectorWork>("/connector/v1/work"))!;
        var result=await pc.PostAsJsonAsync($"/connector/v1/jobs/{next.Id}/{TrackerId}",new ConnectorObservation(next.Lease,new string('C',64),undo?json:PcSnapshot(Approved,0,DateTime.UtcNow.AddDays(-5).Ticks),null));
        Assert.Equal("review",(await result.Content.ReadFromJsonAsync<GameRefreshView>())!.Status);Assert.Equal(before,repo.Load(TrackerId,Approved).Single(i=>i.Item.Id=="count").Current);
    }
    [Fact] public async Task PublicationCannotInheritOverwriteAuthorityFromAnotherApprovedTracker()
    {
        using var fixture=new WorkspaceCase();fixture.Desktop.SaveSource(new(){TrackerId=TrackerId,Account=Approved,AdapterId="death-stranding-dc.fabrication",AdapterVersion="1",Location=@"C:\approved-save",GameDirectory=@"C:\approved-game",ReplaceManualProgress=true});fixture.Prepare();
        using var host=new Host(false,root:fixture.Root);using var owner=host.Client();await Setup(host,owner);var pair=PairForTest(fixture,host);host.Services.GetRequiredService<ConnectorSyncState>().EnableLibraryAndAutomatic();
        using var pc=host.Client();pc.DefaultRequestHeaders.Authorization=new("Bearer",pair.Token);
        var d=new AchievementTrackerDefinition{Id="custom.no-authority-inheritance",Provider="steam",GameId="42",GameTitle="Test journey",Title="Another tracker",Items=new(){new(){Id="one",Name="One"}}};
        var candidate=JsonSerializer.Deserialize<TrackerSourceBinding>(JsonSerializer.Serialize(pair.Sources.Single().Binding))!;candidate.TrackerId=d.Id;
        var response=await pc.PostAsJsonAsync("/connector/v1/trackers",new TrackerPublication(pair.WorkspaceId,[new(d,candidate)]));Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var refused=(await response.Content.ReadFromJsonAsync<TrackerPublicationResult>())!;Assert.Equal(1,refused.Added);Assert.Empty(refused.Sources);Assert.Null(fixture.Repo().Source(d.Id,Approved));
        candidate.ReplaceManualProgress=false;
        var ordinary=(await(await pc.PostAsJsonAsync("/connector/v1/trackers",new TrackerPublication(pair.WorkspaceId,[new(d,candidate)]))).Content.ReadFromJsonAsync<TrackerPublicationResult>())!;
        Assert.Single(ordinary.Sources);Assert.False(ordinary.Sources[0].Binding.ReplaceManualProgress);
    }
}
