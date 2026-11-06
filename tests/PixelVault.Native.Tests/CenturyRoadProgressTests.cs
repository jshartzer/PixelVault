using System.IO;
using System.Text.Json;
using PixelVault.Tracking;
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class CenturyRoadProgressTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "pv-century-progress-" + Guid.NewGuid().ToString("N"));
    readonly AchievementTrackingStore store;
    readonly AchievementTrackerDefinition definition;
    readonly TrackerSourceBinding binding;
    readonly long date = DateTime.UtcNow.AddDays(-2).Ticks;
    const string Account = "76561198000000001";
    const string Id = CenturyAchievementCatalog.DeathStrandingRoadsId;
    public CenturyRoadProgressTests()
    {
        store = new(root); definition = DeathStrandingRoadReader.Definition(); definition.Id = "custom.roads";
        definition.AchievementId = Id; store.AddDefinition(definition);
        binding = new() { TrackerId = definition.Id, Account = Account, AdapterId = DeathStrandingTrackerSource.AdapterId, AdapterVersion = "1", Location = root };
        store.SaveSource(binding);
    }
    TrackerRefreshPlan Plan(int count, long? observed = null, string adapter = DeathStrandingTrackerSource.AdapterId, bool bound = true, bool unknown = false)
    {
        var snapshot = new AchievementTrackerSnapshot { TrackerId = definition.Id, Provider = "steam", GameId = "1850570", Account = Account,
            ObservedUtcTicks = observed ?? date, Source = "Verified save", Evidence = "Road reader v1; full map validated.",
            Items = definition.Items.Select((i, index) => new AchievementTrackerProgress { ItemId = i.Id, Current = unknown && index == 0 ? null : index < count ? 1 : 0 }).ToList() };
        string? attempt = null;
        if (bound) { attempt = store.StartRefresh(binding); store.FinishAttempt(binding, attempt, TrackerRefreshOutcome.AwaitingReview, "Ready"); }
        var fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(count + ":" + snapshot.ObservedUtcTicks + ":" + unknown)));
        return store.Preview(JsonSerializer.Serialize(snapshot, AchievementTrackingCatalog.Json), definition.Id, Account, adapter, "1", fingerprint, bound ? binding.Id : null, attempt);
    }
    CenturyProgress? Progress => store.Century("steam", "1850570", Id, Account);
    void Apply(TrackerRefreshPlan plan, bool older = false) => store.ApplyRefresh(plan, older, [], CancellationToken.None);

    [Fact] public void VerifiedPartialProgressIsDurableAccountScopedAndIndependentOfManualChecks()
    {
        foreach (var item in definition.Items) store.SetManualProgress(definition.Id, Account, item.Id, 1);
        store.SaveNote(definition.Id, Account, definition.Items[0].Id, "Keep my note");
        Apply(Plan(28));
        Assert.Equal(28, Progress!.Current); Assert.Equal(51, Progress.Target); Assert.False(Progress.Unlocked); Assert.Equal(0, Progress.EarnedPoints);
        Assert.Equal(51, store.Load(definition.Id, Account).Count(i => i.Complete));
        Assert.Equal("Keep my note", store.Load(definition.Id, Account)[0].Note);
        Assert.Equal(binding.Id, Progress.BindingId); Assert.Equal(date, Progress.ObservedUtc); Assert.Equal(64, Progress.Fingerprint.Length);
        Assert.Equal(Progress, new AchievementTrackingStore(root).Century("steam", "1850570", Id, Account));
        Assert.Null(store.Century("steam", "1850570", Id, "another-account"));
        Assert.Null(store.Century("steam", "42", Id, Account));
        Assert.Null(store.Century("steam", "1850570", CenturyAchievementCatalog.DeathStrandingInterviewsId, Account));
        var row = AchievementsInfoWindow.CenturyPresentation(CenturyAchievementCatalog.Find("steam", "1850570", Id), Progress);
        Assert.True(row.ProgressKnown); Assert.False(row.Unlocked); Assert.Equal(28, row.CounterCurrent); Assert.Equal(51, row.CounterTarget);
    }
    [Theory] [InlineData("json-import", false)] [InlineData("pixelvault.json-file", true)] [InlineData(DeathStrandingTrackerSource.AdapterId, false)]
    public void ImportsAndUnboundObservationsCannotAwardCentury(string adapter, bool bound)
    { Apply(Plan(51, adapter: adapter, bound: bound)); Assert.Null(Progress); }

    [Fact] public void CompleteSaveAwardsOnceAtFirstVerificationAndSurvivesRollbackDisconnectAndManualEdits()
    {
        Apply(Plan(51)); var earned = Progress!;
        Assert.True(earned.Unlocked); Assert.Equal(100, earned.EarnedPoints); Assert.True(earned.UnlockUtc > date);
        Apply(Plan(51)); Assert.Equal(earned, Progress);
        Apply(Plan(28, date + TimeSpan.TicksPerHour));
        Assert.Equal(28, Progress!.Current); Assert.Equal(earned.UnlockUtc, Progress.UnlockUtc);
        Apply(Plan(51, date - TimeSpan.TicksPerHour), older: true); Assert.Equal(28, Progress.Current);
        store.RemoveSource(definition.Id, Account); store.SetManualProgress(definition.Id, Account, definition.Items[0].Id, 0);
        Assert.Equal(earned.UnlockUtc, Progress.UnlockUtc);
        var row = AchievementsInfoWindow.CenturyPresentation(CenturyAchievementCatalog.Find("steam", "1850570", Id), Progress);
        Assert.Equal(100, row.AwardedPoints); Assert.True(row.Unlocked);
        var game = new AchievementHubGame { Provider = "steam", Id = "1850570" };
        var accounts = new Dictionary<string, AchievementHubStore.AccountData> { ["steam"] = new() { Account = Account } };
        var entry = Assert.Single(LibraryTimelineAchievement.Select([game], accounts, DateTime.MinValue, DateTime.MaxValue, DateTime.UtcNow.Ticks, TimeZoneInfo.Utc, store));
        Assert.Equal(Id, entry.Row.CenturyDefinitionId); Assert.Empty(accounts["steam"].Games);
    }
    [Fact] public void PartialUnknownFailureAndReconfiguredAttemptPreserveLastVerifiedObservation()
    {
        Apply(Plan(28)); var before = Progress;
        Apply(Plan(51, date + TimeSpan.TicksPerHour, unknown: true)); Assert.Equal(before, Progress);
        var attempt = store.StartRefresh(binding); store.FinishAttempt(binding, attempt, TrackerRefreshOutcome.Failed, "Unreadable save"); Assert.Equal(before, Progress);
        var plan = Plan(51, date + TimeSpan.TicksPerDay); store.SaveSource(binding);
        Assert.Throws<InvalidOperationException>(() => Apply(plan)); Assert.Equal(before, Progress);
    }
    [Fact] public void UnchangedAutomaticAcknowledgementEstablishesVerifiedProgress()
    {
        Apply(Plan(28, adapter: "json-import", bound: false)); Assert.Null(Progress);
        store.SaveAutomation(definition.Id, Account, binding.Id, TrackerAutomationMode.SaveChanges, 5, false);
        var settings = store.Automation(definition.Id, Account).Settings;
        var plan = Plan(28);
        Assert.True(store.RecordAutomatic(binding, settings.Id, new() { LastCheckUtc = DateTime.UtcNow.Ticks }, plan, TrackerRefreshOutcome.Unchanged, acknowledge: true));
        Assert.Equal(28, Progress!.Current);
    }
    public void Dispose() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
}
