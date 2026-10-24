using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PixelVaultNative
{
    [Flags] enum TrackerSourceCapabilities { None = 0, Refresh = 1, PartialObservations = 2, ExternalObservationTime = 4 }
    enum TrackerRefreshOutcome { None, Reading, AwaitingReview, Success, Unchanged, Stale, Failed, Cancelled, Unsupported, Undone, ReviewRequired }

    sealed class TrackerSourceBinding
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

    sealed class TrackerSourceStatus
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

    sealed class TrackerSourceObservation
    {
        public string SnapshotJson { get; init; }
        public string Fingerprint { get; init; }
    }

    interface ITrackerDataSource
    {
        string Id { get; }
        string Version { get; }
        TrackerSourceCapabilities Capabilities { get; }
        void Validate(TrackerSourceBinding binding, AchievementTrackerDefinition definition);
        Task<TrackerSourceObservation> ObserveAsync(TrackerSourceBinding binding, AchievementTrackerDefinition definition, CancellationToken token);
    }
    interface ITrackerSourceChangeDetector
    {
        string ChangeSignature(TrackerSourceBinding binding);
    }

    /// <summary>A first reusable source: reread a configured observation file, without invoking any executable.</summary>
    sealed class JsonFileTrackerSource : ITrackerDataSource, ITrackerSourceChangeDetector
    {
        internal const string AdapterId = "pixelvault.json-file";
        public string Id => AdapterId;
        public string Version => "1";
        public TrackerSourceCapabilities Capabilities => TrackerSourceCapabilities.Refresh | TrackerSourceCapabilities.PartialObservations;
        public string ChangeSignature(TrackerSourceBinding binding) => TrackerSourceFiles.Signature(new[] { new FileInfo(binding.Location) }, 4 * 1024 * 1024);
        public void Validate(TrackerSourceBinding binding, AchievementTrackerDefinition definition)
        {
            if (binding.AdapterId != Id || binding.AdapterVersion != Version) throw new NotSupportedException("This source adapter version is not supported.");
            if (!Path.IsPathFullyQualified(binding.Location ?? "") || !string.Equals(Path.GetExtension(binding.Location), ".json", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Choose a full path to a checklist progress JSON file.");
        }
        public async Task<TrackerSourceObservation> ObserveAsync(TrackerSourceBinding binding, AchievementTrackerDefinition definition, CancellationToken token)
        {
            Validate(binding, definition); token.ThrowIfCancellationRequested();
            var before = new FileInfo(binding.Location); var size = before.Length; var stamp = before.LastWriteTimeUtc;
            if (size == 0 || size > 4 * 1024 * 1024) throw new InvalidDataException("Progress files must contain 1 byte to 4 MiB.");
            byte[] bytes = new byte[checked((int)size)];
            using (var file = new FileStream(binding.Location, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, FileOptions.Asynchronous))
            {
                if (file.Length != size) throw new IOException("The source changed while being opened. Try Refresh again.");
                await file.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
                var after = new FileInfo(binding.Location);
                if (file.Length != size || after.Length != size || after.LastWriteTimeUtc != stamp)
                    throw new IOException("The source is being updated. Try Refresh after saving finishes.");
            }
            token.ThrowIfCancellationRequested();
            return new TrackerSourceObservation { SnapshotJson = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF'), Fingerprint = Convert.ToHexString(SHA256.HashData(bytes)) };
        }
    }

    sealed class TrackerReconcileRow
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

    sealed class TrackerRefreshPlan
    {
        internal string Json { get; init; }
        internal string Baseline { get; init; }
        internal string BindingId { get; init; }
        internal string AttemptId { get; init; }
        internal string TrackerId { get; init; }
        internal string Account { get; init; }
        internal string AdapterId { get; init; }
        internal string AdapterVersion { get; init; }
        internal string Fingerprint { get; init; }
        internal IReadOnlyList<TrackerReconcileRow> Rows { get; init; }
        internal long ObservedUtc { get; init; }
        internal string Source { get; init; }
        internal string Evidence { get; init; }
        internal bool HasRollback => Rows.Any(r => r.Stale);
        internal string Summary => Rows.Count(r => r.Changed) + " changed · " + Rows.Count(r => !r.Changed) + " unchanged · "
            + Rows.Count(r => !r.Incoming.HasValue) + " unknown · " + Rows.Count(r => r.Stale) + " older/equal · " + Rows.Count(r => r.Conflict) + " manual conflicts · " + Rows.Count(r => r.AddNote) + " note additions";
    }

    sealed class TrackerRefreshService
    {
        readonly AchievementTrackingStore store;
        readonly Dictionary<string, ITrackerDataSource> adapters;
        static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.Ordinal);
        internal TrackerRefreshService(AchievementTrackingStore store, IEnumerable<ITrackerDataSource> adapters = null)
        {
            this.store = store;
            this.adapters = (adapters ?? new ITrackerDataSource[] { new JsonFileTrackerSource(), new SpreadsheetTrackerSource(), new DeathStrandingTrackerSource(Path.GetDirectoryName(store.DatabasePath)) }).ToDictionary(a => a.Id, StringComparer.Ordinal);
        }
        internal void Configure(string trackerId, string account, string adapterId, string version, string location, string profile = "", string gameDirectory = null, string specificSave = null, bool keepSnapshots = true, TrackerSpreadsheetMapping spreadsheet = null)
        {
            var definition = store.Definitions().First(d => d.Id == trackerId);
            if (!adapters.TryGetValue(adapterId, out var adapter)) throw new NotSupportedException("This tracker source is not installed.");
            var binding = new TrackerSourceBinding { TrackerId = trackerId, Account = account, AdapterId = adapterId, AdapterVersion = version, Location = location, Profile = profile ?? "", GameDirectory = gameDirectory, SpecificSave = specificSave, KeepSnapshots = keepSnapshots, Spreadsheet = spreadsheet };
            adapter.Validate(binding, definition); store.SaveSource(binding);
        }
        internal string ChangeSignature(TrackerSourceBinding binding) => adapters.TryGetValue(binding.AdapterId, out var source) && source is ITrackerSourceChangeDetector detector
            ? detector.ChangeSignature(binding) : throw new NotSupportedException("This source has no automatic change detector.");
        internal TrackerConnectionInfo Connection(AchievementTrackerDefinition definition,TrackerSourceBinding binding,TrackerAutomationSettings settings,TrackerAutomationState state)
            =>TrackerPresentation.Connection(definition,binding,settings,state,adapters.Values);
        internal async Task<TrackerRefreshPlan> PrepareAsync(string trackerId, string account, CancellationToken token, bool automatic = false)
        {
            var definition = store.Definitions().First(d => d.Id == trackerId);
            account = AchievementTrackingCatalog.AccountKey(definition.Provider, account);
            var gate = Gates.GetOrAdd(store.DatabasePath + "\n" + trackerId + "\n" + account, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(token).ConfigureAwait(false);
            TrackerSourceBinding binding = null; string attempt = null;
            try
            {
                token.ThrowIfCancellationRequested(); binding = store.Source(trackerId, account) ?? throw new InvalidDataException("Configure a source before refreshing.");
                if (automatic && store.SourceStatus(trackerId, account)?.Outcome is TrackerRefreshOutcome.Reading or TrackerRefreshOutcome.AwaitingReview)
                    return null; // A manual read/review owns this source until it finishes.
                attempt = store.StartRefresh(binding);
                if (!adapters.TryGetValue(binding.AdapterId, out var adapter) || binding.AdapterVersion != adapter.Version
                    || !adapter.Capabilities.HasFlag(TrackerSourceCapabilities.Refresh)) throw new NotSupportedException("This tracker source or adapter version is not supported.");
                adapter.Validate(binding, definition);
                var observation = await adapter.ObserveAsync(binding, definition, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                var json = observation.SnapshotJson;
                var prior = store.SourceStatus(trackerId, account);
                if (prior?.Fingerprint == observation.Fingerprint && prior.SourceObservedUtc > 0 && adapter.Capabilities.HasFlag(TrackerSourceCapabilities.ExternalObservationTime))
                {
                    // A byte-identical checkpoint may have been copied or touched. Its content is still the same observation.
                    var snapshot = JsonSerializer.Deserialize<AchievementTrackerSnapshot>(json, AchievementTrackingCatalog.Json);
                    if (snapshot != null) { snapshot.ObservedUtcTicks = prior.SourceObservedUtc; json = JsonSerializer.Serialize(snapshot, AchievementTrackingCatalog.Json); }
                }
                var plan = store.Preview(json, trackerId, account, adapter.Id, adapter.Version, observation.Fingerprint, binding.Id, attempt);
                token.ThrowIfCancellationRequested();
                if (!store.FinishAttempt(binding, attempt, TrackerRefreshOutcome.AwaitingReview, plan.Summary)) throw new InvalidOperationException("The source changed during the read. Refresh again.");
                return plan;
            }
            catch (Exception ex)
            {
                if (attempt != null) store.FinishAttempt(binding, attempt, ex is OperationCanceledException ? TrackerRefreshOutcome.Cancelled
                    : ex is NotSupportedException ? TrackerRefreshOutcome.Unsupported : TrackerRefreshOutcome.Failed, ex is OperationCanceledException ? "Refresh cancelled. Saved progress was preserved." : ex.Message);
                throw;
            }
            finally { gate.Release(); }
        }
        internal int Apply(TrackerRefreshPlan plan, bool allowOlder, IEnumerable<string> useSourceItems, CancellationToken token = default, string automationId = null)
        {
            try { return store.ApplyRefresh(plan, allowOlder, useSourceItems, token, automationId); }
            catch (Exception ex)
            {
                var binding = store.Source(plan.TrackerId, plan.Account);
                if (binding != null && binding.Id == plan.BindingId && store.SourceStatus(plan.TrackerId, plan.Account)?.Outcome == TrackerRefreshOutcome.AwaitingReview)
                    store.FinishAttempt(binding, plan.AttemptId, ex is OperationCanceledException ? TrackerRefreshOutcome.Cancelled : TrackerRefreshOutcome.Failed, ex.Message);
                throw;
            }
        }
        internal void Cancel(TrackerRefreshPlan plan)
        {
            var binding = store.Source(plan.TrackerId, plan.Account);
            if (binding?.Id == plan.BindingId) store.FinishAttempt(binding, plan.AttemptId, TrackerRefreshOutcome.Cancelled, "Review cancelled. Saved progress was preserved.", TrackerRefreshOutcome.AwaitingReview);
        }
    }
}
