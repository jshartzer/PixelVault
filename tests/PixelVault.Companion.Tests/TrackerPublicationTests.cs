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
    public async Task PublicationIsAddOnlyScopedIdempotentAndCannotBroadenPairedReadPaths()
    {
        using var fixture=new WorkspaceCase();
        fixture.Desktop.SaveSource(new(){TrackerId=TrackerId,Account=Approved,AdapterId="death-stranding-dc.fabrication",AdapterVersion="1",Location=@"C:\approved-save",GameDirectory=@"C:\approved-game"});fixture.Prepare();
        using var host=new Host(false,root:fixture.Root);using var owner=host.Client();await Setup(host,owner);var pair=PairForTest(fixture,host);
        using var pc=host.Client();pc.DefaultRequestHeaders.Authorization=new("Bearer",pair.Token);
        var definition=new AchievementTrackerDefinition{Id="custom.published",Provider="steam",GameId="42",GameTitle="Test journey",Title="New desktop tracker",Items=new(){new(){Id="count",Name="Counter",Target=100}}};
        var binding=JsonSerializer.Deserialize<TrackerSourceBinding>(JsonSerializer.Serialize(pair.Sources.Single().Binding))!;binding.TrackerId=definition.Id;binding.KeepSnapshots=false;
        var request=new TrackerPublication(pair.WorkspaceId,[new(definition,binding)]);
        Assert.Equal(HttpStatusCode.Conflict,(await pc.PostAsJsonAsync("/connector/v1/trackers",request)).StatusCode);
        host.Services.GetRequiredService<ConnectorSyncState>().EnableLibraryAndAutomatic();
        Assert.Equal(HttpStatusCode.BadRequest,(await pc.PostAsJsonAsync("/connector/v1/trackers",request with{WorkspaceId="other"})).StatusCode);
        using var anonymous=host.Client();Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.PostAsJsonAsync("/connector/v1/trackers",request)).StatusCode);
        var first=(await (await pc.PostAsJsonAsync("/connector/v1/trackers",request)).Content.ReadFromJsonAsync<TrackerPublicationResult>())!;
        Assert.Equal(1,first.Added);Assert.Single(first.Sources);Assert.NotEqual(binding.Id,first.Sources[0].Binding.Id);
        fixture.Repo().SetManualProgress(definition.Id,Approved,"count",67);fixture.Repo().SaveNote(definition.Id,Approved,"count","NAS note");
        int history=fixture.Repo().History(definition.Id,Approved).Count;
        var repeated=(await (await pc.PostAsJsonAsync("/connector/v1/trackers",request)).Content.ReadFromJsonAsync<TrackerPublicationResult>())!;Assert.Equal(0,repeated.Added);Assert.Single(repeated.Sources);
        definition.Title="Changed on desktop";definition.Items[0].Target=999;
        await pc.PostAsJsonAsync("/connector/v1/trackers",request);
        Assert.Equal("New desktop tracker",fixture.Repo().Definitions().Single(d=>d.Id==definition.Id).Title);Assert.Equal(100,fixture.Repo().Definitions().Single(d=>d.Id==definition.Id).Items[0].Target);
        var state=fixture.Repo().Load(definition.Id,Approved).Single();Assert.Equal(67,state.Current);Assert.Equal("NAS note",state.Note);Assert.Equal(history,fixture.Repo().History(definition.Id,Approved).Count);
        definition.Id="custom.unapproved-path";binding.TrackerId=definition.Id;binding.Location=@"C:\different-save";
        var refused=(await (await pc.PostAsJsonAsync("/connector/v1/trackers",request)).Content.ReadFromJsonAsync<TrackerPublicationResult>())!;
        Assert.Equal(1,refused.Added);Assert.Empty(refused.Sources);Assert.Null(fixture.Repo().Source(definition.Id,Approved));
        definition.Provider="retroachievements";
        Assert.Equal(HttpStatusCode.BadRequest,(await pc.PostAsJsonAsync("/connector/v1/trackers",request)).StatusCode);
    }
}
