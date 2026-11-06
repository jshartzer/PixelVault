using PixelVaultNative;

namespace PixelVault.Tracking;

public static class CenturyCargoGoal
{
    public const string TrackerId = "death-stranding-dc.the-weight-of-a-nation";
    public const string ItemId = "cargo-delivered-kg";
    public const int Target = 50000;
    public const int StatisticId = 49;
    // Keep cumulative progress above the goal; reject unsupported or corrupt numeric values.
    public const int MaximumKilograms = 1000000;

    public static AchievementTrackerDefinition Definition() => new()
    {
        Id = TrackerId, Provider = "steam", GameId = "1850570", GameTitle = "Death Stranding Director's Cut",
        AchievementId = CenturyAchievementCatalog.DeathStrandingCargoId,
        Title = "The Weight of a Nation", Description = "Deliver a total of 50,000 kg of cargo.", CompletionLabel = "kg delivered", AggregateAsSingleItem = true,
        Items = new() { new() { Id = ItemId, Name = "Cargo delivered (kg)", Category = "Deliveries", Target = Target,
            Detail = "Deliver a cumulative total of 50,000 kg of cargo.",
            Identifiers = new() { ["AchievementStatisticId"] = "49", ["Unit"] = "kg" } } }
    };

    public static bool Matches(AchievementTrackerDefinition definition) => definition?.Id == TrackerId
        && definition.Provider == "steam" && definition.GameId == "1850570"
        && definition.Milestones?.Count == 0 && definition.CategoryTargets?.Count == 0
        && definition.Items?.Count == 1 && definition.Items[0] is { Archived: false, Target: Target } item
        && item.Id == ItemId && string.IsNullOrEmpty(item.GroupId) && string.IsNullOrEmpty(item.Milestone)
        && item.Identifiers?.GetValueOrDefault("AchievementStatisticId") == "49"
        && item.Identifiers.GetValueOrDefault("Unit") == "kg";
}
