using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PixelVaultNative;
using Xunit;

namespace PixelVault.Companion.Tests;
public sealed partial class CompanionTests
{
    [Fact]
    public async Task PublishedRenameBootstrapsFromOriginalDigestAndPreservesAllTrackerDataAndPermissions()
    {
        using var fixture=new WorkspaceCase();
        fixture.Desktop.SaveSource(new(){TrackerId=TrackerId,Account=Approved,AdapterId="death-stranding-dc.fabrication",AdapterVersion="1",Location=@"C:\approved-save",GameDirectory=@"C:\approved-game"});fixture.Prepare();
        using var host=new Host(false,root:fixture.Root);using var owner=host.Client();await Setup(host,owner);var pair=PairForTest(fixture,host);
        host.Services.GetRequiredService<ConnectorSyncState>().EnableLibraryAndAutomatic();
        using var pc=host.Client();pc.DefaultRequestHeaders.Authorization=new("Bearer",pair.Token);
        var d=new AchievementTrackerDefinition{Id="custom.title-sync",Provider="steam",GameId="42",GameTitle="Test journey",Title="Original name",Items=new(){new(){Id="count",Name="Counter",Target=100}}};
        var binding=JsonSerializer.Deserialize<TrackerSourceBinding>(JsonSerializer.Serialize(pair.Sources.Single().Binding))!;binding.TrackerId=d.Id;
        var published=(await(await pc.PostAsJsonAsync("/connector/v1/trackers",new TrackerPublication(pair.WorkspaceId,[new(d,binding)]))).Content.ReadFromJsonAsync<TrackerPublicationResult>())!;
        var source=Assert.Single(published.Sources);
        var repo=fixture.Repo();repo.SetManualProgress(d.Id,Approved,"count",67);repo.SaveNote(d.Id,Approved,"count","Keep this note");
        var state=JsonSerializer.Serialize(repo.Load(d.Id,Approved));var history=JsonSerializer.Serialize(repo.History(d.Id,Approved));
        var savedSource=JsonSerializer.Serialize(repo.Source(d.Id,Approved));
        TrackerTitles Names(string name)=>new(pair.WorkspaceId,[new(d.Id,d.Provider,d.GameId,name)]);
        var result=(await(await pc.PostAsJsonAsync("/connector/v1/tracker-titles",Names("Road Building"))).Content.ReadFromJsonAsync<TrackerTitleResult>())!;
        Assert.Equal(new TrackerTitleResult(1,0),result);
        Assert.Equal("Road Building",repo.Definitions().Single(x=>x.Id==d.Id).Title);
        Assert.Equal(state,JsonSerializer.Serialize(repo.Load(d.Id,Approved)));Assert.Equal(history,JsonSerializer.Serialize(repo.History(d.Id,Approved)));
        Assert.Equal(savedSource,JsonSerializer.Serialize(repo.Source(d.Id,Approved)));
        Assert.Equal(new TrackerTitleResult(0,0),(await(await pc.PostAsJsonAsync("/connector/v1/tracker-titles",Names("Road Building"))).Content.ReadFromJsonAsync<TrackerTitleResult>())!);
        d.Title="Road Building";
        var repeat=(await(await pc.PostAsJsonAsync("/connector/v1/trackers",new TrackerPublication(pair.WorkspaceId,[new(d,binding)]))).Content.ReadFromJsonAsync<TrackerPublicationResult>())!;
        var accepted=Assert.Single(repeat.Sources);Assert.Equal(source.Revision,accepted.Revision);Assert.Equal(source.Binding.Id,accepted.Binding.Id);
        d.Items[0].Target=999;
        Assert.Empty((await(await pc.PostAsJsonAsync("/connector/v1/trackers",new TrackerPublication(pair.WorkspaceId,[new(d,binding)]))).Content.ReadFromJsonAsync<TrackerPublicationResult>())!.Sources);
        Assert.Equal(100,repo.Definitions().Single(x=>x.Id==d.Id).Items[0].Target);
        // A new service instance reads the persisted common ancestor, not an in-memory shortcut.
        var options=host.Services.GetRequiredService<CompanionOptions>();var workspace=host.Services.GetRequiredService<TrackerWorkspace>();
        var restarted=new PcConnector(options,workspace,host.Services.GetRequiredService<GameLibrary>());
        Assert.Equal(new TrackerTitleResult(1,0),restarted.PublishTitles(Names("Road network")));
        repo.RenameTracker(d.Id,"Independent NAS name","Road network");
        Assert.Equal(new TrackerTitleResult(0,1),restarted.PublishTitles(Names("Desktop changed again")));
        Assert.Equal("Independent NAS name",repo.Definitions().Single(x=>x.Id==d.Id).Title);
        Assert.Equal(1,JsonSerializer.SerializeToElement(restarted.Connection()).GetProperty("trackerTitleConflicts").GetInt32());
        Assert.Equal(new TrackerTitleResult(0,0),restarted.PublishTitles(Names("Independent NAS name")));
        Assert.Equal(new TrackerTitleResult(1,0),restarted.PublishTitles(Names("Agreed next name")));
        Assert.Equal(savedSource,JsonSerializer.Serialize(repo.Source(d.Id,Approved)));
    }

