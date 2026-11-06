using System.Buffers.Binary;
using System.IO;
using Microsoft.Data.Sqlite;
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class DeathStrandingMailCatalogueReaderTests
{
    const int Start = 64, End = Start + 17 + 512 * 20;
    const string Account = "76561197997629753";
    static byte[] Payload()
    {
        var stats = DeathStrandingInterviewReaderTests.Payload(72, 2, true);
        for (int p = 25; p < stats.Length - 12; p += 13)
            if (BinaryPrimitives.ReadUInt32LittleEndian(stats.AsSpan(p, 4)) == 38) BinaryPrimitives.WriteUInt64LittleEndian(stats.AsSpan(p + 5, 8), 1);
        int directory = End + stats.Length, raw = directory + 48;
        var data = new byte[raw + 8];
        DeathStrandingMailCatalogueReader.Signature.CopyTo(data, Start);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(Start + 5), 512);
        var reference = DeathStrandingMailCatalogueReader.References();
        var last = reference.Slots.Select((m, i) => (m.Id, i)).GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.Last().i);
        int available = 0;
        for (int i = 0; i < 512; i++)
        {
            int p = Start + 9 + i * 20;
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(p + 8), (ushort)(i < 242 ? last[reference.Slots[i].Id] : ushort.MaxValue));
            // Real saves retain arbitrary padding; only the meaningful flag bits are read.
            data[p + 16] = 0xb0; data[p + 17] = 0x06; data[p + 18] = 0x16; data[p + 19] = 0x75;
            if (i >= 242) continue;
            var mail = reference.Slots[i];
            data[p + 16] |= (byte)((mail.Important ? 2 : 0) | (mail.WaitIfUnread ? 8 : 0));
            if (mail.Populated && available++ < 2)
            {
                data[p + 15] = 2;
                if (available == 1) data[p + 16] |= 4;
            }
        }
        stats.CopyTo(data, End);
        void Marker(int p, uint id, int offset)
        {
            data[p] = 0x80; BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(p + 1), id);
            data[p + 5] = 0x80; BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(p + 6), offset); data[p + 10] = 0;
        }
        Marker(directory, DeathStrandingMailCatalogueReader.StartMarker, Start);
        Marker(directory + 11, DeathStrandingMailCatalogueReader.EndMarker, End);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(raw), directory); BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(raw + 4), raw);
        return data;
    }
    static AchievementTrackerSnapshot Read(byte[] data) => DeathStrandingMailCatalogueReader.Read(data, Account, DateTime.UtcNow.Ticks, "Fixture", "Verified copy.");
    [Fact]
    public void OpenedFlagIsIndependentOfNotificationAndReceivingWhileUnmappedTitleStaysUnknown()
    {
        var data = Payload(); var snapshot = Read(data);
        Assert.Equal(240, snapshot.Items.Count); Assert.Equal(239, snapshot.Items.Count(i => i.Current.HasValue));
        Assert.Single(snapshot.Items, i => i.Current == 1);
        Assert.Single(snapshot.Items, i => i.Current == 0 && i.Availability == TrackerAvailability.Available);
        Assert.Equal(237, snapshot.Items.Count(i => i.Availability == TrackerAvailability.Locked));
        var missing = snapshot.Items.Single(i => i.ItemId == DeathStrandingMailCatalogueReader.References().UnmappedId);
        Assert.Null(missing.Current); Assert.Equal(TrackerAvailability.Unknown, missing.Availability);
        var definition = DeathStrandingMailCatalogueReader.Definition(); AchievementTrackingCatalog.Validate(definition);
        Assert.True(DeathStrandingMailCatalogueReader.Supports(definition));
        var source = new DeathStrandingTrackerSource(Path.GetTempPath()); Assert.True(source.Supports(definition));
        source.Validate(new() { AdapterId = source.Id, AdapterVersion = "1", Account = Account, Location = Path.GetTempPath(), GameDirectory = Path.GetTempPath() }, definition);
        definition.Items[0].Target = 2; Assert.False(DeathStrandingMailCatalogueReader.Supports(definition));
    }
    [Theory]
    [InlineData("signature")][InlineData("count")][InlineData("index")][InlineData("duplicate-empty")][InlineData("unused")]
    [InlineData("state")][InlineData("importance")][InlineData("wait")][InlineData("counter")][InlineData("directory")]
    [InlineData("end")][InlineData("version")][InlineData("ambiguous")][InlineData("truncated")]
    public void UnsupportedOrInconsistentTablesCannotWriteGuessedMailProgress(string mutation)
    {
        var data = Payload(); int directory = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(data.Length - 8));
        switch (mutation)
        {
            case "signature": data[Start]++; break;
            case "count": data[Start + 5]--; break;
            case "index": data[Start + 9 + 8] = 1; break;
            case "duplicate-empty": data[Start + 9 + 120 * 20 + 8] = 120; break;
            case "unused": data[Start + 9 + 242 * 20 + 8] = 0; break;
            case "state": data[Start + 9 + 15] = 3; break;
            case "importance": data[Start + 9 + 16] ^= 2; break;
            case "wait": data[Start + 9 + 16] ^= 8; break;
            case "counter": data[Start + 9 + 16] ^= 4; break;
            case "directory": BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(data.Length - 4), data.Length); break;
            case "end": data[directory + 17]--; break;
            case "version": data[directory + 10] = 1; break;
            case "ambiguous": data.AsSpan(directory, 11).CopyTo(data.AsSpan(directory + 22)); break;
            case "truncated": data = data[..(End - 1)]; break;
        }
        Assert.Throws<NotSupportedException>(() => Read(data));
    }
    [Fact]
    public void PrivateCopiedTablesMatchTheCounterAndBothNamedReadingControls()
    {
        var root = Environment.GetEnvironmentVariable("PIXELVAULT_DS_MAIL_STATES_REVIEW"); if (string.IsNullOrEmpty(root)) return;
        int checkedCopies = 0;
        foreach (var folder in Directory.EnumerateDirectories(root, "InterviewSaves-*"))
            foreach (var path in Directory.EnumerateFiles(folder, "data.bin", SearchOption.AllDirectories))
            {
                var bytes = File.ReadAllBytes(path); var snapshot = Read(bytes); checkedCopies++;
                Assert.Equal(DeathStrandingInterviewReader.ReadStatistic(bytes, 38, 240), snapshot.Items.Count(i => i.Current == 1));
            }
        Assert.True(checkedCopies >= 160);
        AchievementTrackerSnapshot Saved(string run, string slot) => Read(File.ReadAllBytes(Path.Combine(root, run, slot, "data.bin")));
        var before = Saved("InterviewSaves-20261030-002151699", "manualsave16"); var after = Saved("InterviewSaves-20261030-002255192", "manualsave17");
        const string roboticist = "c6e266d8-38ed-f74a-8152-87612dcda21b", prepper = "dd61e5af-511e-024d-9657-2d925b9b9e58", photographer = "767aa420-d1b4-d44f-97cc-c64d481e7411";
        Assert.Equal(0, before.Items.Single(i => i.ItemId == roboticist).Current); Assert.Equal(1, after.Items.Single(i => i.ItemId == roboticist).Current);
        Assert.Single(before.Items.Zip(after.Items), p => p.First.Current != p.Second.Current);
        Assert.Equal(0, after.Items.Single(i => i.ItemId == prepper).Current); Assert.Equal(TrackerAvailability.Available, after.Items.Single(i => i.ItemId == prepper).Availability);
        var oldBefore = Saved("InterviewSaves-20261029-170942990", "manualsave11"); var oldAfter = Saved("InterviewSaves-20261029-170942990", "manualsave12");
        Assert.Equal(0, oldBefore.Items.Single(i => i.ItemId == photographer).Current); Assert.Equal(1, oldAfter.Items.Single(i => i.ItemId == photographer).Current);
        Assert.Single(oldBefore.Items.Zip(oldAfter.Items), p => p.First.Current != p.Second.Current);
    }
    [Fact]
    public async Task PackagedAutomaticSourceReadsTheNamedPairAndPreservesManualValuesAndNotes()
    {
        var root = Environment.GetEnvironmentVariable("PIXELVAULT_DS_MAIL_STATES_REVIEW"); var worker = Environment.GetEnvironmentVariable("PIXELVAULT_DS_MAIL_STATES_WORKER");
        if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(worker)) return;
        var dir = Path.Combine(Path.GetTempPath(), "PV-MailStates-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        try
        {
            var saves = Path.Combine(dir, "saves"); Directory.CreateDirectory(saves); var checkpoint = Path.Combine(saves, "checkpoint.dat");
            File.Copy(Path.Combine(root, "InterviewSaves-20261030-002151699", "manualsave16", "input.dat"), checkpoint); File.SetLastWriteTimeUtc(checkpoint, DateTime.UtcNow.AddMinutes(-2));
            var store = new AchievementTrackingStore(dir); var definition = DeathStrandingMailCatalogueReader.Definition(); store.AddDefinition(definition);
            var source = new DeathStrandingTrackerSource(Path.Combine(dir, "achievement-tracking"), worker); var service = new TrackerRefreshService(store, new[] { source });
            service.Configure(definition.Id, Account, source.Id, "1", saves, gameDirectory: @"E:\SteamLibrary\steamapps\common\DEATH STRANDING DIRECTORS CUT");
            service.Apply(await service.PrepareAsync(definition.Id, Account, default), false, null!);
            Assert.Equal(139, store.Century("steam", "1850570", PixelVault.Tracking.CenturyAchievementCatalog.DeathStrandingMailsId, Account).Current);
            const string roboticist = "c6e266d8-38ed-f74a-8152-87612dcda21b", prepper = "dd61e5af-511e-024d-9657-2d925b9b9e58";
            store.SaveNote(definition.Id, Account, roboticist, "Keep authored note"); store.SetManualProgress(definition.Id, Account, prepper, 0);
            store.SaveAutomation(definition.Id, Account, store.Source(definition.Id, Account).Id, TrackerAutomationMode.SaveChanges, 5, false);
            var now = DateTime.UtcNow; using var monitor = new TrackerAutomationMonitor(store, service, () => now); monitor.SetAccounts(Account, "");
            async Task Tick() { now = now.AddSeconds(5); await monitor.TickAsync(); now = now.AddSeconds(5); await monitor.TickAsync(); }
            await Tick(); File.Copy(Path.Combine(root, "InterviewSaves-20261030-002255192", "manualsave17", "input.dat"), checkpoint, true); File.SetLastWriteTimeUtc(checkpoint, DateTime.UtcNow.AddMinutes(-1)); await Tick();
            var states = store.Load(definition.Id, Account); Assert.Equal(140, states.Count(i => i.ReportedCurrent == 1));
            var century = store.Century("steam", "1850570", PixelVault.Tracking.CenturyAchievementCatalog.DeathStrandingMailsId, Account);
            Assert.Equal(140, century.Current); Assert.Equal(240, century.Target); Assert.Equal(1, century.Unknown); Assert.False(century.Unlocked);
            Assert.Equal(1, states.Single(i => i.Item.Id == roboticist).ReportedCurrent); Assert.Equal("Keep authored note", states.Single(i => i.Item.Id == roboticist).Note);
            Assert.Equal(0, states.Single(i => i.Item.Id == prepper).ManualCurrent); Assert.Null(states.Single(i => i.Item.Id == DeathStrandingMailCatalogueReader.References().UnmappedId).ReportedCurrent);
            Assert.Equal(0, store.Automation(definition.Id, Account).State.Failures); Assert.False(store.Automation(definition.Id, Account).State.ReviewPending);
        }
        finally { SqliteConnection.ClearAllPools(); if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
