using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace PixelVault.Companion.Tests;

public sealed partial class CompanionTests
{
    [Fact]
    public async Task AchievementUpdatesRequireOwnerAndContainOnlyConfirmedScopedStates()
    {
        using var fixture=new WorkspaceCase();SourceForTest(fixture);
        using var host=new Host(false,root:fixture.Root);using var owner=host.Client();await Setup(host,owner);
        Assert.Equal(HttpStatusCode.Unauthorized,(await host.Client().GetAsync("/api/v1/achievement-updates")).StatusCode);
        var pair=PairForTest(fixture,host);using var pc=host.Client();pc.DefaultRequestHeaders.Authorization=new("Bearer",pair.Token);
        host.Services.GetRequiredService<ConnectorSyncState>().EnableLibraryAndAutomatic();
        var data=AchievementFixture(pair.WorkspaceId);
        (await pc.PostAsJsonAsync("/connector/v1/achievements/snapshot",data)).EnsureSuccessStatusCode();
        var before=(await owner.GetFromJsonAsync<AchievementUpdateGame[]>("/api/v1/achievement-updates"))!.Single(g=>g.GameId=="42");
        Assert.Equal(new[]{"ONE"},before.Locked);Assert.Empty(before.Unlocked);Assert.Equal(64,before.Scope.Length);
        // An unknown row with Unlocked=true is not an unlock; descriptive/private data stays out of this projection.
        var json=await owner.GetStringAsync("/api/v1/achievement-updates");
        Assert.DoesNotContain("TWO",json);Assert.DoesNotContain(Approved,json);Assert.DoesNotContain(data.Rows[0].Guide,json);Assert.DoesNotContain(fixture.Root,json);
        data=data with{CapturedUtc=DateTime.UtcNow.Ticks,ProgressUpdatedUtc=DateTime.UtcNow.Ticks,Rows=data.Rows.Select(r=>r.Id=="ONE"?r with{Unlocked=true,UnlockUtc=DateTime.UtcNow.Ticks}:r).ToArray()};
        (await pc.PostAsJsonAsync("/connector/v1/achievements/snapshot",data)).EnsureSuccessStatusCode();
        var after=(await owner.GetFromJsonAsync<AchievementUpdateGame[]>("/api/v1/achievement-updates"))!.Single(g=>g.GameId=="42");
        Assert.Equal(before.Scope,after.Scope);Assert.Empty(after.Locked);Assert.Equal(new[]{"ONE"},after.Unlocked);
        var detail=await owner.GetFromJsonAsync<JsonElement>("/api/v1/achievements/steam/42/ONE");
        Assert.Equal(data.Rows[0].Guide,detail.GetProperty("achievement").GetProperty("guide").GetString());
    }

    [Fact]
    public async Task DirectLocalAchievementUpdatesReadCanonicalProgressWithoutWritingIt()
    {
        using var fixture=new DirectCase();using var host=fixture.Host();using var owner=host.Client();await Setup(host,owner);
        var before=(await owner.GetFromJsonAsync<AchievementUpdateGame[]>("/api/v1/achievement-updates"))!.Single(g=>g.GameId=="42");
        Assert.Equal(new[]{"ONE"},before.Locked);Assert.Empty(before.Unlocked);
        var path=Path.Combine(fixture.App,"achievement-hub","account.json");
        var updated=File.ReadAllText(path).Replace("\"Unlocked\":false","\"Unlocked\":true").Replace("\"UpdatedUtc\":1","\"UpdatedUtc\":2");
        File.WriteAllText(path,updated);
        // Source stamps invalidate the read projection when the canonical file changes.
        var after=(await owner.GetFromJsonAsync<AchievementUpdateGame[]>("/api/v1/achievement-updates"))!.Single(g=>g.GameId=="42");
        Assert.Equal(new[]{"ONE"},after.Unlocked);Assert.Empty(after.Locked);Assert.Equal(before.Scope,after.Scope);
        Assert.Equal(updated,File.ReadAllText(path));
        Assert.DoesNotContain("Keep this guide",JsonSerializer.Serialize(after));
        // Catalog-only Century definitions are unknown until a verified saved observation exists.
        var century=(await owner.GetFromJsonAsync<AchievementUpdateGame[]>("/api/v1/achievement-updates"))!.Single(g=>g.GameId=="1850570");
        Assert.Empty(century.Locked);Assert.Empty(century.Unlocked);
    }
}
