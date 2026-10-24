using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class TrackerAutomationTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "PV-Automation-" + Guid.NewGuid().ToString("N"));
    readonly AchievementTrackingStore store;
    readonly TrackerRefreshService refresh;
    readonly List<TrackerAutomationMonitor> monitors = new();
    const string Id = "automation-test", Account = "player";
    DateTime now = DateTime.UtcNow.AddHours(-2);
    long observed = DateTime.UtcNow.AddDays(-1).Ticks;
    string FilePath => Path.Combine(dir, "current.json");
    public TrackerAutomationTests()
    {
        Directory.CreateDirectory(dir); store = new(dir);
        store.AddDefinition(new AchievementTrackerDefinition { Id = Id, Provider = "steam", GameId = "42", GameTitle = "A game", Title = "Goals",
            Items = new() { new() { Id = "one", Name = "First" }, new() { Id = "two", Name = "Second" } } });
        Write(0); refresh = new(store); refresh.Configure(Id, Account, JsonFileTrackerSource.AdapterId, "1", FilePath);
    }
    void Write(int value, long? ticks = null, string account = Account)
    {
        File.WriteAllText(FilePath, JsonSerializer.Serialize(new AchievementTrackerSnapshot { TrackerId = Id, Provider = "steam", GameId = "42", Account = account,
            Source = "Save fixture", ObservedUtcTicks = ticks ?? observed, Items = new() { new() { ItemId = "one", Current = value, Availability = TrackerAvailability.Available } } }));
    }
    void Enable(TrackerAutomationMode mode = TrackerAutomationMode.SaveChanges, bool paused = false) => store.SaveAutomation(Id, Account, store.Source(Id, Account).Id, mode, 1, paused);
    TrackerAutomationMonitor Monitor(TrackerRefreshService? service = null, Func<TrackerSourceBinding, string>? probe = null)
    {
        var monitor = new TrackerAutomationMonitor(store, service ?? refresh, () => now, probe); monitor.SetAccounts(Account, ""); monitors.Add(monitor); return monitor;
    }
    async Task Settle(TrackerAutomationMonitor monitor) { await monitor.TickAsync(); now = now.AddSeconds(5); await monitor.TickAsync(); }
    AchievementTrackerState State() => store.Load(Id, Account).Single(s => s.Item.Id == "one");
    public void Dispose() { foreach (var m in monitors) m.Dispose(); SqliteConnection.ClearAllPools(); Directory.Delete(dir, true); }

    [Fact]
    public async Task DefaultsOffAndSettingsSurviveRestartWithoutLeakingToAnotherAccount()
    {
        using var monitor = Monitor(); await Settle(monitor); Assert.Null(State().ReportedCurrent);
        Enable(TrackerAutomationMode.Periodic); var reopened = new AchievementTrackingStore(dir);
        Assert.Equal(TrackerAutomationMode.Periodic, reopened.Automation(Id, Account).Settings.Mode);
        Assert.Equal(TrackerAutomationMode.Manual, reopened.Automation(Id, "other").Settings.Mode);
        monitor.SetAccounts("other", ""); await Settle(monitor); Assert.Null(State().ReportedCurrent);
        monitor.SetAccounts(Account, ""); await Settle(monitor); Assert.Equal(0, State().ReportedCurrent);
    }
    [Fact]
    public async Task BurstWritesDebounceAndDuplicatesDoNotCreateHistoryOrNotices()
    {
        Enable(); using var monitor = Monitor(); int notices = 0; monitor.Notice += _ => notices++;
        await monitor.TickAsync(); Write(1, ++observed); now = now.AddSeconds(1); await monitor.TickAsync(); Assert.Null(State().Current);
        now = now.AddSeconds(2); await monitor.TickAsync(); Assert.Null(State().Current);
        now = now.AddSeconds(3); await monitor.TickAsync(); Assert.Equal(1, State().Current); Assert.Equal(1, notices);
        int count = store.History(Id, Account).Count; await Settle(monitor); Assert.Equal(count, store.History(Id, Account).Count); Assert.Equal(1, notices);
        File.SetLastWriteTimeUtc(FilePath, DateTime.UtcNow); await Settle(monitor); Assert.Equal(count, store.History(Id, Account).Count); Assert.Equal(1, notices);
    }
    [Fact]
    public async Task CloudReplacementWithUnchangedSizeAndTimestampIsDetected()
    {
        Enable(); using var monitor = Monitor(); await Settle(monitor); var stamp = File.GetLastWriteTimeUtc(FilePath); long length = new FileInfo(FilePath).Length;
        Write(1, ++observed); File.SetLastWriteTimeUtc(FilePath, stamp); Assert.Equal(length, new FileInfo(FilePath).Length);
        await Settle(monitor); Assert.Equal(1, State().Current);
    }
    [Fact]
    public async Task OnOpenOnlyReadsAfterAnExplicitOpenAndDoesNotBecomePeriodic()
    {
        Enable(TrackerAutomationMode.OnOpen); using var monitor = Monitor(); await Settle(monitor); Assert.Null(State().Current);
        monitor.Opened(Id, Account); await Settle(monitor); Assert.Equal(0, State().Current);
        Write(1, ++observed); now = now.AddHours(1); await Settle(monitor); Assert.Equal(0, State().Current);
        monitor.Opened(Id, Account); await Settle(monitor); Assert.Equal(1, State().Current);
    }
    [Fact]
    public async Task PeriodicRefreshWaitsForIntervalAndCatchesUpOnceAfterSleep()
    {
        Enable(TrackerAutomationMode.Periodic); using var monitor = Monitor(); await Settle(monitor); Write(1, ++observed);
        await Settle(monitor); Assert.Equal(0, State().Current);
        now = now.AddHours(1); await Settle(monitor); Assert.Equal(1, State().Current); Assert.Equal(2, store.History(Id, Account).Count);
    }
    [Fact]
    public async Task PausedAndReconfiguredSourcesStayOffUntilExplicitlyEnabled()
    {
        Enable(paused: true); using var monitor = Monitor(); await Settle(monitor); Assert.Null(State().Current);
        Enable(); await Settle(monitor); Assert.Equal(0, State().Current);
        refresh.Configure(Id, Account, JsonFileTrackerSource.AdapterId, "1", FilePath); Write(1, ++observed);
        Assert.Equal(TrackerAutomationMode.Manual, store.Automation(Id, Account).Settings.Mode);
        await Settle(monitor); Assert.Equal(0, State().Current);
    }
    [Fact]
    public async Task ManualConflictRequiresReviewAndPreservesAllProgressAndNotes()
    {
        store.SetManualProgress(Id, Account, "one", 1); store.SaveNote(Id, Account, "one", "My route"); Enable();
        using var monitor = Monitor(); int notices = 0; monitor.Notice += n => { Assert.True(n.NeedsReview); notices++; };
        await Settle(monitor); Assert.Equal(TrackerRefreshOutcome.ReviewRequired, store.SourceStatus(Id, Account).Outcome);
        Assert.Null(State().ReportedCurrent); Assert.Equal(1, State().ManualCurrent); Assert.Equal("My route", State().Note);
        File.SetLastWriteTimeUtc(FilePath, DateTime.UtcNow); await Settle(monitor); Assert.Equal(1, notices);
        var plan = await refresh.PrepareAsync(Id, Account, default); refresh.Apply(plan, false, null!);
        Assert.Equal(0, State().ReportedCurrent); Assert.Equal(1, State().ManualCurrent); Assert.Equal("My route", State().Note);
    }
    [Fact]
    public async Task OlderSavesAreNeverAcceptedAutomatically()
    {
        store.Import(File.ReadAllText(FilePath), Account); Write(1, observed - 10); Enable(); using var monitor = Monitor();
        await Settle(monitor); Assert.Equal(0, State().ReportedCurrent); Assert.Equal(TrackerRefreshOutcome.ReviewRequired, store.SourceStatus(Id, Account).Outcome);
    }
    [Fact]
    public async Task AnUndoneAutomaticBatchIsNotReappliedOnRestart()
    {
        Enable(); using (var first = Monitor()) await Settle(first);
        store.Undo(Id, Account, store.History(Id, Account).First().Id); Assert.Null(State().ReportedCurrent);
        using var next = Monitor(); await Settle(next); Assert.Null(State().ReportedCurrent); Assert.Equal(TrackerRefreshOutcome.ReviewRequired, store.SourceStatus(Id, Account).Outcome);
        var review = await refresh.PrepareAsync(Id, Account, default); refresh.Cancel(review);
        File.SetLastWriteTimeUtc(FilePath, DateTime.UtcNow); await Settle(next); Assert.Null(State().ReportedCurrent);
        refresh.Apply(await refresh.PrepareAsync(Id, Account, default), false, null!);
        Assert.False(store.Automation(Id, Account).State.ReviewPending); Assert.Equal(0, State().Current);
    }
    [Fact]
    public async Task ManualReviewIsNotInvalidatedByBackgroundWork()
    {
        Enable(); var plan = await refresh.PrepareAsync(Id, Account, default); using var monitor = Monitor();
        await Settle(monitor); Assert.Equal(plan.AttemptId, store.SourceStatus(Id, Account).AttemptId);
        Assert.Equal(1, refresh.Apply(plan, false, null!));
    }
    [Fact]
    public async Task FailedReadsBackOffAndIdenticalFailuresStayQuietAcrossRestart()
    {
        Enable(); File.WriteAllText(FilePath, "invalid"); int notices = 0;
        using (var monitor = Monitor())
        {
            monitor.Notice += _ => notices++; await Settle(monitor); Assert.Equal(1, notices); var last = store.Automation(Id, Account).State.LastCheckUtc;
            await Settle(monitor); Assert.Equal(last, store.Automation(Id, Account).State.LastCheckUtc);
        }
        using var restarted = Monitor(); restarted.Notice += _ => notices++; now = now.AddMinutes(2); await Settle(restarted);
        Assert.Equal(1, notices); Assert.Equal(2, store.Automation(Id, Account).State.Failures);
        Write(1, ++observed); now = now.AddMinutes(3); await Settle(restarted); Assert.Equal(1, State().Current); Assert.Equal(0, store.Automation(Id, Account).State.Failures);
    }
    [Fact]
    public async Task WrongAccountNeverChangesProgressAndMissingFilesRecover()
    {
        Enable(); Write(1, account: "other"); using var monitor = Monitor(); await Settle(monitor);
        Assert.Null(State().Current); Assert.Contains("another", store.Automation(Id, Account).State.Message);
        File.Delete(FilePath); now = now.AddMinutes(2); await monitor.TickAsync(); Assert.Null(State().Current);
        Write(1); now = now.AddMinutes(3); await Settle(monitor); Assert.Equal(1, State().Current);
    }
    sealed class BlockingSource : ITrackerDataSource
    {
        public string Id => "blocking";
        public string Version => "1";
        public TrackerSourceCapabilities Capabilities => TrackerSourceCapabilities.Refresh;
        internal TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Calls;
        public void Validate(TrackerSourceBinding b, AchievementTrackerDefinition d) { }
        public async Task<TrackerSourceObservation> ObserveAsync(TrackerSourceBinding b, AchievementTrackerDefinition d, CancellationToken token)
        { Interlocked.Increment(ref Calls); Started.TrySetResult(); await Task.Delay(Timeout.Infinite, token); throw new Exception(); }
    }
    [Theory]
    [InlineData("pause")]
    [InlineData("account")]
    [InlineData("source")]
    [InlineData("disconnect")]
    [InlineData("cancel")]
    [InlineData("shutdown")]
    public async Task LifecycleChangesCancelAnOwnedReadWithoutApplyingOrOverlapping(string action)
    {
        var source = new BlockingSource(); var service = new TrackerRefreshService(store, new[] { source }); service.Configure(Id, Account, source.Id, "1", FilePath); Enable();
        using var monitor = Monitor(service, _ => "same"); await monitor.TickAsync(); now = now.AddSeconds(5);
        var pending = monitor.TickAsync(); await source.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await monitor.TickAsync(); Assert.Equal(1, source.Calls);
        switch (action)
        {
            case "pause": Enable(paused: true); break;
            case "account": monitor.SetAccounts("other", ""); break;
            case "source": service.Configure(Id, Account, source.Id, "1", FilePath); break;
            case "disconnect": store.RemoveSource(Id, Account); break;
            case "cancel": monitor.Cancel(); break;
            case "shutdown": monitor.Dispose(); break;
        }
        await pending.WaitAsync(TimeSpan.FromSeconds(5)); Assert.Null(State().Current);
        Assert.Empty(store.History(Id, Account));
        if (action == "cancel") { await monitor.TickAsync(); Assert.Equal(1, source.Calls); }
    }
    [Fact]
    public async Task AutomationSettingsAreGuardedInsideTheApplyTransaction()
    {
        Enable(); var settings = store.Automation(Id, Account).Settings; var plan = await refresh.PrepareAsync(Id, Account, default);
        Enable(paused: true);
        Assert.ThrowsAny<OperationCanceledException>(() => refresh.Apply(plan, false, null!, default, settings.Id)); Assert.Null(State().Current);
    }
    [Fact]
    public void IntervalsAndStaleConfigurationAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => store.SaveAutomation(Id, Account, store.Source(Id, Account).Id, TrackerAutomationMode.Periodic, 0, false));
        Assert.Throws<InvalidOperationException>(() => store.SaveAutomation(Id, Account, "old-source", TrackerAutomationMode.SaveChanges, 5, false));
    }
    [Fact]
    public async Task AutomaticResultHistoryIsDurableAccountScopedAndBounded()
    {
        Enable(TrackerAutomationMode.Periodic); using var monitor = Monitor();
        for (int i = 0; i < 105; i++) { now = now.AddMinutes(2); await Settle(monitor); }
        Assert.Equal(10, new AchievementTrackingStore(dir).AutomationHistory(Id, Account).Count);
        Assert.Empty(store.AutomationHistory(Id, "other"));
        using var c = new SqliteConnection("Data Source=" + store.DatabasePath); c.Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT COUNT(*) FROM tracker_automation_result";
        Assert.Equal(100L, cmd.ExecuteScalar()); Assert.Single(store.History(Id, Account));
    }
}
