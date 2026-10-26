namespace PixelVaultNative;

/// <summary>Full-definition totals, independent of presentation filters. Archives never contribute.</summary>
public sealed record TrackerTotals(int Completed, int Total, int Unknown, int Records, int CompletedRecords)
{
    public double Percent => Total == 0 ? 0 : 100.0 * Completed / Total;

    public static TrackerTotals Calculate(AchievementTrackerDefinition definition, IEnumerable<AchievementTrackerState> states)
    {
        var active = states.Where(s => !s.Item.Archived).ToList();
        var rows = active.GroupBy(s => definition.Milestones.Count > 0 ? s.Item.GroupId : s.Item.Id).ToList();
        int completed = active.Count(s => s.Complete), total = active.Count;
        if (active.Count == 1 && definition.Milestones.Count == 0 && active[0].Item.Target > 1)
        {
            total = active[0].Item.Target;completed = Math.Clamp(active[0].Current ?? 0, 0, total);
        }
        if (definition.CategoryTargets.Count > 0)
        {
            var qualifying = QualifyingRows(active);
            completed = definition.CategoryTargets.Sum(t => Math.Min(t.Value, qualifying.GetValueOrDefault(t.Key)));
            total = definition.CategoryTargets.Values.Sum();
        }
        return new(completed, total, active.Count(s => !s.IsKnown), rows.Count, rows.Count(g => g.Any(s => s.Complete)));
    }

    public static Dictionary<string, int> QualifyingRows(IEnumerable<AchievementTrackerState> states) => states
        .Where(s => !s.Item.Archived && s.Complete).GroupBy(s => s.Item.Category ?? "Other")
        .ToDictionary(g => g.Key, g => g.Select(s => s.Item.GroupId).Distinct(StringComparer.Ordinal).Count());
}
