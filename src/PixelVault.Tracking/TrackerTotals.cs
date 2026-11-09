namespace PixelVaultNative;

/// <summary>Full-definition totals, independent of presentation filters. Archives never contribute.</summary>
public sealed record TrackerTotals(int Completed, int Total, int Unknown, int Records, int CompletedRecords)
{
    public double Percent => Total == 0 ? 0 : 100.0 * Completed / Total;
    public int AggregateCompleted { get; init; } = Completed;
    public int AggregateTotal { get; init; } = Total;

    /// <summary>Evaluate required items using only the supplied source observations, never manual checklist state.</summary>
    public static TrackerTotals FromVerifiedSnapshot(AchievementTrackerDefinition definition, AchievementTrackerSnapshot snapshot)
    {
        var values = snapshot.Items.ToDictionary(i => i.ItemId, StringComparer.Ordinal);
        return Calculate(definition, definition.Items.Select(item => new AchievementTrackerState
        { Item = item, ReportedCurrent = values.GetValueOrDefault(item.Id)?.Current }));
    }

    public static TrackerTotals Calculate(AchievementTrackerDefinition definition, IEnumerable<AchievementTrackerState> states)
    {
        var visible = states.Where(s => !s.Item.Archived).ToList();
        var active = visible.Where(s => !s.Item.ExcludedFromTotal).ToList();
        var rows = visible.GroupBy(s => definition.Milestones.Count > 0 ? s.Item.GroupId : s.Item.Id).ToList();
        int completed = active.Count(s => s.Complete), total = active.Count;
        if (definition.SumItemTargets && definition.Milestones.Count == 0)
        {
            total = active.Sum(s => s.Item.Target);
            completed = active.Sum(s => Math.Clamp(s.Current ?? 0, 0, s.Item.Target));
        }
        else if (active.Count == 1 && definition.Milestones.Count == 0 && active[0].Item.Target > 1)
        {
            total = active[0].Item.Target;completed = Math.Clamp(active[0].Current ?? 0, 0, total);
        }
        if (definition.CategoryTargets.Count > 0)
        {
            var qualifying = QualifyingRows(active);
            completed = definition.CategoryTargets.Sum(t => Math.Min(t.Value, qualifying.GetValueOrDefault(t.Key)));
            total = definition.CategoryTargets.Values.Sum();
        }
        return new(completed, total, active.Count(s => !s.IsKnown), rows.Count, rows.Count(g => g.Any(s => s.Complete)))
        {
            AggregateCompleted = definition.AggregateAsSingleItem ? total > 0 && completed >= total ? 1 : 0 : completed,
            AggregateTotal = definition.AggregateAsSingleItem ? total > 0 ? 1 : 0 : total
        };
    }

    public static Dictionary<string, int> QualifyingRows(IEnumerable<AchievementTrackerState> states) => states
        .Where(s => !s.Item.Archived && !s.Item.ExcludedFromTotal && s.Complete).GroupBy(s => s.Item.Category ?? "Other")
        .ToDictionary(g => g.Key, g => g.Select(s => s.Item.GroupId).Distinct(StringComparer.Ordinal).Count());
}
