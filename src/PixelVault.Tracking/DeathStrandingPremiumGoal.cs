using PixelVaultNative;

namespace PixelVault.Tracking;

/// <summary>The official goal stops at 20 distinct LL-or-better deliveries per category.</summary>
public static class DeathStrandingPremiumGoal
{
    public static readonly string[] Categories = { "Condition", "Quantity", "Time", "Miscellaneous" };
    public static string ItemId(string category) => "qualifying-" + category.ToLowerInvariant();
    public static AchievementTrackerDefinition Definition() => new()
    {
        Id = AchievementTrackingCatalog.DeathStrandingOrdersId, Provider = "steam", GameId = "1850570",
        GameTitle = "DEATH STRANDING DIRECTOR'S CUT", AchievementId = "019",
        Title = "Growth of a Legend — Premium orders",
        Description = "Complete 20 unique premium deliveries in each category with Legend of Legends or better. Each order counts once; each category stops at 20. The complete order checklist is in Nothing Left to Deliver.",
        CompletionLabel = "Qualifying deliveries", SumItemTargets = true,
        Items = Categories.Select((category, index) => new AchievementTrackerItem
        {
            Id = ItemId(category), Name = category + " deliveries", Category = category, Target = 20, Order = index,
            Detail = "Complete 20 different " + category.ToLowerInvariant() + " premium deliveries with LL or LLL. Repeating an order, earning both ranks, or completing extra orders after 20 does not add achievement progress."
        }).ToList()
    };

    public static AchievementTrackerSnapshot Project(AchievementTrackerDefinition catalogue, AchievementTrackerSnapshot ranks)
    {
        var values = ranks.Items.ToDictionary(i => i.ItemId, i => i.Current);
        return new()
        {
            TrackerId = AchievementTrackingCatalog.DeathStrandingOrdersId, Provider = ranks.Provider, GameId = ranks.GameId,
            Account = ranks.Account, Source = ranks.Source, Evidence = ranks.Evidence + " Official goal: distinct LL-or-LLL orders, capped at 20 per category.",
            ObservedUtcTicks = ranks.ObservedUtcTicks,
            Items = Categories.Select(category => new AchievementTrackerProgress
            {
                ItemId = ItemId(category), Availability = TrackerAvailability.Unknown,
                Current = Math.Min(20, catalogue.Items.Where(i => i.Category == category && values.GetValueOrDefault(i.Id) > 0)
                    .Select(i => i.GroupId).Distinct(StringComparer.Ordinal).Count())
            }).ToList()
        };
    }
}
