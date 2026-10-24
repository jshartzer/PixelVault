using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PixelVaultNative
{
    enum TrackerAvailability { Unknown, Available, Locked }

    sealed class AchievementTrackerDefinition
    {
        public string Id { get; set; }
        public string Provider { get; set; }
        public string GameId { get; set; }
        public string GameTitle { get; set; }
        public string AchievementId { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string CompletionLabel { get; set; } = "Completed";
        public List<string> Milestones { get; set; } = new();
        public Dictionary<string, int> CategoryTargets { get; set; } = new();
        public List<AchievementTrackerItem> Items { get; set; } = new();
        public override string ToString() => GameTitle + " — " + Title;
    }

    sealed class AchievementTrackerItem
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Category { get; set; }
        public string Detail { get; set; }
        public int Target { get; set; } = 1;
        public int Order { get; set; }
        [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
        public bool Archived { get; set; }
        public string GroupId { get; set; }
        public string Milestone { get; set; }
        public Dictionary<string, string> Identifiers { get; set; } = new();
    }

    sealed class AchievementTrackerProgress
    {
        public string ItemId { get; set; }
        public int? Current { get; set; }
        public TrackerAvailability Availability { get; set; }
        public string Note { get; set; }
    }

    /// <summary>Portable, account-bound observations. A snapshot never changes provider achievements or manual edits.</summary>
    sealed class AchievementTrackerSnapshot
    {
        public int Version { get; set; } = 1;
        public string TrackerId { get; set; }
        public string Provider { get; set; }
        public string GameId { get; set; }
        public string Account { get; set; }
        public string Source { get; set; }
        public string Evidence { get; set; }
        public long ObservedUtcTicks { get; set; }
        public List<AchievementTrackerProgress> Items { get; set; } = new();
    }

    sealed class AchievementTrackerState
    {
        public AchievementTrackerItem Item { get; set; }
        public int? ReportedCurrent { get; set; }
        public int? ManualCurrent { get; set; }
        public int? Current => ManualCurrent ?? ReportedCurrent;
        public bool IsKnown => Current.HasValue;
        public bool Complete => Current >= Item.Target;
        public TrackerAvailability Availability { get; set; }
        public string Note { get; set; } = "";
        public string Source { get; set; } = "";
        public string Evidence { get; set; } = "";
        public long ObservedUtcTicks { get; set; }
        public long ManualUtcTicks { get; set; }
        public bool IsManual => ManualCurrent.HasValue;
        public bool HasConflict => IsManual && ObservedUtcTicks > 0 && ManualCurrent != ReportedCurrent;
        public string Status => Complete ? "Completed" : !IsKnown ? "Not checked"
            : Availability == TrackerAvailability.Locked ? "Locked" : Availability == TrackerAvailability.Available ? "Ready" : "Remaining";
    }

    static class AchievementTrackingCatalog
    {
        internal const string DeathStrandingId = "death-stranding-dc.homo-faber";
        internal const string DeathStrandingBuildId = "death-stranding-dc.master-builder";
        internal const string DeathStrandingUpgradeId = "death-stranding-dc.trail-blazer";
        internal const string DeathStrandingChipsId = "death-stranding-dc.fount-of-knowledge";
        internal const string DeathStrandingOrdersId = "death-stranding-dc.growth-of-a-legend";
        internal static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

        internal static AchievementTrackerDefinition DeathStranding()
        {
            using var stream = typeof(AchievementTrackingCatalog).Assembly.GetManifestResourceStream("PixelVault.Tracking.DeathStranding.json");
            return JsonSerializer.Deserialize<AchievementTrackerDefinition>(stream, Json);
        }
        internal static IReadOnlyList<AchievementTrackerDefinition> Builtins()
        {
            using var stream = typeof(AchievementTrackingCatalog).Assembly.GetManifestResourceStream("PixelVault.Tracking.DeathStrandingWorld.json");
            var definitions = new List<AchievementTrackerDefinition> { DeathStranding() };
            definitions.AddRange(JsonSerializer.Deserialize<List<AchievementTrackerDefinition>>(stream, Json));
            using var orders = typeof(AchievementTrackingCatalog).Assembly.GetManifestResourceStream("PixelVault.Tracking.DeathStrandingOrders.json");
            definitions.Add(JsonSerializer.Deserialize<AchievementTrackerDefinition>(orders, Json));
            return definitions;
        }

        internal static string AccountKey(string provider, string account)
        {
            var value = (account ?? "").Trim();
            return provider == "retroachievements" ? value.ToLowerInvariant() : value;
        }

        internal static void Validate(AchievementTrackerDefinition definition)
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
        internal static Dictionary<string, int> QualifyingRows(IEnumerable<AchievementTrackerState> states) => states
            .Where(s => s.Complete).GroupBy(s => s.Item.Category ?? "Other")
            .ToDictionary(g => g.Key, g => g.Select(s => s.Item.GroupId).Distinct(StringComparer.Ordinal).Count());
    }
}
