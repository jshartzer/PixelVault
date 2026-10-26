using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PixelVaultNative
{
    public enum TrackerAvailability { Unknown, Available, Locked }

    public sealed class AchievementTrackerDefinition
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

    public sealed class AchievementTrackerItem
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Category { get; set; }
        public string Detail { get; set; }
        // Optional plain text, stored with private custom catalogues, never interpreted as markup.
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public string Content { get; set; }
        public int Target { get; set; } = 1;
        public int Order { get; set; }
        [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
        public bool Archived { get; set; }
        public string GroupId { get; set; }
        public string Milestone { get; set; }
        public Dictionary<string, string> Identifiers { get; set; } = new();
    }

    public sealed class AchievementTrackerProgress
    {
        public string ItemId { get; set; }
        public int? Current { get; set; }
        public TrackerAvailability Availability { get; set; }
        public string Note { get; set; }
    }

    /// <summary>Portable, account-bound observations. A snapshot never changes provider achievements or manual edits.</summary>
    public sealed class AchievementTrackerSnapshot
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

    public sealed class AchievementTrackerState
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

}
