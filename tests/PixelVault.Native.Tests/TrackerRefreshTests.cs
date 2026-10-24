using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class TrackerRefreshTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "PV-Refresh-" + Guid.NewGuid().ToString("N"));
    const string Tracker = "refresh-test", Account = "account";
    readonly AchievementTrackingStore store;
    string FilePath => Path.Combine(dir, "observations.json");
    public TrackerRefreshTests()
    {
        Directory.CreateDirectory(dir); store = new(dir);
        store.AddDefinition(new AchievementTrackerDefinition { Id = Tracker, Provider = "steam", GameId = "42", GameTitle = "A game", Title = "Goals",
            Items = new() { new() { Id = "one", Name = "First" }, new() { Id = "two", Name = "Second", Target = 3 }, new() { Id = "three", Name = "Third" } } });
    }
    public void Dispose()
    {
        var full = Path.GetFullPath(dir); var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(temp, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Test cleanup escaped its temporary root.");
        // Other test classes clear the shared SQLite pools concurrently; Windows can briefly retain a closing handle.
        for (int attempt = 0; ; attempt++)
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(full, true); break; }
            catch (IOException) when (attempt < 5) { Thread.Sleep(50); }
        }
    }
    AchievementTrackerSnapshot Snapshot(long? ticks = null, int? first = 1) => new() { TrackerId = Tracker, Provider = "steam", GameId = "42", Account = Account,
        Source = "Current save", Evidence = "Fixture", ObservedUtcTicks = ticks ?? DateTime.UtcNow.AddMinutes(-1).Ticks,
        Items = new() { new() { ItemId = "one", Current = first, Availability = TrackerAvailability.Available }, new() { ItemId = "two", Current = 2 } } };
    string Write(AchievementTrackerSnapshot s) { var json = JsonSerializer.Serialize(s); System.IO.File.WriteAllText(FilePath, json); return json; }
    TrackerRefreshService Configure(ITrackerDataSource? adapter = null)
    {
        var service = new TrackerRefreshService(store, adapter == null ? null : new[] { adapter });
        service.Configure(Tracker, Account, adapter?.Id ?? JsonFileTrackerSource.AdapterId, adapter?.Version ?? "1", FilePath, "Main profile"); return service;
    }
    AchievementTrackerState State(string item = "one") => store.Load(Tracker, Account).Single(s => s.Item.Id == item);

    [Fact]
    public async Task RealFileRefresh_PreviewsThenAppliesWhilePreservingManualNotesAndUnknownItems()
    {
        Write(Snapshot()); var service = Configure(); store.SetManualProgress(Tracker, Account, "one", 0); store.SaveNote(Tracker, Account, "one", "My plan");
        var plan = await service.PrepareAsync(Tracker, Account, default);
        Assert.Equal(2, plan.Rows.Count); Assert.Single(plan.Rows, r => r.Conflict); Assert.Null(State().ReportedCurrent);
        Assert.Equal(TrackerRefreshOutcome.AwaitingReview, store.SourceStatus(Tracker, Account).Outcome);
        Assert.Equal(2, service.Apply(plan, false, Array.Empty<string>()));
        Assert.Equal(0, State().Current); Assert.Equal(1, State().ReportedCurrent); Assert.Equal("My plan", State().Note); Assert.Null(State("three").Current);
        var binding = new AchievementTrackingStore(dir).Source(Tracker, Account); Assert.Equal("Main profile", binding.Profile); Assert.Equal(FilePath, binding.Location);
        Assert.Null(store.Source(Tracker, "different-account")); Assert.True(store.SourceStatus(Tracker, Account).LastSuccessUtc > 0);
    }

    [Fact]
    public async Task DuplicateFile_RecordsNoSpuriousHistoryAndUpdatedFileIsReread()
    {
        var snapshot = Snapshot(); Write(snapshot); var service = Configure(); service.Apply(await service.PrepareAsync(Tracker, Account, default), false, null!);
        var count = store.History(Tracker, Account).Count; var fingerprint = store.SourceStatus(Tracker, Account).Fingerprint;
        Assert.Equal(0, service.Apply(await service.PrepareAsync(Tracker, Account, default), false, null!)); Assert.Equal(count, store.History(Tracker, Account).Count);
        Assert.Equal(TrackerRefreshOutcome.Unchanged, store.SourceStatus(Tracker, Account).Outcome);
        snapshot.ObservedUtcTicks += TimeSpan.FromSeconds(1).Ticks; snapshot.Items[0].Current = 0; Write(snapshot);
        service.Apply(await service.PrepareAsync(Tracker, Account, default), false, null!); Assert.Equal(0, State().ReportedCurrent);
        Assert.NotEqual(fingerprint, store.SourceStatus(Tracker, Account).Fingerprint);
    }

    [Fact]
    public async Task OlderOrEqualConflictingObservations_NeedExplicitAcceptance()
    {
        var snapshot = Snapshot(); store.Import(Write(snapshot), Account); var service = Configure();
        snapshot.ObservedUtcTicks -= TimeSpan.FromDays(1).Ticks; snapshot.Items[0].Current = 0; Write(snapshot);
        var plan = await service.PrepareAsync(Tracker, Account, default); Assert.True(plan.HasRollback);
        Assert.Equal(0, service.Apply(plan, false, null!)); Assert.Equal(1, State().ReportedCurrent);
        Assert.Equal(TrackerRefreshOutcome.Stale, store.SourceStatus(Tracker, Account).Outcome); Assert.Equal(0, store.SourceStatus(Tracker, Account).LastSuccessUtc);
        Assert.Equal(2, service.Apply(await service.PrepareAsync(Tracker, Account, default), true, null!)); Assert.Equal(0, State().ReportedCurrent);
        snapshot.Items[0].Current = 1; Write(snapshot);
        Assert.True((await service.PrepareAsync(Tracker, Account, default)).HasRollback);
    }

    [Fact]
    public async Task ChoosingSourceForOneConflict_ClearsOnlyThatOverrideAndUndoRestoresEverything()
    {
        Write(Snapshot()); var service = Configure(); store.SetManualProgress(Tracker, Account, "one", 0); store.SetManualProgress(Tracker, Account, "two", 3);
        var plan = await service.PrepareAsync(Tracker, Account, default); service.Apply(plan, false, new[] { "one" });
        Assert.Null(State().ManualCurrent); Assert.Equal(3, State("two").ManualCurrent);
        var refresh = store.History(Tracker, Account).First(); store.SaveNote(Tracker, Account, "one", "A later note");
        store.Undo(Tracker, Account, refresh.Id);
        Assert.Equal(0, State().ManualCurrent); Assert.Null(State().ReportedCurrent); Assert.Equal("A later note", State().Note); Assert.Equal(3, State("two").ManualCurrent);
    }

    [Fact]
    public async Task StateChangesOrRebindingInvalidateAnOldPreviewAtomically()
    {
        Write(Snapshot()); var service = Configure(); var plan = await service.PrepareAsync(Tracker, Account, default);
        store.SaveNote(Tracker, Account, "two", "Changed while reviewing");
        Assert.Throws<InvalidOperationException>(() => service.Apply(plan, false, null!)); Assert.Null(State().ReportedCurrent);
        plan = await service.PrepareAsync(Tracker, Account, default); Configure();
        Assert.Throws<InvalidOperationException>(() => service.Apply(plan, false, null!)); Assert.Null(State().ReportedCurrent);
    }

    [Fact]
    public async Task WrongAccountCorruptionAndUnsupportedVersion_PreserveProgressAndLastSuccess()
    {
        var snapshot = Snapshot(); Write(snapshot); var service = Configure(); service.Apply(await service.PrepareAsync(Tracker, Account, default), false, null!);
        var last = store.SourceStatus(Tracker, Account).LastSuccessUtc; var count = store.History(Tracker, Account).Count;
        snapshot.Account = "another-account"; Write(snapshot);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.PrepareAsync(Tracker, Account, default));
        Assert.Equal(TrackerRefreshOutcome.Failed, store.SourceStatus(Tracker, Account).Outcome); Assert.Equal(last, store.SourceStatus(Tracker, Account).LastSuccessUtc);
        System.IO.File.WriteAllText(FilePath, "{unfinished"); await Assert.ThrowsAsync<JsonException>(() => service.PrepareAsync(Tracker, Account, default));
        Assert.Equal(1, State().ReportedCurrent); Assert.Equal(count, store.History(Tracker, Account).Count);
        var binding = store.Source(Tracker, Account); binding.AdapterVersion = "future"; store.SaveSource(binding);
        await Assert.ThrowsAsync<NotSupportedException>(() => service.PrepareAsync(Tracker, Account, default));
        Assert.Equal(TrackerRefreshOutcome.Unsupported, store.SourceStatus(Tracker, Account).Outcome);
    }

    sealed class WaitingAdapter : ITrackerDataSource
    {
        public string Id => "test.waiting"; public string Version => "1";
        public TrackerSourceCapabilities Capabilities => TrackerSourceCapabilities.Refresh;
        public TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Validate(TrackerSourceBinding b, AchievementTrackerDefinition d) { }
        public async Task<TrackerSourceObservation> ObserveAsync(TrackerSourceBinding b, AchievementTrackerDefinition d, CancellationToken token)
        { Started.SetResult(); await Task.Delay(Timeout.Infinite, token); throw new Exception("Unreachable"); }
    }
    [Fact]
    public async Task CancellationDuringReadAndBeforeCommit_LeavesNoPartialProgress()
    {
        var adapter = new WaitingAdapter(); var service = Configure(adapter); using var cts = new CancellationTokenSource();
        var pending = service.PrepareAsync(Tracker, Account, cts.Token); await adapter.Started.Task; cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(TrackerRefreshOutcome.Cancelled, store.SourceStatus(Tracker, Account).Outcome); Assert.Empty(store.History(Tracker, Account));
        Write(Snapshot()); service = Configure(); var plan = await service.PrepareAsync(Tracker, Account, default);
        Assert.ThrowsAny<OperationCanceledException>(() => service.Apply(plan, false, null!, cts.Token)); Assert.Null(State().Current); Assert.Empty(store.History(Tracker, Account));
    }

    [Fact]
    public async Task CancelledReviewDoesNotApplyAnObservation()
    {
        Write(Snapshot()); var service = Configure(); var plan = await service.PrepareAsync(Tracker, Account, default); service.Cancel(plan);
        Assert.Null(State().Current); Assert.Equal(TrackerRefreshOutcome.Cancelled, store.SourceStatus(Tracker, Account).Outcome);
        Assert.Throws<InvalidOperationException>(() => service.Apply(plan, false, null!));
        Assert.Equal(TrackerRefreshOutcome.Cancelled, store.SourceStatus(Tracker, Account).Outcome);
    }

    [Fact]
    public void UndoIsFieldScopedAccountScopedAndRejectsConflictingLaterWrites()
    {
        store.SetManualProgress(Tracker, Account, "one", 1); var first = store.History(Tracker, Account).Single();
        store.SaveNote(Tracker, Account, "one", "Later");
        Assert.Throws<InvalidDataException>(() => store.Undo(Tracker, "different-account", first.Id));
        store.Undo(Tracker, Account, first.Id); Assert.Null(State().Current); Assert.Equal("Later", State().Note);
        Assert.Throws<InvalidDataException>(() => store.Undo(Tracker, Account, first.Id));
        store.SetManualProgress(Tracker, Account, "one", 1); first = store.History(Tracker, Account).First();
        store.SetManualProgress(Tracker, Account, "one", 0);
        Assert.Throws<InvalidOperationException>(() => store.Undo(Tracker, Account, first.Id)); Assert.Equal(0, State().ManualCurrent);
    }

    [Fact]
    public void UndoImportDoesNotReplayPendingSeedButExplicitReimportWorks()
    {
        Directory.CreateDirectory(store.ImportDirectory); var json = Write(Snapshot());
        System.IO.File.WriteAllText(Path.Combine(store.ImportDirectory, "seed.json"), json);
        store.ImportPending(Tracker, Account); store.Undo(Tracker, Account, store.History(Tracker, Account).First().Id);
        Assert.Equal(0, store.ImportPending(Tracker, Account)); Assert.Null(State().Current);
        Assert.Equal(2, store.Import(json, Account)); Assert.True(State().Complete); Assert.Equal(0, store.Import(json, Account));
    }

    [Fact]
    public void UndoNoteRestoresOnlyTheNoteAndRetainsLaterManualProgress()
    {
        store.SaveNote(Tracker, Account, "one", "New note"); var note = store.History(Tracker, Account).Single();
        store.SetManualProgress(Tracker, Account, "one", 1); store.Undo(Tracker, Account, note.Id);
        Assert.Equal("", State().Note); Assert.Equal(1, State().ManualCurrent);
    }

    [Fact]
    public async Task UndoRefreshDoesNotPreventRereadingTheSameFile()
    {
        Write(Snapshot()); var service = Configure(); service.Apply(await service.PrepareAsync(Tracker, Account, default), false, null!);
        var batch = store.History(Tracker, Account).First(); store.SetManualProgress(Tracker, Account, "one", 0); store.Undo(Tracker, Account, batch.Id);
        Assert.Equal(0, State().ManualCurrent); Assert.Null(State().ReportedCurrent);
        service.Apply(await service.PrepareAsync(Tracker, Account, default), false, null!); Assert.Equal(1, State().ReportedCurrent); Assert.Equal(0, State().Current);
    }

    [Fact]
    public async Task FailureAfterFirstDatabaseWriteRollsBackAllObservationsAndHistory()
    {
        var snapshot = Snapshot(); Write(snapshot); var service = Configure(); service.Apply(await service.PrepareAsync(Tracker, Account, default), false, null!);
        var lastSuccess = store.SourceStatus(Tracker, Account).LastSuccessUtc; var history = store.History(Tracker, Account).Count;
        snapshot.ObservedUtcTicks += TimeSpan.FromSeconds(1).Ticks; snapshot.Items[0].Current = 0; snapshot.Items[1].Current = 3; Write(snapshot);
        using (var c = new SqliteConnection("Data Source=" + store.DatabasePath))
        {
            c.Open(); using var command = c.CreateCommand();
            command.CommandText = "CREATE TRIGGER fail_second BEFORE UPDATE ON tracker_item_state WHEN NEW.item_id='two' BEGIN SELECT RAISE(ABORT,'fixture failure'); END"; command.ExecuteNonQuery();
        }
        var plan = await service.PrepareAsync(Tracker, Account, default);
        Assert.Throws<SqliteException>(() => service.Apply(plan, false, null!));
        Assert.Equal(1, State().ReportedCurrent); Assert.Equal(2, State("two").ReportedCurrent); Assert.Equal(history, store.History(Tracker, Account).Count);
        Assert.Equal(lastSuccess, store.SourceStatus(Tracker, Account).LastSuccessUtc); Assert.Equal(TrackerRefreshOutcome.Failed, store.SourceStatus(Tracker, Account).Outcome);
    }

    [Fact]
    public async Task NewerAttemptInvalidatesOldPreviewWithoutDamagingNewAttempt()
    {
        Write(Snapshot()); var service = Configure(); var old = await service.PrepareAsync(Tracker, Account, default);
        var current = await service.PrepareAsync(Tracker, Account, default);
        Assert.Throws<InvalidOperationException>(() => service.Apply(old, false, null!));
        Assert.Equal(TrackerRefreshOutcome.AwaitingReview, store.SourceStatus(Tracker, Account).Outcome);
        Assert.Equal(2, service.Apply(current, false, null!));
    }

    [Theory]
    [InlineData("game")][InlineData("tracker")][InlineData("version")][InlineData("unknown")][InlineData("duplicate")]
    [InlineData("negative")][InlineData("future")][InlineData("empty")][InlineData("availability")]
    public async Task InvalidAdapterObservationsAreRejectedBeforeAnyWrites(string invalid)
    {
        var snapshot = Snapshot();
        switch (invalid)
        {
            case "game": snapshot.GameId = "wrong"; break;
            case "tracker": snapshot.TrackerId = "wrong"; break;
            case "version": snapshot.Version = 99; break;
            case "unknown": snapshot.Items[0].ItemId = "wrong"; break;
            case "duplicate": snapshot.Items.Add(snapshot.Items[0]); break;
            case "negative": snapshot.Items[0].Current = -1; break;
            case "future": snapshot.ObservedUtcTicks = DateTime.UtcNow.AddDays(1).Ticks; break;
            case "empty": snapshot.Items.Clear(); break;
            case "availability": snapshot.Items[0].Availability = (TrackerAvailability)99; break;
        }
        Write(snapshot); var service = Configure();
        await Assert.ThrowsAsync<InvalidDataException>(() => service.PrepareAsync(Tracker, Account, default));
        Assert.Empty(store.History(Tracker, Account)); Assert.All(store.Load(Tracker, Account), s => Assert.Null(s.Current));
    }

}
