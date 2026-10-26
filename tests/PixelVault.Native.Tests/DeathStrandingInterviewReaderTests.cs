using System.Buffers.Binary;
using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class DeathStrandingInterviewReaderTests
{
    const int Start = 25;
    static byte[] Payload(int value = 67, int optional = 2)
    {
        var rows = DeathStrandingInterviewReader.References().Where(r => optional == 2 || r.Key != 8 && (optional == 1 || r.Key != 7)).OrderBy(r => r.Key).ToList();
        var data = new byte[Start + rows.Count * 13 + 7];Convert.FromHexString("879ca01f131c7ece01").CopyTo(data, Start - 10);data[Start - 1] = (byte)rows.Count;
        for (int i = 0; i < rows.Count; i++)
        {
            var row = data.AsSpan(Start + i * 13, 13);BinaryPrimitives.WriteUInt32LittleEndian(row, rows[i].Key);row[4] = rows[i].Value;
            BinaryPrimitives.WriteUInt64LittleEndian(row.Slice(5), rows[i].Key == 39 ? (ulong)value : 0);
        }
        return data;
    }
    static AchievementTrackerSnapshot Read(byte[] data) => DeathStrandingInterviewReader.Read(data, "player", DateTime.UtcNow.Ticks, "Fixture", "Verified copy.");
    [Theory] [InlineData(0, 0)] [InlineData(66, 1)] [InlineData(67, 2)] [InlineData(100, 2)] [InlineData(158, 2)]
    public void ReadsCompleteCounterTablesAndUsesGenericCountGoal(int value, int optional)
    {
        var snapshot = Read(Payload(value, optional));var row = Assert.Single(snapshot.Items);Assert.Equal(value, row.Current);Assert.Equal(DeathStrandingInterviewReader.ItemId, row.ItemId);
        var d = DeathStrandingInterviewReader.Definition();AchievementTrackingCatalog.Validate(d);Assert.Equal("039", d.AchievementId);
        var state = new AchievementTrackerState { Item = d.Items.Single(), ReportedCurrent = value };var totals = TrackerTotals.Calculate(d, new[] { state });
        Assert.Equal(100, totals.Total);Assert.Equal(Math.Min(100, value), totals.Completed);Assert.Equal(value >= 100, state.Complete);Assert.Equal(value >= 100 ? 1 : 0, totals.CompletedRecords);
    }
    [Theory] [InlineData("prefix")] [InlineData("count")] [InlineData("missing")] [InlineData("duplicate")] [InlineData("type")] [InlineData("boolean")] [InlineData("range")] [InlineData("truncated")] [InlineData("ambiguous")]
    public void UnsupportedDataCannotGenerateAnIncorrectCount(string mutation)
    {
        var data = Payload();int row = Start + 3 * 13;
        switch (mutation)
        {
            case "prefix": data[Start - 10]++;break;
            case "count": data[Start - 1] = 50;break;
            case "missing": BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(row), 99);break;
            case "duplicate": data.AsSpan(Start, 4).CopyTo(data.AsSpan(row));break;
            case "type": data[row + 4] = 5;break;
            case "boolean": BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(Start + 5), 2);break;
            case "range": data = Payload(159);break;
            case "truncated": data = data[..(Start + 48 * 13)];break;
            case "ambiguous": data = data.Concat(data).ToArray();break;
        }
        Assert.Throws<NotSupportedException>(() => Read(data));
    }
    [Fact]
    public void CounterObservationPreservesManualCountNotesAndOtherAccounts()
    {
        var dir = Path.Combine(Path.GetTempPath(), "PV-Interview-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new AchievementTrackingStore(dir);var d = DeathStrandingInterviewReader.Definition();store.AddDefinition(d);
            var first = Read(Payload(66));store.Import(JsonSerializer.Serialize(first), "player", d.Id);
            store.SetManualProgress(d.Id, "player", DeathStrandingInterviewReader.ItemId, 75);store.SaveNote(d.Id, "player", DeathStrandingInterviewReader.ItemId, "Keep this note");
            var next = Read(Payload(67));next.ObservedUtcTicks = first.ObservedUtcTicks + 1;store.Import(JsonSerializer.Serialize(next), "player", d.Id);
            var state = Assert.Single(store.Load(d.Id, "player"));Assert.Equal(75, state.ManualCurrent);Assert.Equal(67, state.ReportedCurrent);Assert.Equal("Keep this note", state.Note);Assert.Null(Assert.Single(store.Load(d.Id, "other")).Current);
        }
        finally { SqliteConnection.ClearAllPools();if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
    [Fact]
    public void NativeSourceRecognizesTheCustomCounterWithoutChangingOtherDefinitions()
    {
        var d = DeathStrandingInterviewReader.Definition();var source = new DeathStrandingTrackerSource(Path.GetTempPath());Assert.True(source.Supports(d));
        var binding = new TrackerSourceBinding { AdapterId = source.Id, AdapterVersion = "1", Account = "76561197960265729", Location = Path.GetTempPath(), GameDirectory = Path.GetTempPath() };
        source.Validate(binding, d);Assert.Contains("statistic", source.Coverage(d));d.Items[0].Target = 1000;Assert.Throws<NotSupportedException>(() => source.Validate(binding, d));
        Assert.DoesNotContain(AchievementTrackingCatalog.Builtins(), x => x.Id == d.Id);
    }
    [Fact]
    public async Task PrivateReadOneInterviewControlUsesThePackagedAutomaticSource()
    {
        var manifestFile = Environment.GetEnvironmentVariable("PIXELVAULT_DS_INTERVIEW_REVIEW");if (string.IsNullOrWhiteSpace(manifestFile)) return;
        var manifest = JsonDocument.Parse(File.ReadAllText(manifestFile)).RootElement;
        foreach (var row in manifest.GetProperty("Copies").EnumerateArray()) Assert.Equal(row.GetProperty("Count").GetInt32(), Read(File.ReadAllBytes(row.GetProperty("Data").GetString()!)).Items.Single().Current);
        var account = JsonDocument.Parse(File.ReadAllText(Environment.GetEnvironmentVariable("PIXELVAULT_TRACKING_REVIEW_SEED")!)).RootElement.GetProperty("Account").GetString()!;
        var dir = Path.Combine(Path.GetTempPath(), "PV-InterviewAuto-" + Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        try
        {
            var folder = Path.Combine(dir, "saves", "manualsave0");Directory.CreateDirectory(folder);var path = Path.Combine(folder, "checkpoint.dat");
            File.Copy(manifest.GetProperty("Before").GetString()!, path);File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-2));
            var store = new AchievementTrackingStore(dir);var d = DeathStrandingInterviewReader.Definition();store.AddDefinition(d);var source = new DeathStrandingTrackerSource(Path.Combine(dir, "achievement-tracking"), Environment.GetEnvironmentVariable("PIXELVAULT_DS_INTERVIEW_WORKER"));
            var service = new TrackerRefreshService(store, new[] { source });service.Configure(d.Id, account, source.Id, "1", Path.Combine(dir, "saves"), gameDirectory: Environment.GetEnvironmentVariable("PIXELVAULT_DS_REVIEW_GAME"));
            service.Apply(await service.PrepareAsync(d.Id, account, default), false, null!);Assert.Equal(66, store.Load(d.Id, account).Single().Current);
            store.SaveNote(d.Id, account, DeathStrandingInterviewReader.ItemId, "Keep this reading note");store.SaveAutomation(d.Id, account, store.Source(d.Id, account).Id, TrackerAutomationMode.SaveChanges, 5, false);
            var now = DateTime.UtcNow;using var monitor = new TrackerAutomationMonitor(store, service, () => now);monitor.SetAccounts(account, "");
            async Task Tick() { now = now.AddSeconds(5);await monitor.TickAsync();now = now.AddSeconds(5);await monitor.TickAsync(); }
            await Tick();File.Copy(manifest.GetProperty("After").GetString()!, path, true);File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-1));await Tick();
            var state = store.Load(d.Id, account).Single();Assert.Equal(67, state.Current);Assert.Equal("Keep this reading note", state.Note);Assert.Equal(0, store.Automation(d.Id, account).State.Failures);Assert.False(store.Automation(d.Id, account).State.ReviewPending);
        }
        finally { SqliteConnection.ClearAllPools();if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
