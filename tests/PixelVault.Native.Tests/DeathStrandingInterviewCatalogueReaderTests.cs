using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class DeathStrandingInterviewCatalogueReaderTests
{
    const string Account = "76561197997629753";
    static byte[] Payload()
    {
        var rows = DeathStrandingInterviewCatalogueReader.References();
        var data = new byte[DeathStrandingInterviewCatalogueReader.Offset + 158];
        DeathStrandingInterviewCatalogueReader.Prefix.CopyTo(data, 0);
        var active = rows.Select((r, i) => (r, i)).Where(x => x.r.Populated).Take(2).ToArray();
        data[DeathStrandingInterviewCatalogueReader.Offset + active[0].i] = 0x0d;
        data[DeathStrandingInterviewCatalogueReader.Offset + active[1].i] = 0x0b;
        return data.Concat(DeathStrandingInterviewReaderTests.Payload(1, 2, true)).ToArray();
    }
    static AchievementTrackerSnapshot Read(byte[] data) => DeathStrandingInterviewCatalogueReader.Read(data, Account, DateTime.UtcNow.Ticks, "Fixture", "Verified copy.");

    [Theory]
    [InlineData(80)][InlineData(90)][InlineData(100)][InlineData(110)][InlineData(120)]
    [InlineData(130)][InlineData(140)][InlineData(150)][InlineData(160)]
    public void CompactHeaderWidthSelectsTheExactOrderedTable(int progress)
    {
        var data = Payload();
        if (progress < 128) data[5] = (byte)progress;
        else data = data[..5].Concat(new byte[] { 0x81, (byte)progress, 0 }).Concat(data[6..]).ToArray();
        Assert.Equal(progress < 128 ? 0x260 : 0x262, DeathStrandingInterviewCatalogueReader.StateOffset(data));
        Assert.Single(Read(data).Items, i => i.Current == 1);
        data[progress < 128 ? 6 : 8] ^= 1;
        Assert.Throws<NotSupportedException>(() => Read(data));
    }

    [Theory]
    [InlineData(99,99,true)][InlineData(100,100,true)][InlineData(103,100,true)][InlineData(108,100,true)]
    [InlineData(99,100,false)][InlineData(108,99,false)][InlineData(108,101,false)]
    public void StoppedOfficialCounterDoesNotLosePostAchievementReadStates(int read, int counter, bool valid)
    {
        var rows = DeathStrandingInterviewCatalogueReader.References();
        var data = new byte[DeathStrandingInterviewCatalogueReader.Offset + 158];
        DeathStrandingInterviewCatalogueReader.Prefix.CopyTo(data, 0);
        foreach (var index in rows.Select((r,i)=>(r,i)).Where(x=>x.r.Populated).Take(read).Select(x=>x.i)) data[0x260 + index]=0x0d;
        data = data.Concat(DeathStrandingInterviewReaderTests.Payload(counter,2,true,true)).ToArray();
        if(valid) Assert.Equal(read,Read(data).Items.Count(i=>i.Current==1));
        else Assert.Throws<NotSupportedException>(()=>Read(data));
    }

    [Fact]
    public void PrivateLateGameCopiesValidateAllFiveAffectedReaders()
    {
        var root=Environment.GetEnvironmentVariable("PIXELVAULT_DS_LATE_GAME_REVIEW");if(string.IsNullOrEmpty(root))return;
        int copies=0;var variants=new HashSet<int>();bool postAchievement=false;
        foreach(var path in Directory.EnumerateFiles(root,"input.dat.payload",SearchOption.AllDirectories))
        {
            var data=File.ReadAllBytes(path);int offset=DeathStrandingInterviewCatalogueReader.StateOffset(data);
            Assert.True(offset is 0x260 or 0x262);variants.Add(offset);
            var catalogue=Read(data);int count=catalogue.Items.Count(i=>i.Current==1);
            Assert.Equal(Math.Min(count,100),DeathStrandingInterviewReader.ReadCount(data));postAchievement|=count>100;
            var mails=DeathStrandingMailCatalogueReader.Read(data,Account,DateTime.UtcNow.Ticks,"Private copy","");
            var mailCounter=DeathStrandingMailReader.Read(data,Account,DateTime.UtcNow.Ticks,"Private copy","");
            Assert.Equal(mailCounter.Items.Single().Current,mails.Items.Count(i=>i.Current==1));
            Assert.InRange(DeathStrandingCargoReader.Read(data,Account,DateTime.UtcNow.Ticks,"Private copy","").Items.Single().Current!.Value,1,1000000);
            copies++;
        }
        Assert.True(copies>=69);Assert.Equal(2,variants.Count);Assert.True(postAchievement);
    }

    [Fact]
    public void OnlyReadCountsForCenturyAndOfficialWhileUnreadRemainsAvailable()
    {
        var data = Payload();var snapshot = Read(data);var definition = DeathStrandingInterviewCatalogueReader.Definition();
        Assert.Equal(149, snapshot.Items.Count);Assert.Equal(1, snapshot.Items.Count(i => i.Current == 1));
        Assert.Equal(2, snapshot.Items.Count(i => i.Availability == TrackerAvailability.Available));
        Assert.Equal(147, snapshot.Items.Count(i => i.Current == 0 && i.Availability == TrackerAvailability.Locked));
        Assert.Single(snapshot.Items.Where(i => i.Current == 0 && i.Availability == TrackerAvailability.Available));
        Assert.Equal(1, DeathStrandingInterviewReader.ReadCount(data));
        Assert.True(DeathStrandingInterviewCatalogueReader.Supports(definition));AchievementTrackingCatalog.Validate(definition);
        var source = new DeathStrandingTrackerSource(Path.GetTempPath());Assert.True(source.Supports(definition));
        source.Validate(new() { AdapterId = source.Id, AdapterVersion = "1", Account = Account, Location = Path.GetTempPath(), GameDirectory = Path.GetTempPath() }, definition);
        definition.Items[0].Target = 2;Assert.False(source.Supports(definition));
    }
    [Theory]
    [InlineData("prefix")][InlineData("version")][InlineData("shift")][InlineData("state")][InlineData("empty")][InlineData("count")][InlineData("truncated")][InlineData("ambiguous")]
    public void UnsupportedOrInconsistentStatesNeverProduceObservations(string mutation)
    {
        var data = Payload();var rows = DeathStrandingInterviewCatalogueReader.References();
        switch (mutation)
        {
            case "prefix": data[0]++;break;
            case "version": data[5] = 0x51;break;
            case "shift": data = new byte[1].Concat(data).ToArray();break;
            case "state": data[DeathStrandingInterviewCatalogueReader.Offset] = 0x08;break;
            case "empty": data[DeathStrandingInterviewCatalogueReader.Offset + rows.FindIndex(r => !r.Populated)] = 0x0b;break;
            case "count": data[DeathStrandingInterviewCatalogueReader.Offset + rows.FindIndex(r => r.Populated)] = 0x0b;break;
            case "truncated": data = data[..(DeathStrandingInterviewCatalogueReader.Offset + 157)];break;
            case "ambiguous": data = data.Concat(DeathStrandingInterviewReaderTests.Payload(1, 2, true)).ToArray();break;
        }
        Assert.Throws<NotSupportedException>(() => Read(data));
    }
    [Fact]
    public void PrivateCopiedStatesMatchAllCountersAndTheNamedReportControl()
    {
        var root = Environment.GetEnvironmentVariable("PIXELVAULT_DS_INTERVIEW_STATES_REVIEW");if (string.IsNullOrEmpty(root)) return;
        int checkedCopies = 0;
        foreach (var path in Directory.EnumerateDirectories(root, "InterviewSaves-*"))
            foreach (var data in Directory.EnumerateFiles(path, "data.bin", SearchOption.AllDirectories))
            {
                var bytes = File.ReadAllBytes(data);var snapshot = Read(bytes);checkedCopies++;
                Assert.Equal(149, snapshot.Items.Count);
                Assert.Equal(DeathStrandingInterviewReader.ReadCount(bytes), snapshot.Items.Count(i => i.Current == 1));
            }
        Assert.True(checkedCopies >= 80);
        var before = File.ReadAllBytes(Path.Combine(root, "InterviewSaves-20261029-131223970", "manualsave8", "data.bin"));
        var after = File.ReadAllBytes(Path.Combine(root, "InterviewSaves-20261029-131345370", "manualsave9", "data.bin"));
        Assert.Equal(68, DeathStrandingInterviewReader.ReadCount(before));Assert.Equal(69, DeathStrandingInterviewReader.ReadCount(after));
        Assert.Equal(0x0b, before[0x2ef]);Assert.Equal(0x0d, after[0x2ef]);
        Assert.Equal(1, Read(after).Items.Single(i => i.ItemId == "6e0e9f8f-0b77-a043-b34e-40bab248b390").Current);
        Assert.Equal(0, Read(after).Items.Single(i => i.ItemId == "3a92e339-4909-4e4a-be19-e535d9130de4").Current);
    }
    [Fact]
    public async Task PackagedWorkersRefreshBothTrackersAfterSaveChangesWithoutAlteringNotesLinksOrManualValues()
    {
        var root = Environment.GetEnvironmentVariable("PIXELVAULT_DS_INTERVIEW_STATES_REVIEW");
        var worker = Environment.GetEnvironmentVariable("PIXELVAULT_DS_INTERVIEW_STATES_WORKER");
        if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(worker)) return;
        var dir = Path.Combine(Path.GetTempPath(), "PV-InterviewStates-" + Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        try
        {
            var saves = Path.Combine(dir, "saves");Directory.CreateDirectory(saves);var checkpoint = Path.Combine(saves, "checkpoint.dat");
            File.Copy(Path.Combine(root, "InterviewSaves-20261029-131223970", "manualsave8", "input.dat"), checkpoint);
            File.SetLastWriteTimeUtc(checkpoint, DateTime.UtcNow.AddMinutes(-2));
            var store = new AchievementTrackingStore(dir);var count = DeathStrandingInterviewReader.Definition();var catalogue = DeathStrandingInterviewCatalogueReader.Definition();
            store.AddDefinition(count);store.AddDefinition(catalogue);
            store.SetAchievementLink(catalogue.Id, PixelVault.Tracking.CenturyAchievementCatalog.DeathStrandingInterviewsId);
            var source = new DeathStrandingTrackerSource(Path.Combine(dir, "achievement-tracking"), worker);
            var service = new TrackerRefreshService(store, new[] { source });
            foreach (var definition in new[] { count, catalogue })
            {
                service.Configure(definition.Id, Account, source.Id, "1", saves, gameDirectory: @"E:\SteamLibrary\steamapps\common\DEATH STRANDING DIRECTORS CUT");
                service.Apply(await service.PrepareAsync(definition.Id, Account, default), false, null!);
                store.SaveNote(definition.Id, Account, definition.Items[0].Id, "Keep reading note");
                store.SaveAutomation(definition.Id, Account, store.Source(definition.Id, Account).Id, TrackerAutomationMode.SaveChanges, 5, false);
            }
            int manual = store.Load(catalogue.Id, Account).Single(i => i.Item.Id == catalogue.Items[0].Id).ReportedCurrent!.Value;
            store.SetManualProgress(catalogue.Id, Account, catalogue.Items[0].Id, manual);
            var now = DateTime.UtcNow;using var monitor = new TrackerAutomationMonitor(store, service, () => now);monitor.SetAccounts(Account, "");
            async Task Tick() { now = now.AddSeconds(5);await monitor.TickAsync();now = now.AddSeconds(5);await monitor.TickAsync(); }
            await Tick();
            File.Copy(Path.Combine(root, "InterviewSaves-20261029-131345370", "manualsave9", "input.dat"), checkpoint, true);
            File.SetLastWriteTimeUtc(checkpoint, DateTime.UtcNow.AddMinutes(-1));await Tick();
            Assert.Equal(69, store.Load(count.Id, Account).Single().Current);
            Assert.Equal(69, store.Load(catalogue.Id, Account).Count(i => i.ReportedCurrent == 1));
            Assert.Equal(manual, store.Load(catalogue.Id, Account).Single(i => i.Item.Id == catalogue.Items[0].Id).ManualCurrent);
            foreach (var definition in new[] { count, catalogue })
            {
                Assert.Equal("Keep reading note", store.Load(definition.Id, Account).Single(i => i.Item.Id == definition.Items[0].Id).Note);
                Assert.Equal(0, store.Automation(definition.Id, Account).State.Failures);
                Assert.False(store.Automation(definition.Id, Account).State.ReviewPending);
            }
            Assert.Equal("039", store.Definitions().Single(i => i.Id == count.Id).AchievementId);
            Assert.Equal(PixelVault.Tracking.CenturyAchievementCatalog.DeathStrandingInterviewsId, store.Definitions().Single(i => i.Id == catalogue.Id).AchievementId);
        }
        finally { SqliteConnection.ClearAllPools();if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
