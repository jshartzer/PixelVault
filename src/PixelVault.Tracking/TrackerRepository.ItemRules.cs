using Microsoft.Data.Sqlite;

namespace PixelVaultNative;

public sealed record TrackerItemRuleEdit(string ItemId, bool Missable, bool RequiredForCompletion, string ExpectedVersion);

public partial class TrackerRepository
{
    public static string ItemRulesVersion(AchievementTrackerDefinition definition, string itemId)
    {
        var item = definition.Items.Single(i => i.Id == itemId);
        return Digest(Json(RuleMembers(definition, item).OrderBy(i => i.Id, StringComparer.Ordinal)
            .Select(i => new { i.Id, i.Missable, i.ExcludedFromTotal })));
    }

    public static IReadOnlyDictionary<string, string> ItemRulesVersions(AchievementTrackerDefinition definition)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var group in definition.Items.GroupBy(i => string.IsNullOrEmpty(i.GroupId) ? "item:" + i.Id : "group:" + i.GroupId))
        {
            var version = Digest(Json(group.OrderBy(i => i.Id, StringComparer.Ordinal).Select(i => new { i.Id, i.Missable, i.ExcludedFromTotal })));
            foreach (var item in group) result[item.Id] = version;
        }
        return result;
    }

    static IEnumerable<AchievementTrackerItem> RuleMembers(AchievementTrackerDefinition definition, AchievementTrackerItem item) =>
        string.IsNullOrEmpty(item.GroupId) ? new[] { item } : definition.Items.Where(i => i.GroupId == item.GroupId);

    static void ReadItemRules(SqliteConnection c, SqliteTransaction tx, List<AchievementTrackerDefinition> definitions)
    {
        using var cmd = c.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = "SELECT tracker_id,item_id,missable,excluded FROM tracker_item_rule";
        using var reader = cmd.ExecuteReader();
        var trackers = definitions.ToDictionary(d => d.Id, d => d.Items.ToDictionary(i => i.Id));
        while (reader.Read())
            if (trackers.TryGetValue(reader.GetString(0), out var items) && items.TryGetValue(reader.GetString(1), out var item))
            { item.Missable = reader.GetBoolean(2); item.ExcludedFromTotal = reader.GetBoolean(3); }
    }

    // Catalogue metadata is shared across accounts. This command never changes progress or awards points.
    public void SetItemRules(string tracker, TrackerItemRuleEdit edit)
    {
        using var c = Open(); using var tx = c.BeginTransaction();
        var definition = ReadDefinitions(c, tx).SingleOrDefault(d => d.Id == tracker)
            ?? throw new InvalidDataException("The tracker was not found.");
        var item = definition.Items.SingleOrDefault(i => i.Id == edit.ItemId && !i.Archived)
            ?? throw new InvalidDataException("The tracker item was not found.");
        if (edit.ExpectedVersion != ItemRulesVersion(definition, item.Id))
            throw new InvalidOperationException("These item settings changed in another window. Reload the tracker before saving again.");
        var members = RuleMembers(definition, item).ToList();
        if (!edit.RequiredForCompletion && !definition.Items.Any(i => !i.Archived && !i.ExcludedFromTotal && !members.Contains(i)))
            throw new InvalidDataException("Keep at least one item required for completion.");
        foreach (var member in members)
        {
            using var cmd = c.CreateCommand(); cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO tracker_item_rule(tracker_id,item_id,missable,excluded) VALUES($t,$i,$m,$e) ON CONFLICT(tracker_id,item_id) DO UPDATE SET missable=$m,excluded=$e";
            cmd.Parameters.AddWithValue("$t", tracker); cmd.Parameters.AddWithValue("$i", member.Id);
            cmd.Parameters.AddWithValue("$m", edit.Missable); cmd.Parameters.AddWithValue("$e", !edit.RequiredForCompletion);
            cmd.ExecuteNonQuery();
        }
        tx.Commit(); NotifyAutomationChanged();
    }
}
