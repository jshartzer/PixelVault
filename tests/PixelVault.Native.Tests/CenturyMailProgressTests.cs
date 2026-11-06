using System.IO;
using System.Text.Json;
using PixelVault.Tracking;
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class CenturyMailProgressTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "pv-century-mail-" + Guid.NewGuid().ToString("N"));
    readonly AchievementTrackingStore store;
    readonly AchievementTrackerDefinition definition = DeathStrandingMailCatalogueReader.Definition();
    readonly TrackerSourceBinding binding;
    readonly long date = DateTime.UtcNow.AddDays(-2).Ticks;
    const string Account = "76561198000000001";
    const string Id = CenturyAchievementCatalog.DeathStrandingMailsId;
    public CenturyMailProgressTests()
    {
        store = new(root); store.AddDefinition(definition);
        binding = new() { TrackerId = definition.Id, Account = Account, AdapterId = DeathStrandingTrackerSource.AdapterId, AdapterVersion = "1", Location = root };
        store.SaveSource(binding);
    }
    TrackerRefreshPlan Plan(int read, long? observed = null, bool unknown = true, bool bound = true, string adapter = DeathStrandingTrackerSource.AdapterId, bool missingKnown = false)
    {
        var known = 0;
        var snapshot = new AchievementTrackerSnapshot { TrackerId = definition.Id, Provider = "steam", GameId = "1850570", Account = Account,
            ObservedUtcTicks = observed ?? date, Source = "Validated email save", Evidence = "Complete mail catalogue observation",
            Items = definition.Items.Select(i => new AchievementTrackerProgress { ItemId = i.Id,
                Current = i.Id == CenturyMailCatalogue.UnmappedId ? unknown ? null : read == 240 ? 1 : 0 : known++ < read ? 1 : 0 }).ToList() };
        if (missingKnown) snapshot.Items[0].Current = null;
        string? attempt = null;
        if (bound) { attempt = store.StartRefresh(binding); store.FinishAttempt(binding, attempt, TrackerRefreshOutcome.AwaitingReview, "Ready"); }
        var json = JsonSerializer.Serialize(snapshot, AchievementTrackingCatalog.Json);
        var fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json)));
        return store.Preview(json, definition.Id, Account, adapter, "1", fingerprint, bound ? binding.Id : null, attempt);
    }
    CenturyProgress? Progress => store.Century("steam", "1850570", Id, Account);
    void Apply(TrackerRefreshPlan plan, bool older = false) => store.ApplyRefresh(plan, older, [], CancellationToken.None);

    [Fact] public void FullCatalogueCountsReadStatesWithoutManualCreditOrTheOfficialHundredMailThreshold()
    {
        Assert.True(CenturyMailCatalogue.ItemIds.SetEquals(definition.Items.Select(i => i.Id)));
        Assert.Equal(DeathStrandingMailCatalogueReader.References().UnmappedId, CenturyMailCatalogue.UnmappedId);
        foreach (var item in definition.Items) store.SetManualProgress(definition.Id, Account, item.Id, 1);
        store.SaveNote(definition.Id, Account, definition.Items[0].Id, "My mail note");
        Apply(Plan(144));
        Assert.Equal(140, Progress!.Current); Assert.Equal(236, Progress.Target); Assert.Equal(1, Progress.Unknown);
        Assert.False(Progress.Unlocked); Assert.Equal(0, Progress.EarnedPoints);
        Assert.Equal(240, store.Load(definition.Id, Account).Count(i => i.Complete));
        Assert.Equal("My mail note", store.Load(definition.Id, Account)[0].Note);
        Assert.Null(store.Century("steam", "1850570", Id, "other-account"));
        Assert.Null(store.Century("steam", "42", Id, Account));
        Assert.Null(store.Century("steam", "1850570", CenturyAchievementCatalog.DeathStrandingRoadsId, Account));
        var row = AchievementsInfoWindow.CenturyPresentation(CenturyAchievementCatalog.Find("steam", "1850570", Id), Progress);
        Assert.Equal(100.0 * 140 / 236, AchievementCollectionStats.CounterPercent(row)!.Value, 8); Assert.Equal("140 / 236", row.Meta);
        Assert.False(row.HasStableProviderIdentity);
    }
    [Fact] public void UnknownEntryCannotShrinkTheGoalOrUnlockAt239AndCompleteEvidenceAwardsOnce()
    {
        Apply(Plan(239)); Assert.Equal(235, Progress!.Current); Assert.Equal(236, Progress.Target); Assert.False(Progress.Unlocked);
        Apply(Plan(240, date + TimeSpan.TicksPerHour, unknown: false)); var earned = Progress!;
        Assert.True(earned.Unlocked); Assert.Equal(100, earned.EarnedPoints); Assert.Equal(0, earned.Unknown);
        Apply(Plan(240, date + TimeSpan.TicksPerHour, unknown: false)); Assert.Equal(earned, Progress);
        Apply(Plan(144, date + TimeSpan.TicksPerDay)); Assert.Equal(earned.UnlockUtc, Progress!.UnlockUtc);
        Apply(Plan(239, date - TimeSpan.TicksPerHour), older: true); Assert.Equal(140, Progress.Current);
    }
    [Theory] [InlineData("json-import", false)] [InlineData("pixelvault.json-file", true)] [InlineData(DeathStrandingTrackerSource.AdapterId, false)]
    public void ImportedAndUnboundValuesCannotCreateCenturyProgress(string adapter, bool bound)
    { Apply(Plan(240, unknown: false, bound: bound, adapter: adapter)); Assert.Null(Progress); }

    [Fact] public void MissingKnownEntryFailureAndChangedBindingPreserveTheLastVerifiedResult()
    {
        Apply(Plan(144)); var prior = Progress;
        Apply(Plan(239, date + TimeSpan.TicksPerHour, missingKnown: true)); Assert.Equal(prior, Progress);
        var attempt = store.StartRefresh(binding); store.FinishAttempt(binding, attempt, TrackerRefreshOutcome.Failed, "Unsupported save"); Assert.Equal(prior, Progress);
        var plan = Plan(239, date + TimeSpan.TicksPerDay); store.SaveSource(binding);
        Assert.Throws<InvalidOperationException>(() => Apply(plan)); Assert.Equal(prior, Progress);
    }
    [Fact] public void UnchangedAutomaticReadStillConnectsTheCenturyObservation()
    {
        Apply(Plan(144, adapter: "json-import", bound: false)); Assert.Null(Progress);
        store.SaveAutomation(definition.Id, Account, binding.Id, TrackerAutomationMode.SaveChanges, 5, false);
        var plan = Plan(144); var settings = store.Automation(definition.Id, Account).Settings;
        Assert.True(store.RecordAutomatic(binding, settings.Id, new() { LastCheckUtc = DateTime.UtcNow.Ticks }, plan, TrackerRefreshOutcome.Unchanged, acknowledge: true));
        Assert.Equal(140, Progress!.Current); Assert.Equal(236, Progress.Target);
    }
    [Fact] public void OfficialHundredMailCounterDoesNotSupplyCenturyCompletion()
    {
        var official = DeathStrandingMailReader.Definition(); store.AddDefinition(official);
        var source = new TrackerSourceBinding {TrackerId=official.Id,Account=Account,AdapterId=DeathStrandingTrackerSource.AdapterId,AdapterVersion="1",Location=root};
        store.SaveSource(source);var attempt=store.StartRefresh(source);store.FinishAttempt(source,attempt,TrackerRefreshOutcome.AwaitingReview,"Ready");
        var snapshot=new AchievementTrackerSnapshot {TrackerId=official.Id,Provider="steam",GameId="1850570",Account=Account,ObservedUtcTicks=date,Source="Mail counter",
            Items=new(){new(){ItemId=DeathStrandingMailReader.ItemId,Current=240}}};
        Apply(store.Preview(JsonSerializer.Serialize(snapshot,AchievementTrackingCatalog.Json),official.Id,Account,source.AdapterId,"1",new string('A',64),source.Id,attempt));
        Assert.Null(Progress);Assert.Equal(240,store.Load(official.Id,Account).Single().Current);
    }
    [Fact] public void OptionalItemsNeedNoReadButRequiredMissablesStillNeedVerifiedReadsAndSettingsNeverAward()
    {
        Assert.Equal(4, definition.Items.Count(i => i.Missable && i.ExcludedFromTotal));
        var missing = definition.Items.First(i => !i.ExcludedFromTotal);
        var snapshot = new AchievementTrackerSnapshot { TrackerId = definition.Id, Provider = "steam", GameId = "1850570", Account = Account,
            ObservedUtcTicks = date, Source = "Validated mail save", Items = definition.Items.Select(i => new AchievementTrackerProgress
            { ItemId = i.Id, Current = i.ExcludedFromTotal || i.Id == missing.Id ? 0 : 1 }).ToList() };
        void Refresh()
        {
            var attempt = store.StartRefresh(binding); store.FinishAttempt(binding, attempt, TrackerRefreshOutcome.AwaitingReview, "Ready");
            Apply(store.Preview(JsonSerializer.Serialize(snapshot, AchievementTrackingCatalog.Json), definition.Id, Account,
                binding.AdapterId, "1", new string('B', 64), binding.Id, attempt));
        }
        void Rules(bool required)
        {
            var current = store.Definitions().Single(d => d.Id == definition.Id);
            store.SetItemRules(definition.Id, new(missing.Id, true, required, TrackerRepository.ItemRulesVersion(current, missing.Id)));
        }
        store.SetManualProgress(definition.Id, Account, missing.Id, 1);
        Refresh(); Assert.Equal(235, Progress!.Current); Assert.Equal(236, Progress.Target); Assert.False(Progress.Unlocked);
        Rules(true); Assert.False(Progress.Unlocked); Refresh(); Assert.False(Progress.Unlocked);
        Rules(false); Assert.Equal(236, Progress.Target); Assert.False(Progress.Unlocked);
        Refresh(); Assert.True(Progress.Unlocked); Assert.Equal(235, Progress.Target); Assert.Equal(235, Progress.Current);
        Assert.Equal(100, Progress.EarnedPoints);
    }
    public void Dispose() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
}
