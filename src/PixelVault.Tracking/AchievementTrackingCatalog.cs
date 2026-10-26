using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PixelVaultNative
{
    public static class AchievementTrackingCatalog
    {
        public const string DeathStrandingId = "death-stranding-dc.homo-faber";
        public const string DeathStrandingBuildId = "death-stranding-dc.master-builder";
        public const string DeathStrandingUpgradeId = "death-stranding-dc.trail-blazer";
        public const string DeathStrandingChipsId = "death-stranding-dc.fount-of-knowledge";
        public const string DeathStrandingOrdersId = "death-stranding-dc.growth-of-a-legend";
        public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

        public static AchievementTrackerDefinition DeathStranding()
        {
            using var stream = typeof(AchievementTrackingCatalog).Assembly.GetManifestResourceStream("PixelVault.Tracking.DeathStranding.json");
            return JsonSerializer.Deserialize<AchievementTrackerDefinition>(stream, Json);
        }
        public static IReadOnlyList<AchievementTrackerDefinition> Builtins()
        {
            using var stream = typeof(AchievementTrackingCatalog).Assembly.GetManifestResourceStream("PixelVault.Tracking.DeathStrandingWorld.json");
            var definitions = new List<AchievementTrackerDefinition> { DeathStranding() };
            definitions.AddRange(JsonSerializer.Deserialize<List<AchievementTrackerDefinition>>(stream, Json));
            using var orders = typeof(AchievementTrackingCatalog).Assembly.GetManifestResourceStream("PixelVault.Tracking.DeathStrandingOrders.json");
            definitions.Add(JsonSerializer.Deserialize<AchievementTrackerDefinition>(orders, Json));
            return definitions;
        }

        public static string AccountKey(string provider, string account)
        {
            var value = (account ?? "").Trim();
            return provider == "retroachievements" ? value.ToLowerInvariant() : value;
        }

        public static void Validate(AchievementTrackerDefinition definition)
        {
            if (definition == null || string.IsNullOrWhiteSpace(definition.Id) || definition.Id.Length > 200
                || string.IsNullOrWhiteSpace(definition.Provider) || string.IsNullOrWhiteSpace(definition.GameId)
                || string.IsNullOrWhiteSpace(definition.GameTitle) || string.IsNullOrWhiteSpace(definition.Title)
                || definition.Items == null || definition.Items.Count == 0 || definition.Items.Count > 10000)
                throw new InvalidDataException("A checklist needs a game, title and between 1 and 10,000 items.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in definition.Items)
                if (item == null || string.IsNullOrWhiteSpace(item.Id) || !ids.Add(item.Id)
                    || string.IsNullOrWhiteSpace(item.Name) || item.Target < 1 || item.Target > 1000000)
                    throw new InvalidDataException("Each checklist item needs a unique ID, name and positive target.");
            if (definition.Items.Any(i => i.Content?.Length > 100000)
                || definition.Items.Sum(i => (long)(i.Content?.Length ?? 0)) > 2000000)
                throw new InvalidDataException("Content supports up to 100,000 characters per item and 2,000,000 per tracker.");
            if (definition.Milestones == null || definition.CategoryTargets == null)
                throw new InvalidDataException("Milestones and category targets must be lists.");
            if (definition.Milestones.Count > 0)
            {
                if (definition.Milestones.Count > TrackerMilestones.Maximum || definition.Milestones.Any(string.IsNullOrWhiteSpace)
                    || definition.Milestones.Distinct(StringComparer.Ordinal).Count() != definition.Milestones.Count)
                    throw new InvalidDataException("Choose between one and sixteen distinct completion columns.");
                foreach (var group in definition.Items.GroupBy(i => i.GroupId))
                    if (string.IsNullOrWhiteSpace(group.Key) || group.Count() != definition.Milestones.Count
                        || !group.Select(i => i.Milestone).ToHashSet(StringComparer.Ordinal).SetEquals(definition.Milestones)
                        || group.Any(i => i.Target != 1 || i.Name != group.First().Name || i.Category != group.First().Category))
                        throw new InvalidDataException("Each milestone row needs one binary item per column with a shared name and category.");
                if(definition.Items.GroupBy(i=>i.GroupId).Any(g=>g.Select(i=>i.Archived).Distinct().Count()>1))
                    throw new InvalidDataException("Archive or restore every milestone of a record together.");
            }
            else if (definition.Items.Any(i => !string.IsNullOrEmpty(i.GroupId) || !string.IsNullOrEmpty(i.Milestone)) || definition.CategoryTargets.Count > 0)
                throw new InvalidDataException("Grouped items and category targets need milestone columns.");
            foreach (var target in definition.CategoryTargets)
                if (string.IsNullOrWhiteSpace(target.Key) || target.Value < 1 || target.Value > 1000000 || !definition.Items.Any(i => i.Category == target.Key))
                    throw new InvalidDataException("Category targets need an existing category and a positive target.");
        }

        // Any completed milestone qualifies a row once, even when several ranks are recorded.
        public static Dictionary<string, int> QualifyingRows(IEnumerable<AchievementTrackerState> states) => TrackerTotals.QualifyingRows(states);
    }
}
