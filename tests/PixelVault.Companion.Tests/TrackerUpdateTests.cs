using System.Net.Http.Json;
using System.Text.Json;
using PixelVaultNative;
using Xunit;

namespace PixelVault.Companion.Tests;

public sealed partial class CompanionTests
{
    [Fact]
    public async Task TrackerViewReportsWhetherAnActualSourceIsLinked()
    {
        using var fixture=new WorkspaceCase();SourceForTest(fixture);using var host=new Host(false,root:fixture.Root);using var owner=host.Client();await Setup(host,owner);
        Assert.True((await Editor(owner)).GetProperty("sourceLinked").GetBoolean());
        fixture.Repo().RemoveSource(TrackerId,Approved);
        Assert.False((await Editor(owner)).GetProperty("sourceLinked").GetBoolean());
    }
    static async Task<string> UpdateVersion(HttpClient client)
    {
        var rows=await client.GetFromJsonAsync<JsonElement>("/api/v1/trackers");return rows.EnumerateArray().Single(t=>t.GetProperty("id").GetString()==TrackerId).GetProperty("updateVersion").GetString()!;
    }
    [Fact]
    public async Task TrackerUpdateRevisionIgnoresReadTimestampsButDetectsProgressAndNotesWithoutExposingNoteContent()
    {
        using var fixture=new WorkspaceCase();fixture.Prepare();using var host=new Host(false,root:fixture.Root);using var client=host.Client();await Setup(host,client);
        var before=await UpdateVersion(client);Assert.Equal(64,before.Length);
        var repo=fixture.Repo();var json=JsonSerializer.Serialize(new AchievementTrackerSnapshot{TrackerId=TrackerId,Provider="steam",GameId="42",Account=Approved,Source="Another read of the same save",ObservedUtcTicks=DateTime.UtcNow.Ticks,Items=new(){new(){ItemId="first",Current=0},new(){ItemId="count",Current=2}}});
        repo.ApplyRefresh(repo.Preview(json,TrackerId,Approved,"json-import","1","same-progress-new-time"),false,[],default);
        Assert.Equal(before,await UpdateVersion(client));
        repo.SetManualProgress(TrackerId,Approved,"count",3);var progress=await UpdateVersion(client);Assert.NotEqual(before,progress);
        repo.SaveNote(TrackerId,Approved,"count","Private updated note");Assert.NotEqual(progress,await UpdateVersion(client));
        var body=await client.GetStringAsync("/api/v1/trackers");Assert.DoesNotContain("Private updated note",body);Assert.DoesNotContain(Approved,body);Assert.DoesNotContain(fixture.Root,body);
        var listing=JsonDocument.Parse(body).RootElement.EnumerateArray().Single(t=>t.GetProperty("id").GetString()==TrackerId);
        var items=listing.GetProperty("updateProgress").EnumerateArray().ToArray();
        Assert.Equal(3,items.Length);Assert.DoesNotContain(items,i=>i.GetProperty("id").GetString()=="archived");
        Assert.Equal(3,items.Single(i=>i.GetProperty("id").GetString()=="count").GetProperty("current").GetInt32());
        Assert.Equal(JsonValueKind.Null,items.Single(i=>i.GetProperty("id").GetString()=="unknown").GetProperty("current").ValueKind);
    }
}
