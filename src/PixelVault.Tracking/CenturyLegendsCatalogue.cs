using System.Text.Json;
using PixelVaultNative;

namespace PixelVault.Tracking;

/// <summary>The complete Legend-or-higher goal, independent of the official per-category premium-order target.</summary>
public static class CenturyLegendsCatalogue
{
    public const string TrackerId = "death-stranding-dc.nothing-left-to-deliver";
    public const int Total = 540;
    static readonly Lazy<HashSet<string>> Ids = new(() => Definition().Items.Select(i => i.Id).ToHashSet(StringComparer.Ordinal));
    public static IReadOnlySet<string> ItemIds => Ids.Value;

    public static AchievementTrackerDefinition Definition()
    {
        using var stream = typeof(CenturyLegendsCatalogue).Assembly.GetManifestResourceStream("PixelVault.Tracking.DeathStrandingOrders.json");
        var orders = JsonSerializer.Deserialize<AchievementTrackerDefinition>(stream, AchievementTrackingCatalog.Json);
        var items = orders.Items.Where(i => i.Milestone == "LLL").ToList();
        if (items.Count != Total || items.Select(i => i.Id).Distinct(StringComparer.Ordinal).Count() != Total
            || items.Any(i => i.Id != i.GroupId + ":LLL" || i.Identifiers.GetValueOrDefault("MissionId") != i.GroupId || i.Target != 1))
            throw new InvalidDataException("The complete LLL order catalogue is invalid.");
        foreach (var item in items)
        {
            item.GroupId = null; item.Milestone = null;
            item.Detail = "Earn a Legend or higher evaluation in a Premium Delivery for standard order "
                + item.Identifiers.GetValueOrDefault("OrderNumber") + ".";
        }
        return new AchievementTrackerDefinition
        {
            Id = TrackerId, Provider = "steam", GameId = "1850570", GameTitle = orders.GameTitle,
            AchievementId = CenturyAchievementCatalog.DeathStrandingLegendsId,
            Title = "Nothing Left to Deliver", Description = "Earn Legend or higher in a Premium Delivery for all 540 standard orders.",
            CompletionLabel = "Legend orders", Items = items
        };
    }

    public static bool Matches(AchievementTrackerDefinition definition) => definition?.Id == TrackerId
        && definition.Provider == "steam" && definition.GameId == "1850570"
        && definition.Milestones?.Count == 0 && definition.CategoryTargets?.Count == 0
        && definition.Items?.Count == Total && ItemIds.SetEquals(definition.Items.Select(i => i.Id))
        && definition.Items.All(i => !i.Archived && i.Target == 1 && string.IsNullOrEmpty(i.GroupId)
            && string.IsNullOrEmpty(i.Milestone) && i.Identifiers?.GetValueOrDefault("MissionId") + ":LLL" == i.Id);
}