    [Fact]
    public async Task TitleSyncRequiresPairingScopeGrantAndTrustedInitialBaselineForCustomAndBundledNames()
    {
        using var fixture=new WorkspaceCase();SourceForTest(fixture);
        using var host=new Host(false,root:fixture.Root);using var owner=host.Client();await Setup(host,owner);var pair=PairForTest(fixture,host);
        using var pc=host.Client();pc.DefaultRequestHeaders.Authorization=new("Bearer",pair.Token);
        var d=fixture.Repo().Definitions().Single(x=>x.Id==TrackerId);
        var request=new TrackerTitles(pair.WorkspaceId,[new(d.Id,d.Provider,d.GameId,"Renamed paired tracker")]);
        using var anonymous=host.Client();Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.PostAsJsonAsync("/connector/v1/tracker-titles",request)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await pc.PostAsJsonAsync("/connector/v1/tracker-titles",request)).StatusCode);
        host.Services.GetRequiredService<ConnectorSyncState>().EnableLibraryAndAutomatic();
        Assert.Equal(HttpStatusCode.BadRequest,(await pc.PostAsJsonAsync("/connector/v1/tracker-titles",request with{WorkspaceId="wrong"})).StatusCode);
        foreach(var bad in new[]{new TrackerTitle(d.Id,"retroachievements",d.GameId,"New"),new(d.Id,d.Provider,"wrong-game","New"),new(d.Id,d.Provider,d.GameId,"\nNew"),new("missing",d.Provider,d.GameId,"New")})
            Assert.Equal(HttpStatusCode.BadRequest,(await pc.PostAsJsonAsync("/connector/v1/tracker-titles",request with{Titles=[request.Titles[0],bad]})).StatusCode);
        Assert.Equal(d.Title,fixture.Repo().Definitions().Single(x=>x.Id==d.Id).Title);
        Assert.Equal(new TrackerTitleResult(1,0),(await(await pc.PostAsJsonAsync("/connector/v1/tracker-titles",request)).Content.ReadFromJsonAsync<TrackerTitleResult>())!);
        var builtin=AchievementTrackingCatalog.Builtins().First(x=>x.Provider=="steam");
        Assert.Equal(new TrackerTitleResult(1,0),(await(await pc.PostAsJsonAsync("/connector/v1/tracker-titles",request with{Titles=[new(builtin.Id,builtin.Provider,builtin.GameId,"Bundled rename")]})).Content.ReadFromJsonAsync<TrackerTitleResult>())!);
        var independent=new AchievementTrackerDefinition{Id="custom.nas-only",Provider="steam",GameId="42",GameTitle="Test journey",Title="NAS authored",Items=new(){new(){Id="one",Name="One"}}};fixture.Repo().AddDefinition(independent);
        Assert.Equal(new TrackerTitleResult(0,1),(await(await pc.PostAsJsonAsync("/connector/v1/tracker-titles",request with{Titles=[new(independent.Id,independent.Provider,independent.GameId,"Unknown desktop origin")]})).Content.ReadFromJsonAsync<TrackerTitleResult>())!);
        Assert.Equal("NAS authored",fixture.Repo().Definitions().Single(x=>x.Id==independent.Id).Title);
        var another=AchievementTrackingCatalog.Builtins().First(x=>x.Provider=="steam"&&x.Id!=builtin.Id);fixture.Repo().RenameTracker(another.Id,"Independent bundled name");
        Assert.Equal(new TrackerTitleResult(0,1),(await(await pc.PostAsJsonAsync("/connector/v1/tracker-titles",request with{Titles=[new(another.Id,another.Provider,another.GameId,"Desktop bundled name")]})).Content.ReadFromJsonAsync<TrackerTitleResult>())!);
        Assert.Equal("Independent bundled name",fixture.Repo().Definitions().Single(x=>x.Id==another.Id).Title);
    }
}
