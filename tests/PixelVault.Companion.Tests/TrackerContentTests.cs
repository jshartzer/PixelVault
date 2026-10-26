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
    public async Task ContentPublishesAsPrivatePlainTextWithoutReplacingExistingCatalogueOrProgress()
    {
        using var fixture=new WorkspaceCase();SourceForTest(fixture);
        using var host=new Host(false,root:fixture.Root);using var owner=host.Client();await Setup(host,owner);var pair=PairForTest(fixture,host);
        host.Services.GetRequiredService<ConnectorSyncState>().EnableLibraryAndAutomatic();
        using var pc=host.Client();pc.DefaultRequestHeaders.Authorization=new("Bearer",pair.Token);
        const string text="First paragraph.\n\n<script>Do not execute this text.</script>\nA & B — final paragraph.";
        var definition=new AchievementTrackerDefinition{Id="custom.content",Provider="steam",GameId="42",GameTitle="Test journey",Title="Interview catalogue",AchievementId="039",Items=new(){new(){Id="uuid-one",Name="First interview",Detail="Author: Test author\nLocation: Test location",Content=text},new(){Id="uuid-two",Name="No content"}}};
        var request=new TrackerPublication(pair.WorkspaceId,[new(definition)]);
        var result=(await(await pc.PostAsJsonAsync("/connector/v1/trackers",request)).Content.ReadFromJsonAsync<TrackerPublicationResult>())!;
        Assert.Equal(1,result.Added);Assert.Empty(result.Sources);
        using var anonymous=host.Client();Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.GetAsync("/api/v1/trackers/"+definition.Id)).StatusCode);
        var view=await owner.GetFromJsonAsync<JsonElement>("/api/v1/trackers/"+definition.Id);
        Assert.Equal(text,view.GetProperty("definition").GetProperty("items")[0].GetProperty("content").GetString());
        var repo=fixture.Repo();repo.SetManualProgress(definition.Id,Approved,"uuid-one",1);repo.SaveNote(definition.Id,Approved,"uuid-one","Keep personal note");
        var history=JsonSerializer.Serialize(repo.History(definition.Id,Approved));
        definition.Items[0].Content="Changed later on desktop";
        await pc.PostAsJsonAsync("/connector/v1/trackers",request);
        Assert.Equal(text,repo.Definitions().Single(d=>d.Id==definition.Id).Items[0].Content);
        var states=repo.Load(definition.Id,Approved);Assert.Equal(1,states.Single(s=>s.Item.Id=="uuid-one").Current);Assert.Equal("Keep personal note",states.Single(s=>s.Item.Id=="uuid-one").Note);
        Assert.Equal(history,JsonSerializer.Serialize(repo.History(definition.Id,Approved)));Assert.Null(repo.Source(definition.Id,Approved));
    }
}
