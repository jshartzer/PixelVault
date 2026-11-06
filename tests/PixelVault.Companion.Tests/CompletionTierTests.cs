using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace PixelVault.Companion.Tests;

public sealed partial class CompanionTests
{
    [Fact]
    public async Task SummaryCountsDoNotAwardAFrameWithoutVerifiedAchievementRows()
    {
        using var fixture=new WorkspaceCase();SourceForTest(fixture);LibraryFixture(fixture.Root);
        var file=Path.Combine(fixture.Root,"library","catalog.json");
        var catalogue=JsonSerializer.Deserialize<GameCatalogue>(File.ReadAllText(file),LibraryJson)!;
        catalogue=catalogue with{Games=catalogue.Games.Select(g=>g.Provider=="steam"?g with{Stats=g.Stats with{AchievementUnlocked=8,AchievementTotal=8}}:g).ToList()};
        File.WriteAllText(file,JsonSerializer.Serialize(catalogue,LibraryJson));
        using var host=new Host(false,root:fixture.Root);using var owner=host.Client();await Setup(host,owner);
        var game=await owner.GetFromJsonAsync<JsonElement>("/api/v1/games/steam/42");
        Assert.Equal("none",game.GetProperty("completionTier").GetString());
        Assert.Equal(JsonValueKind.Null,game.GetProperty("completionTotal").ValueKind);
    }

    [Fact]
    public async Task IncompleteBaseCategoryPreventsAFrameEvenWhenOtherBaseAndDlcCategoriesAreComplete()
    {
        using var fixture=new WorkspaceCase();SourceForTest(fixture);LibraryFixture(fixture.Root);
        using var host=new Host(false,root:fixture.Root);using var owner=host.Client();await Setup(host,owner);
        var pair=PairForTest(fixture,host);using var pc=host.Client();pc.DefaultRequestHeaders.Authorization=new("Bearer",pair.Token);
        host.Services.GetRequiredService<ConnectorSyncState>().EnableLibraryAndAutomatic();
        var data=AchievementFixture(pair.WorkspaceId);
        data=data with{Rows=data.Rows.Select(r=>r with{ProgressKnown=true,Unlocked=r.Id=="ONE",Groups=new(r.Id=="TWO"?"Progression":"Story","")}).ToArray()};
        (await pc.PostAsJsonAsync("/connector/v1/achievements/snapshot",data)).EnsureSuccessStatusCode();
        var game=await owner.GetFromJsonAsync<JsonElement>("/api/v1/games/steam/42");
        Assert.Equal("none",game.GetProperty("completionTier").GetString());
    }
    [Fact]
    public async Task LibraryCompletionUsesEarnedBaseAndDlcWithCurrentWebGroups()
    {
        using var fixture=new WorkspaceCase();SourceForTest(fixture);LibraryFixture(fixture.Root);
        using var host=new Host(false,root:fixture.Root);using var owner=host.Client();await Setup(host,owner);
        var pair=PairForTest(fixture,host);using var pc=host.Client();pc.DefaultRequestHeaders.Authorization=new("Bearer",pair.Token);
        host.Services.GetRequiredService<ConnectorSyncState>().EnableLibraryAndAutomatic();
        var data=AchievementFixture(pair.WorkspaceId);
        data=data with{Rows=data.Rows.Select(r=>r with{ProgressKnown=true,Unlocked=r.Id=="TWO"}).ToArray()};
        (await pc.PostAsJsonAsync("/connector/v1/achievements/snapshot",data)).EnsureSuccessStatusCode();
        var gold=await owner.GetFromJsonAsync<JsonElement>("/api/v1/games/steam/42");
        Assert.Equal("base",gold.GetProperty("completionTier").GetString());
        Assert.Equal(1,gold.GetProperty("completionEarned").GetInt32());
        Assert.Equal(data.Rows.Count,gold.GetProperty("completionTotal").GetInt32());
        data=data with{CapturedUtc=DateTime.UtcNow.Ticks,ProgressUpdatedUtc=DateTime.UtcNow.Ticks,Rows=data.Rows.Select(r=>r with{Unlocked=true}).ToArray()};
        (await pc.PostAsJsonAsync("/connector/v1/achievements/snapshot",data)).EnsureSuccessStatusCode();
        var emerald=await owner.GetFromJsonAsync<JsonElement>("/api/v1/games/steam/42");
        Assert.Equal("base-dlc",emerald.GetProperty("completionTier").GetString());
        Assert.Equal(data.Rows.Count,emerald.GetProperty("completionEarned").GetInt32());
        Assert.Equal(data.Rows.Count,emerald.GetProperty("completionTotal").GetInt32());
        var one=await owner.GetFromJsonAsync<JsonElement>("/api/v1/achievements/steam/42/ONE");
        (await owner.PostAsJsonAsync("/api/v1/achievements/steam/42/ONE/groups",new EditAchievementGroups(GroupVersion(one),Guid.NewGuid().ToString(),new("Story","")))).EnsureSuccessStatusCode();
        var noDlc=await owner.GetFromJsonAsync<JsonElement>("/api/v1/games/steam/42");
        Assert.Equal("base",noDlc.GetProperty("completionTier").GetString());
    }
}
