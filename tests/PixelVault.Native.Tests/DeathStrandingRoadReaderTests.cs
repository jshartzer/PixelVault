using System.Buffers.Binary;
using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class DeathStrandingRoadReaderTests
{
    static byte[] Payload(bool restored = true, bool roadDefault = false)
    {
        var facts = DeathStrandingWorldReader.References().Select(r => (Guid.Parse(r.Id), false)).ToList();
        facts.AddRange(DeathStrandingRoadReader.References().Where(r => !roadDefault || r.Title != "UC Route 41-20").Select(r => (Guid.Parse(r.FactId), restored && r.Title == "UC Route 41-20")));
        var bytes = new byte[32 + facts.Count * 17 + 8];BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(28), (uint)facts.Count);
        for (int i = 0; i < facts.Count; i++) { facts[i].Item1.ToByteArray().CopyTo(bytes, 32 + i * 17);bytes[32 + i * 17 + 16] = facts[i].Item2 ? (byte)1 : (byte)0; }
        return bytes;
    }
    static AchievementTrackerSnapshot Read(byte[] data) => DeathStrandingRoadReader.Read(data, "fixture", DateTime.UtcNow.Ticks, "Fixture", "Validated copy.");
    static AchievementTrackerDefinition Custom()
    {
        var d = DeathStrandingRoadReader.Definition();d.Id = "custom.fixture";d.Title = "Road Building";
        foreach (var i in d.Items) { i.Id = "custom-" + i.Id;i.Name += " (Description)"; }
        d.Items.Add(new AchievementTrackerItem { Id = "bad-extra", Name = "UC Route 41-34", Archived = true });return d;
    }
    [Fact] public void ExplicitGeometryFactsSeparateRestoredRoadsFromAbsentDefaults()
    {
        var after = Read(Payload());Assert.Equal(51, after.Items.Count);Assert.Single(after.Items, i => i.Current == 1);
        var target = DeathStrandingRoadReader.References().Single(r => r.Title == "UC Route 41-20");Assert.Equal(1, after.Items.Single(i => i.ItemId == target.FactId).Current);
        Assert.All(Read(Payload(false)).Items, i => Assert.Equal(0, i.Current));Assert.All(Read(Payload(roadDefault: true)).Items, i => Assert.Equal(0, i.Current));
        Assert.DoesNotContain(after.Items, i => i.ItemId == "27cf8e4c-4e91-b744-9bc3-22611c5eef7a"); // Similar road fact is not the geometry-linked highway fact.
    }
    [Theory] [InlineData("missing")] [InlineData("duplicate")] [InlineData("extra")] [InlineData("target")] [InlineData("game")] [InlineData("milestones")] [InlineData("ambiguous-name")]
    public void OnlyCompleteUnambiguousBinaryRoadListsAreSupported(string mutation)
    {
        var d = Custom();Assert.True(DeathStrandingRoadReader.Supports(d));
        switch (mutation)
        {
            case "missing": d.Items.RemoveAt(0);break;
            case "duplicate": d.Items[0].Name = d.Items[1].Name;break;
            case "extra": d.Items[^1].Archived = false;break;
            case "target": d.Items[0].Target = 2;break;
            case "game": d.GameId = "other";break;
            case "milestones": d.Milestones.Add("done");break;
            case "ambiguous-name": d.Items[0].Name += " UC Route 41-20";break;
        }
        Assert.False(DeathStrandingRoadReader.Supports(d));
    }
    [Theory] [InlineData("value")] [InlineData("duplicate")] [InlineData("count")] [InlineData("truncated")] [InlineData("marker")] [InlineData("anchor")]
    public void MalformedTablesDoNotClearAnyCheckboxes(string mutation)
    {
        var d = Payload();
        switch (mutation)
        {
            case "value": d[48] = 2;break;
            case "duplicate": d.AsSpan(32, 16).CopyTo(d.AsSpan(49, 16));break;
            case "count": BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(28), uint.MaxValue);break;
            case "truncated": d = d[..^9];break;
            case "marker": d[^1] = 1;break;
            case "anchor": var anchor = Guid.Parse(DeathStrandingWorldReader.References().Single(r => r.Name == "ldfp_builded_post").Id).ToByteArray();d[d.AsSpan().IndexOf(anchor)] ^= 1;break;
        }
        Assert.Throws<NotSupportedException>(() => Read(d));
    }
    [Fact] public void ProjectionPreservesCustomIdentityAndLeavesArchivedUnknown()
    {
        var canonical = Read(Payload());var d = Custom();var projected = DeathStrandingRoadReader.Project(canonical, d);
        Assert.Equal(d.Id, projected.TrackerId);Assert.Equal(52, projected.Items.Count);Assert.Null(projected.Items.Single(i => i.ItemId == "bad-extra").Current);
        Assert.Equal(d.Items.Select(i => i.Id), projected.Items.Select(i => i.ItemId));Assert.Single(projected.Items, i => i.Current == 1);
        canonical.Items[1].ItemId = canonical.Items[0].ItemId;Assert.Throws<InvalidDataException>(() => DeathStrandingRoadReader.Project(canonical, d));
    }
    [Fact] public void OptionalCopiedControlVerifiesNamedRestorationAndAllRecentPayloads()
    {
        var root = Environment.GetEnvironmentVariable("PIXELVAULT_DS_ROAD_REVIEW");if (string.IsNullOrEmpty(root)) return;
        var before = Read(File.ReadAllBytes(Path.Combine(root, "manualsave4", "data.bin")));var after = Read(File.ReadAllBytes(Path.Combine(root, "manualsave5", "data.bin")));
        Assert.Equal(27, before.Items.Count(i => i.Current == 1));Assert.Equal(28, after.Items.Count(i => i.Current == 1));
        var changed = Assert.Single(after.Items.Where(i => before.Items.Single(b => b.ItemId == i.ItemId).Current != i.Current));
        Assert.Equal(DeathStrandingRoadReader.References().Single(r => r.Title == "UC Route 41-20").FactId, changed.ItemId);Assert.Equal(1, changed.Current);
        var rows = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "diagnostic.json"))).RootElement;
        foreach (var row in rows.EnumerateArray()) Assert.Equal(51, Read(File.ReadAllBytes(row.GetProperty("Data").GetString()!)).Items.Count);
    }
    [Fact] public async Task OptionalPackagedWorkerAutomaticallyReconcilesBothSavesAndPreservesNotes()
    {
        var root = Environment.GetEnvironmentVariable("PIXELVAULT_DS_ROAD_REVIEW");var worker = Environment.GetEnvironmentVariable("PIXELVAULT_DS_ROAD_WORKER");var sourceFile = Environment.GetEnvironmentVariable("PIXELVAULT_DS_ROAD_SCOPE");
        if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(worker) || string.IsNullOrEmpty(sourceFile)) return;
        var approved = JsonSerializer.Deserialize<TrackerSourceBinding>(File.ReadAllText(sourceFile), AchievementTrackingCatalog.Json)!;
        var temp = Path.Combine(Path.GetTempPath(), "PV-Road-Control-" + Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
        try
        {
            var saves = Path.Combine(temp, "saves");Directory.CreateDirectory(saves);var checkpoint = Path.Combine(saves, "checkpoint.dat");
            var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "diagnostic.json"))).RootElement;
            void Select(string slot)
            {
                File.Copy(Path.Combine(root, slot, "input.dat"), checkpoint, true);
                File.SetLastWriteTimeUtc(checkpoint, DateTime.Parse(manifest.EnumerateArray().Single(r => r.GetProperty("Slot").GetString() == slot).GetProperty("SourceUtc").GetString()!).ToUniversalTime());
            }
            Select("manualsave4");var store = new AchievementTrackingStore(temp);var d = Custom();store.AddDefinition(d);
            var target = d.Items.Single(i => i.Name.StartsWith("UC Route 41-20 (", StringComparison.Ordinal)).Id;
            var falseTarget = DeathStrandingRoadReader.Project(Read(File.ReadAllBytes(Path.Combine(root, "manualsave4", "data.bin"))), d).Items.First(i => i.Current == 0).ItemId;
            store.SetManualProgress(d.Id, approved.Account, falseTarget, 1);store.SaveNote(d.Id, approved.Account, falseTarget, "Keep this authored note");
            store.SetManualProgress(d.Id, approved.Account, "bad-extra", 1);store.SaveNote(d.Id, approved.Account, "bad-extra", "Archived note");
            var adapter = new DeathStrandingTrackerSource(Path.GetDirectoryName(store.DatabasePath), worker);
            var refresh = new TrackerRefreshService(store, new ITrackerDataSource[] { adapter });
            refresh.Configure(d.Id, approved.Account, adapter.Id, adapter.Version, saves, "Fixture", approved.GameDirectory, checkpoint, false);
            var binding = store.Source(d.Id, approved.Account);binding.ReplaceManualProgress = true;store.SaveSource(binding);
            var plan = await refresh.PrepareAsync(d.Id, approved.Account, CancellationToken.None);refresh.Apply(plan, false, Array.Empty<string>(), CancellationToken.None);
            var initial = store.Load(d.Id, approved.Account);Assert.Equal(27, initial.Count(s => !s.Item.Archived && s.Complete));Assert.Null(initial.Single(s => s.Item.Id == falseTarget).ManualCurrent);
            Assert.Equal(27, store.Century("steam", "1850570", PixelVault.Tracking.CenturyAchievementCatalog.DeathStrandingRoadsId, approved.Account).Current);
            Assert.Equal("Keep this authored note", initial.Single(s => s.Item.Id == falseTarget).Note);
            Assert.Equal(1, initial.Single(s => s.Item.Id == "bad-extra").ManualCurrent);
            store.SaveAutomation(d.Id, approved.Account, binding.Id, TrackerAutomationMode.SaveChanges, 5, false);
            var now = DateTime.UtcNow;using var monitor = new TrackerAutomationMonitor(store, refresh, () => now);
            monitor.SetAccounts(approved.Account, "");await monitor.TickAsync(CancellationToken.None);now = now.AddSeconds(5);await monitor.TickAsync(CancellationToken.None);
            Select("manualsave5");await monitor.TickAsync(CancellationToken.None);now = now.AddSeconds(5);await monitor.TickAsync(CancellationToken.None);
            var final = store.Load(d.Id, approved.Account);Assert.Equal(28, final.Count(s => !s.Item.Archived && s.Complete));Assert.True(final.Single(s => s.Item.Id == target).Complete);
            var century = store.Century("steam", "1850570", PixelVault.Tracking.CenturyAchievementCatalog.DeathStrandingRoadsId, approved.Account);
            Assert.Equal(28, century.Current);Assert.False(century.Unlocked);Assert.Equal(0, century.EarnedPoints);
            Assert.Equal("Keep this authored note", final.Single(s => s.Item.Id == falseTarget).Note);Assert.Equal("Archived note", final.Single(s => s.Item.Id == "bad-extra").Note);
            Assert.False(store.Automation(d.Id, approved.Account).State.ReviewPending);Assert.Equal(0, store.Automation(d.Id, approved.Account).State.Failures);
        }
        finally { SqliteConnection.ClearAllPools();if (Directory.Exists(temp)) Directory.Delete(temp, true); }
    }
}
