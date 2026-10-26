using Microsoft.Data.Sqlite;
using System.Text.Json;
namespace PixelVaultNative
{
    [Flags] public enum TrackerSourceCapabilities { None = 0, Refresh = 1, PartialObservations = 2, ExternalObservationTime = 4 }
    public enum TrackerRefreshOutcome { None, Reading, AwaitingReview, Success, Unchanged, Stale, Failed, Cancelled, Unsupported, Undone, ReviewRequired }

    public sealed class TrackerSourceBinding
    {
        public string Id { get; set; }
        public string TrackerId { get; set; }
        public string Account { get; set; }
        public string AdapterId { get; set; }
        public string AdapterVersion { get; set; }
        public string Location { get; set; }
        public string Profile { get; set; }
        public string GameDirectory { get; set; }
        public string SpecificSave { get; set; }
        public bool KeepSnapshots { get; set; } = true;
        public TrackerSpreadsheetMapping Spreadsheet { get; set; }
    }

    public sealed class TrackerSourceStatus
    {
        public string BindingId { get; set; }
        public string AttemptId { get; set; }
        public TrackerRefreshOutcome Outcome { get; set; }
        public long LastAttemptUtc { get; set; }
        public long LastSuccessUtc { get; set; }
        public long SourceObservedUtc { get; set; }
        public string Fingerprint { get; set; }
        public string Message { get; set; }
    }

    public sealed class TrackerReconcileRow
    {
        public string ItemId { get; init; }
        public string Name { get; init; }
        public int? Previous { get; init; }
        public int? Incoming { get; init; }
        public int? Manual { get; init; }
        public TrackerAvailability Availability { get; init; }
        public bool Changed { get; init; }
        public bool Stale { get; init; }
        public bool Conflict => Manual.HasValue && Manual != Incoming;
        public bool HasManual => Manual.HasValue;
        public bool UseSource { get; set; }
        public string IncomingNote { get; init; }
        public bool AddNote { get; init; }
        public string NoteAction => AddNote ? "Add to empty note" : string.IsNullOrWhiteSpace(IncomingNote) ? "—" : "Keep existing note";
        public string PreviousText => Previous?.ToString() ?? "Unknown";
        public string IncomingText => Incoming?.ToString() ?? "Unknown";
        public string ManualText => Manual?.ToString() ?? "—";
        public string Result => Stale ? "Older/equal observation" : Conflict ? "Manual conflict" : Changed ? "Changed" : "Unchanged";
    }

    public sealed class TrackerRefreshPlan
    {
        public string Json { get; init; }
        public string Baseline { get; init; }
        public string BindingId { get; init; }
        public string AttemptId { get; init; }
        public string TrackerId { get; init; }
        public string Account { get; init; }
        public string AdapterId { get; init; }
        public string AdapterVersion { get; init; }
        public string Fingerprint { get; init; }
        public IReadOnlyList<TrackerReconcileRow> Rows { get; init; }
        public long ObservedUtc { get; init; }
        public string Source { get; init; }
        public string Evidence { get; init; }
        public bool HasRollback => Rows.Any(r => r.Stale);
        public string Summary => Rows.Count(r => r.Changed) + " changed · " + Rows.Count(r => !r.Changed) + " unchanged · "
            + Rows.Count(r => !r.Incoming.HasValue) + " unknown · " + Rows.Count(r => r.Stale) + " older/equal · " + Rows.Count(r => r.Conflict) + " manual conflicts · " + Rows.Count(r => r.AddNote) + " note additions";
    }

    public sealed class TrackerSpreadsheetMapping
    {
        public string Worksheet { get; set; }
        public string Name { get; set; }
        public string Category { get; set; }
        public string Target { get; set; }
        public string Detail { get; set; }
        public string Id { get; set; }
        public string Current { get; set; }
        public string Completed { get; set; }
        public string Availability { get; set; }
        public string Note { get; set; }
        public string Group { get; set; }
        public string Milestone { get; set; }
        public List<TrackerMilestoneColumn> CompletionColumns { get; set; } = new();
        [System.Text.Json.Serialization.JsonIgnore]
        public string[] Headers => new[] { Name, Category, Target, Detail, Id, Current, Completed, Availability, Note,Group,Milestone }
            .Concat((CompletionColumns ?? new()).Select(c => c?.Header)).ToArray();
        [System.Text.Json.Serialization.JsonIgnore]
        public bool HasProgress => Headers.Skip(5).Take(4).Any(h => !string.IsNullOrEmpty(h)) || (CompletionColumns?.Any(c => !string.IsNullOrEmpty(c?.Header)) ?? false);
    }
    public enum TrackerAutomationMode { Manual, OnOpen, SaveChanges, Periodic }
    public sealed class TrackerAutomationSettings
    {
        public string Id { get; set; }
        public string BindingId { get; set; }
        public TrackerAutomationMode Mode { get; set; }
        public int IntervalMinutes { get; set; } = 5;
        public bool Paused { get; set; }
    }
    public sealed class TrackerAutomationState
    {
        public long LastCheckUtc { get; set; }
        public string Message { get; set; } = "Automatic refresh is off.";
        public string NoticeKey { get; set; }
        public int Failures { get; set; }
        public bool ReviewPending { get; set; }
    }

}
