using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PixelVault.Tracking;
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class CenturyCargoProgressTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "PV-Cargo-" + Guid.NewGuid().ToString("N"));
    readonly AchievementTrackingStore store;
    readonly TrackerSourceBinding binding;
    readonly long date = DateTime.UtcNow.AddDays(-2).Ticks;
    const string Account = "76561197960265729";
    public CenturyCargoProgressTests()
    {
        store = new(root);
        binding = new() { TrackerId = CenturyCargoGoal.TrackerId, Account = Account,
            AdapterId = DeathStrandingTrackerSource.AdapterId, AdapterVersion = "1", Location = root };
        store.SaveSource(binding);
    }
    CenturyProgress? Progress => store.Century("steam", "1850570", CenturyAchievementCatalog.DeathStrandingCargoId, Account);
    internal static byte[] Payload(ulong kilograms)
    {
        var data = DeathStrandingInterviewReaderTests.Payload(statistic41: true);
        int row = Enumerable.Range(0, data[24]).Select(i => 25 + i * 13)
            .Single(p => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(p)) == 49);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(row + 5), kilograms);
        return data;
    }
    [Theory]
    [InlineData(0)] [InlineData(16503)] [InlineData(49999)] [InlineData(50000)] [InlineData(50001)] [InlineData(1000000)]
    public void ReadsCumulativeWholeKilogramsAndPreservesProgressAboveTheTarget(int kilograms)
    {
        var snapshot = DeathStrandingCargoReader.Read(Payload((ulong)kilograms), Account, date, "Copied save", "Verified");
        Assert.Equal(CenturyCargoGoal.TrackerId, snapshot.TrackerId);
        Assert.Equal(kilograms, Assert.Single(snapshot.Items).Current);
        Assert.Equal(67, DeathStrandingInterviewReader.ReadCount(Payload((ulong)kilograms)));
        var definition = CenturyCargoGoal.Definition(); AchievementTrackingCatalog.Validate(definition);
        Assert.True(CenturyCargoGoal.Matches(definition)); Assert.Equal(50000, definition.Items.Single().Target);
        var result = DeathStrandingSaveWorker.Analyze(Payload((ulong)kilograms), new() { Account = Account, ObservedUtc = date }, "fingerprint", "dll");
        Assert.Equal(kilograms, Assert.Single(result.Projections[definition.Id].Snapshot.Items).Current);
    }
    [Theory]
    [InlineData("overflow")] [InlineData("type")] [InlineData("duplicate")] [InlineData("truncated")] [InlineData("ambiguous")]
    public void InvalidWeightObservationsFailWithoutInventingZero(string change)
    {
        var data = Payload(change == "overflow" ? ulong.MaxValue : 50000);
        if (change == "type") data[25 + 4] = 9;
        if (change == "duplicate") data.AsSpan(25, 4).CopyTo(data.AsSpan(38));
        if (change == "truncated") data = data[..100];
        if (change == "ambiguous") data = data.Concat(data).ToArray();
        Assert.Throws<NotSupportedException>(() => DeathStrandingCargoReader.Read(data, Account, date, "Save", "Verified"));
    }
    TrackerRefreshPlan Plan(int? kilograms, long? observed = null, bool bound = true)
    {
        var snapshot = new AchievementTrackerSnapshot { TrackerId = binding.TrackerId, Provider = "steam", GameId = "1850570", Account = Account,
            ObservedUtcTicks = observed ?? date, Source = "Cargo", Items = new() { new() { ItemId = CenturyCargoGoal.ItemId, Current = kilograms } } };
        string? attempt = null;
        if (bound) { attempt = store.StartRefresh(binding); store.FinishAttempt(binding, attempt, TrackerRefreshOutcome.AwaitingReview, "Ready"); }
        var json = JsonSerializer.Serialize(snapshot, AchievementTrackingCatalog.Json);
        return store.Preview(json, binding.TrackerId, Account, bound ? binding.AdapterId : "json-import", "1",
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))), bound ? binding.Id : null, attempt);
    }
    void Apply(TrackerRefreshPlan plan, bool older = false) => store.ApplyRefresh(plan, older, [], CancellationToken.None);
    [Fact]
    public void OnlyKnownBoundKilogramsAwardAtOrAbove50000AndTheFirstUnlockPersists()
    {
        store.SetManualProgress(binding.TrackerId, Account, CenturyCargoGoal.ItemId, 50000);
        store.SaveNote(binding.TrackerId, Account, CenturyCargoGoal.ItemId, "Keep my note");
        Apply(Plan(16503)); Assert.Equal(16503, Progress!.Current); Assert.False(Progress.Unlocked);
        Apply(Plan(49999, date + 1)); Assert.False(Progress.Unlocked); Assert.Equal(0, Progress.EarnedPoints);
        Apply(Plan(50000, date + 2)); var earned = Progress!;
        Assert.True(earned.Unlocked); Assert.Equal(50000, earned.Current); Assert.Equal(100, earned.EarnedPoints);
        Apply(Plan(50001, date + 3)); Assert.Equal(earned.UnlockUtc, Progress!.UnlockUtc);
        Assert.Equal(50001, store.Load(binding.TrackerId, Account).Single().ReportedCurrent);
        Assert.Equal("Keep my note", store.Load(binding.TrackerId, Account).Single().Note);
        Assert.Null(store.Century("steam", "1850570", CenturyAchievementCatalog.DeathStrandingCargoId, "other"));
        Apply(Plan(100, date + 4)); Assert.Equal(earned.UnlockUtc, Progress!.UnlockUtc);
        Apply(Plan(50000, date - 1), true); Assert.Equal(100, Progress.Current);
    }
    [Fact]
    public void UnknownImportFailureOrChangedBindingCannotAwardCenturyCredit()
    {
        Apply(Plan(50000, bound: false)); Assert.Null(Progress);
        Apply(Plan(16503)); var before = Progress;
        Apply(Plan(null, date + 1)); Assert.Equal(before, Progress);
        var attempt = store.StartRefresh(binding); store.FinishAttempt(binding, attempt, TrackerRefreshOutcome.Failed, "Unsupported");
        Assert.Equal(before, Progress);
        var pending = Plan(50000, date + 2); store.SaveSource(binding);
        Assert.Throws<InvalidOperationException>(() => Apply(pending)); Assert.Equal(before, Progress);
        var definition = CenturyCargoGoal.Definition(); definition.Items[0].Target = 100;
        Assert.False(new DeathStrandingTrackerSource(root).Supports(definition));
    }
    [Fact]
    public void UnchangedAutomaticCheckCanRecordCenturyWithoutChangingOtherTrackers()
    {
        Apply(Plan(16503, bound: false)); Assert.Null(Progress);
        store.SaveAutomation(binding.TrackerId, Account, binding.Id, TrackerAutomationMode.SaveChanges, 5, false);
        var settings = store.Automation(binding.TrackerId, Account).Settings;
        Assert.True(store.RecordAutomatic(binding, settings.Id, new() { LastCheckUtc = DateTime.UtcNow.Ticks }, Plan(16503), TrackerRefreshOutcome.Unchanged, acknowledge: true));
        Assert.Equal(16503, Progress!.Current);
        Assert.All(store.Load(CenturyLegendsCatalogue.TrackerId, Account), row => Assert.Null(row.Current));
        Assert.Equal(5, CenturyAchievementCatalog.ForGame("steam", "1850570").Count);
    }
    [Theory]
    [InlineData(null, 0, 0)] [InlineData(16503, 16503, 0)] [InlineData(49999, 49999, 0)] [InlineData(50000, 50000, 1)] [InlineData(75000, 50000, 1)]
    public void CargoKeepsKilogramProgressButContributesOnlyOneItemToCombinedCounts(int? kilograms, int progress, int completedItem)
    {
        var definition = CenturyCargoGoal.Definition();
        var state = new AchievementTrackerState { Item = definition.Items.Single(), ReportedCurrent = kilograms };
        var totals = TrackerTotals.Calculate(definition, new[] { state });
        Assert.Equal(50000, totals.Total); Assert.Equal(progress, totals.Completed);
        Assert.Equal(1, totals.AggregateTotal); Assert.Equal(completedItem, totals.AggregateCompleted);
        Assert.Equal(kilograms, state.ReportedCurrent);
    }
    public void Dispose() { SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
}
