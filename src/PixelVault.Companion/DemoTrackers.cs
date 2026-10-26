using PixelVaultNative;

namespace PixelVault.Companion;

public sealed record TrackerPreview(AchievementTrackerDefinition Definition, IReadOnlyList<AchievementTrackerState> States)
{
    public TrackerTotals Totals => TrackerTotals.Calculate(Definition, States);
}

/// <summary>Synthetic data only. No live file paths, account identifiers, game reads or personal progress.</summary>
public static class DemoTrackers
{
    public static IReadOnlyList<TrackerPreview> Create()
    {
        var discoveries = new AchievementTrackerDefinition
        {
            Id = "demo.explorer", Provider = "demo", GameId = "journey", GameTitle = "The Long Journey",
            Title = "Places worth finding", Description = "A sample checklist for exploring tracker navigation.",
            Items = new()
            {
                new() { Id = "harbor", Name = "The old harbor", Category = "Coast", Detail = "Follow the path down from the lighthouse.", Order = 0 },
                new() { Id = "lighthouse", Name = "Lighthouse overlook", Category = "Coast", Order = 1 },
                new() { Id = "grove", Name = "The silver grove", Category = "Highlands", Detail = "Progress has not been checked yet.", Order = 2 },
                new() { Id = "camp", Name = "Mountain camp", Category = "Highlands", Order = 3 },
                new() { Id = "supplies", Name = "Gather trail supplies", Category = "Highlands", Target = 5, Order = 4 }
            }
        };
        var states = discoveries.Items.Select(i => new AchievementTrackerState { Item = i }).ToList();
        states[0].ManualCurrent = 1; states[0].Note = "Try this route at sunset.";
        states[1].ReportedCurrent = 1; states[1].Source = "Sample observation";
        states[3].ReportedCurrent = 0; states[3].Availability = TrackerAvailability.Locked;
        states[4].ManualCurrent = 2;
        var challenges = new AchievementTrackerDefinition
        {
            Id = "demo.challenges", Provider = "demo", GameId = "journey", GameTitle = "The Long Journey",
            Title = "Trail challenges", Description = "Independent completion columns, with a goal that counts each trail once.",
            Milestones = new() { "Finished", "Perfect" }, CategoryTargets = new() { ["Trails"] = 2 }
        };
        challenges.Items = TrackerMilestones.Expand(new[]
        {
            new AchievementTrackerItem { Id = "ridge", Name = "Ridge trail", Category = "Trails", Order = 0 },
            new AchievementTrackerItem { Id = "river", Name = "River trail", Category = "Trails", Order = 1 },
            new AchievementTrackerItem { Id = "forest", Name = "Forest trail", Category = "Trails", Order = 2 }
        }, challenges.Milestones);
        var challengeStates = challenges.Items.Select((i, n) => new AchievementTrackerState
        { Item = i, ManualCurrent = n < 2 ? 1 : null }).ToList();
        var collection = new AchievementTrackerDefinition
        {
            Id = "demo.collection", Provider = "demo", GameId = "archive", GameTitle = "Midnight Archive",
            Title = "Lost pages", Description = "A second sample game. Unknown progress is kept separate from zero.",
            Items = Enumerable.Range(1, 12).Select(n => new AchievementTrackerItem
            { Id = "page." + n, Name = "Journal page " + n, Category = n <= 6 ? "West wing" : "East wing", Order = n }).ToList()
        };
        return new[] { new TrackerPreview(discoveries, states), new TrackerPreview(challenges, challengeStates),
            new TrackerPreview(collection, collection.Items.Select(i => new AchievementTrackerState { Item = i }).ToList()) };
    }
}
