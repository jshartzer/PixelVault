using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using PixelVaultNative;
using Xunit;

namespace PixelVault.Companion.Tests;
public sealed partial class CompanionTests
{
    [Fact]
    public async Task ReturnFeedPaginatesWithoutDroppingAuthoredFields()
    {
        using var fixture=new WorkspaceCase();var d=new AchievementTrackerDefinition{Id="return.pages",Provider="steam",GameId="42",GameTitle="Test",Title="Many items",Items=Enumerable.Range(0,101).Select(i=>new AchievementTrackerItem{Id="item-"+i,Name="Item "+i}).ToList()};fixture.Desktop.AddDefinition(d);SourceForTest(fixture);
        using var host=new Host(false,root:fixture.Root);using var owner=host.Client();await Setup(host,owner);var pair=PairForTest(fixture,host);var sync=host.Services.GetRequiredService<ConnectorSyncState>();sync.EnableLibraryAndAutomatic();sync.EnableTrackerWebEdits();
        foreach(var item in d.Items)fixture.Repo().SetManualProgress(d.Id,Approved,item.Id,1);
        using var pc=host.Client();pc.DefaultRequestHeaders.Authorization=new("Bearer",pair.Token);var first=(await pc.GetFromJsonAsync<TrackerWebPage>("/connector/v1/tracker-edits/"+d.Id))!;Assert.Equal(100,first.Fields.Count);Assert.Equal(100,first.NextOffset);
        var last=(await pc.GetFromJsonAsync<TrackerWebPage>("/connector/v1/tracker-edits/"+d.Id+"?offset=100"))!;Assert.Single(last.Fields);Assert.Null(last.NextOffset);Assert.Equal(101,first.Fields.Concat(last.Fields).Select(f=>f.ItemId).Distinct().Count());
    }
    [Fact]
    public async Task PairedReturnPathRequiresOptInAndReturnsOnlyPostCopyManualFieldsInApprovedScope()
    {
        using var fixture=new WorkspaceCase();SourceForTest(fixture);using var host=new Host(false,root:fixture.Root);using var owner=host.Client();await Setup(host,owner);var pair=PairForTest(fixture,host);
        using var pc=host.Client();pc.DefaultRequestHeaders.Authorization=new("Bearer",pair.Token);var endpoint="/connector/v1/tracker-edits/"+TrackerId;
        Assert.Equal(HttpStatusCode.Unauthorized,(await host.Client().GetAsync(endpoint)).StatusCode);
        var sync=host.Services.GetRequiredService<ConnectorSyncState>();sync.EnableLibraryAndAutomatic();Assert.Equal(HttpStatusCode.Conflict,(await pc.GetAsync(endpoint)).StatusCode);sync.EnableTrackerWebEdits();
        Assert.Empty((await pc.GetFromJsonAsync<TrackerWebPage>(endpoint))!.Fields);
        var before=await Editor(owner);await Save(owner,Edit(before,"count","progress",4));await Save(owner,Edit(await Editor(owner),"first","note",note:"Web note"));
        var page=(await pc.GetFromJsonAsync<TrackerWebPage>(endpoint))!;Assert.Equal(pair.WorkspaceId,page.WorkspaceId);Assert.Equal(2,page.Fields.Count);Assert.Equal(4,page.Fields.Single(f=>f.Field=="progress").Current);Assert.Null(page.Fields.Single(f=>f.Field=="progress").BaselineCurrent);
        Assert.DoesNotContain("Private other account",System.Text.Json.JsonSerializer.Serialize(page));Assert.Equal(HttpStatusCode.BadRequest,(await pc.GetAsync(endpoint+"?offset=-1")).StatusCode);
        sync.SetAutomatic(false);Assert.Equal(HttpStatusCode.Conflict,(await pc.GetAsync(endpoint)).StatusCode);
    }
    [Fact]
    public async Task UndoReturnsThePriorManualValueWithANewReturnVersion()
    {
        using var fixture=new WorkspaceCase();SourceForTest(fixture);using var host=new Host(false,root:fixture.Root);using var owner=host.Client();await Setup(host,owner);var pair=PairForTest(fixture,host);
        var sync=host.Services.GetRequiredService<ConnectorSyncState>();sync.EnableLibraryAndAutomatic();sync.EnableTrackerWebEdits();using var pc=host.Client();pc.DefaultRequestHeaders.Authorization=new("Bearer",pair.Token);
        var result=await Save(owner,Edit(await Editor(owner),"count","progress",4));var body=await result.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();var history=body.GetProperty("result").GetProperty("historyId").GetInt64();
        var endpoint="/connector/v1/tracker-edits/"+TrackerId;var initial=Assert.Single((await pc.GetFromJsonAsync<TrackerWebPage>(endpoint))!.Fields);
        await owner.PostAsJsonAsync($"/api/v1/trackers/{TrackerId}/history/{history}/undo",new{requestId=Guid.NewGuid().ToString()});var undone=Assert.Single((await pc.GetFromJsonAsync<TrackerWebPage>(endpoint))!.Fields);Assert.Null(undone.Current);Assert.NotEqual(initial.Version,undone.Version);
    }
}
