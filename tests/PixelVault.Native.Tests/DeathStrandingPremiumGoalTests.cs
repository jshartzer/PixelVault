using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PixelVault.Tracking;
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class DeathStrandingPremiumGoalTests
{
    readonly AchievementTrackerDefinition catalogue = AchievementTrackingCatalog.DeathStrandingOrderCatalogue();
    AchievementTrackerSnapshot Ranks(int condition, int quantity, int time, int miscellaneous, bool both = false)
    {
        var counts = new[] { condition, quantity, time, miscellaneous };
        var earned = DeathStrandingPremiumGoal.Categories.SelectMany((category, n) => catalogue.Items
            .Where(i => i.Category == category).Select(i => i.GroupId).Distinct().Take(counts[n])).ToHashSet();
        return new() { TrackerId = catalogue.Id, Provider = "steam", GameId = "1850570", Account = "player",
            Source = "Fixture save", ObservedUtcTicks = DateTime.UtcNow.Ticks,
            Items = catalogue.Items.Select(i => new AchievementTrackerProgress {
                ItemId = i.Id, Current = earned.Contains(i.GroupId) && (both || i.Milestone == "LLL") ? 1 : 0 }).ToList() };
    }
    [Fact]
    public void UniqueLlOrLllOrdersStopAtTwentyInEachCategory()
    {
        var definition = DeathStrandingPremiumGoal.Definition();
        AchievementTrackingCatalog.Validate(definition);
        var projected = DeathStrandingPremiumGoal.Project(catalogue, Ranks(70, 66, 19, 22));
        Assert.Equal(new int?[] {20,20,19,20}, projected.Items.Select(i => i.Current));
        var totals = TrackerTotals.FromVerifiedSnapshot(definition, projected);
        Assert.Equal(79, totals.Completed); Assert.Equal(80, totals.Total); Assert.Equal(4, totals.Records);
        Assert.Equal(projected.Items.Select(i => i.Current), DeathStrandingPremiumGoal.Project(catalogue, Ranks(70,66,19,22,true)).Items.Select(i => i.Current));
        var ll = Ranks(20,20,20,20);
        foreach(var item in ll.Items) item.Current = item.ItemId.EndsWith(":LL") ? ll.Items.Single(i => i.ItemId == item.ItemId + "L").Current : 0;
        Assert.Equal(80, TrackerTotals.FromVerifiedSnapshot(definition, DeathStrandingPremiumGoal.Project(catalogue,ll)).Completed);
    }
    [Fact]
    public void ExtraCompletedCategoryOrdersDoNotProduceAnotherProgressNotification()
    {
        var root = Path.Combine(Path.GetTempPath(), "PV-Premium-" + Guid.NewGuid().ToString("N"));
        try {
            var store = new AchievementTrackingStore(root);
            var definition = DeathStrandingPremiumGoal.Definition();
            TrackerRefreshPlan Plan(AchievementTrackerSnapshot ranks) => store.Preview(
                JsonSerializer.Serialize(DeathStrandingPremiumGoal.Project(catalogue,ranks), AchievementTrackingCatalog.Json), definition.Id,"player","json-import","1",Guid.NewGuid().ToString("N"));
            var initial = Plan(Ranks(20,20,19,20)); store.ApplyRefresh(initial,false,[],default);
            store.SaveNote(definition.Id,"player","qualifying-time","One more timed delivery");
            var extra = Plan(Ranks(21,21,19,21,true));
            Assert.Equal(0,TrackerRefreshPolicy.ChangedCount(extra,null));
            var last = Plan(Ranks(21,21,20,21,true));
            Assert.Equal(1,TrackerRefreshPolicy.ChangedCount(last,null));
            store.ApplyRefresh(last,false,[],default);
            Assert.Equal(80,TrackerTotals.Calculate(definition,store.Load(definition.Id,"player")).Completed);
            Assert.Equal("One more timed delivery",store.Load(definition.Id,"player").Single(i=>i.Item.Id=="qualifying-time").Note);
        } finally { SqliteConnection.ClearAllPools(); if(Directory.Exists(root))Directory.Delete(root,true); }
    }
}
