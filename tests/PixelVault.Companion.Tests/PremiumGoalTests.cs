using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PixelVaultNative;
using PixelVault.Tracking;
using Xunit;

namespace PixelVault.Companion.Tests;
public sealed partial class CompanionTests
{
    [Fact]
    public async Task PremiumGoalReportsCappedTotalsAndStableNotificationVersionOnUnchangedProgress()
    {
        using var fixture = new WorkspaceCase(); fixture.Prepare(); using var host = new Host(false, root: fixture.Root);
        using var owner = host.Client(); await Setup(host, owner);
        var workspace = host.Services.GetRequiredService<TrackerWorkspace>(); var repo = workspace.Repository;
        var definition = DeathStrandingPremiumGoal.Definition(); var account = workspace.Manifest!.Accounts["steam"];
        void Observe(int time) {
            var snapshot = new AchievementTrackerSnapshot { TrackerId=definition.Id,Provider="steam",GameId="1850570",Account=account,
                Source="Fixture",ObservedUtcTicks=DateTime.UtcNow.Ticks,Items=definition.Items.Select(i=>new AchievementTrackerProgress { ItemId=i.Id,Current=i.Category=="Time"?time:20 }).ToList() };
            repo.ApplyRefresh(repo.Preview(JsonSerializer.Serialize(snapshot),definition.Id,account,"json-import","1",Guid.NewGuid().ToString("N")),false,[],default);
        }
        async Task<JsonElement> Summary() => (await owner.GetFromJsonAsync<JsonElement>("/api/v1/trackers")).EnumerateArray().Single(t=>t.GetProperty("id").GetString()==definition.Id);
        Observe(19);var before=await Summary();
        Assert.Equal(79,before.GetProperty("totals").GetProperty("completed").GetInt32());
        Assert.Equal(80,before.GetProperty("totals").GetProperty("total").GetInt32());
        Assert.Equal(4,before.GetProperty("updateProgress").GetArrayLength());
        Observe(19);var same=await Summary();Assert.Equal(before.GetProperty("updateVersion").GetString(),same.GetProperty("updateVersion").GetString());
        Observe(20);var after=await Summary();Assert.NotEqual(before.GetProperty("updateVersion").GetString(),after.GetProperty("updateVersion").GetString());
        Assert.Equal(80,after.GetProperty("totals").GetProperty("completed").GetInt32());
    }
}
