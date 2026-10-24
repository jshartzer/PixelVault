using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Data.Sqlite;
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class DeathStrandingTrackerTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "PV-DS-Reader-" + Guid.NewGuid().ToString("N"));
    const string Account = "76561197960265729";
    public DeathStrandingTrackerTests() => Directory.CreateDirectory(dir);
    public void Dispose() { SqliteConnection.ClearAllPools(); Directory.Delete(dir, true); }
    static void Put(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset, 4), value);
    static byte[] Catalogue(int fabricated = 0)
    {
        var keys = DeathStrandingSaveReader.CatalogueKeys(); var targets = AchievementTrackingCatalog.DeathStranding().Items.Take(fabricated)
            .Select(i => unchecked((uint)long.Parse(i.Identifiers["CatalogueNameCode"]))).ToHashSet();
        var data = new byte[408 * 8 + 128];
        for (int i = 0; i < keys.Length; i++) { Put(data, 64 + i * 8, keys[i]); Put(data, 68 + i * 8, (uint)i << 14 | 0x20000000u | (targets.Contains(keys[i]) ? 0x80000000u : 0)); }
        return data;
    }
    static byte[] Payload(byte[] data)
    {
        var payload = new byte[(data.Length + 512 + DeathStrandingSaveReader.Block - 1) / DeathStrandingSaveReader.Block * DeathStrandingSaveReader.Block];
        Put(payload, 0x1fc, (uint)data.Length); data.CopyTo(payload, 512); MD5.HashData(data).CopyTo(payload, 0x1ec); return payload;
    }
    static byte[] Container(string account, bool encrypted = true)
    {
        var id = Encoding.ASCII.GetBytes("steam_" + account); var raw = new byte[12 + id.Length + 127];
        Put(raw, 0, DeathStrandingSaveReader.Magic); Put(raw, 4, 0x01020304); Put(raw, 8, (uint)id.Length); id.CopyTo(raw, 12);
        for (int i = 12 + id.Length; i < raw.Length; i++) raw[i] = (byte)i;
        if (encrypted) DeathStrandingSaveReader.Transform(raw.AsSpan(8), raw.AsSpan(4, 4));
        var result = new byte[24 + raw.Length]; Encoding.ASCII.GetBytes("0").CopyTo(result, 0); Encoding.ASCII.GetBytes("0").CopyTo(result, 8);
        Encoding.ASCII.GetBytes(raw.Length.ToString()).CopyTo(result, 16); raw.CopyTo(result, 24); return result;
    }

    [Fact]
    public void EncryptedAndPlaintextContainersAgreeWithoutMutatingInput()
    {
        var encrypted = Container(Account); var original = encrypted.ToArray();
        Assert.Equal(DeathStrandingSaveReader.DecryptContainer(Container(Account, false), Account), DeathStrandingSaveReader.DecryptContainer(encrypted, Account));
        Assert.Equal(original, encrypted);
    }
    [Fact]
    public void WrongAccountIsRejectedBeforeDecompression()
    {
        var error = Assert.Throws<DeathStrandingReaderException>(() => DeathStrandingSaveReader.DecryptContainer(Container(Account), "76561197960265730"));
        Assert.Equal("WrongAccount", error.Code);
    }
    [Theory]
    [InlineData("short")][InlineData("magic")][InlineData("length")][InlineData("header")]
    public void MalformedContainerIsRejected(string kind)
    {
        var bytes = Container(Account);
        if (kind == "short") bytes = bytes.Take(12).ToArray();
        if (kind == "magic") Put(bytes, 24, 0);
        if (kind == "length") bytes[16] = (byte)'9';
        if (kind == "header") bytes[0] = (byte)'x';
        Assert.Throws<InvalidDataException>(() => DeathStrandingSaveReader.DecryptContainer(bytes, Account));
    }
    [Fact]
    public void PayloadChecksumAndBoundsAreValidated()
    {
        var data = Catalogue(); var payload = Payload(data);
        Assert.Equal(data, DeathStrandingSaveReader.DecodePayload(new byte[1], (_, size) => payload.Take(size).ToArray()));
        payload[512] ^= 1;
        Assert.Throws<InvalidDataException>(() => DeathStrandingSaveReader.DecodePayload(new byte[1], (_, size) => payload.Take(size).ToArray()));
        Put(payload, 0x1fc, uint.MaxValue);
        Assert.Throws<InvalidDataException>(() => DeathStrandingSaveReader.DecodePayload(new byte[1], (_, size) => payload.Take(size).ToArray()));
    }
    [Fact]
    public void AllTargetsCanChangeIncludingOriginalResearchExamples()
    {
        var empty = DeathStrandingSaveReader.ReadCatalogue(Catalogue(), Account, DateTime.UtcNow.Ticks, "Fixture", "Test");
        var full = DeathStrandingSaveReader.ReadCatalogue(Catalogue(108), Account, DateTime.UtcNow.Ticks, "Fixture", "Test");
        var one = DeathStrandingSaveReader.ReadCatalogue(Catalogue(1), Account, DateTime.UtcNow.Ticks, "Fixture", "Test");
        Assert.Equal(108, full.Items.Count); Assert.All(empty.Items, i => Assert.Equal(0, i.Current)); Assert.All(full.Items, i => Assert.Equal(1, i.Current));
        Assert.Single(one.Items, i => i.Current == 1);
    }
    [Theory]
    [InlineData("key")][InlineData("index")][InlineData("duplicate")][InlineData("ambiguous")]
    public void UnknownOrAmbiguousCatalogueLayoutsAreRejected(string kind)
    {
        var data = Catalogue();
        if (kind == "key") Put(data, 64, 0);
        if (kind == "index") Put(data, 68 + 8, 0x20000000);
        if (kind == "duplicate") Put(data, 64 + 8, BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(64, 4)));
        if (kind == "ambiguous") data = data.Concat(data).ToArray();
        Assert.Throws<NotSupportedException>(() => DeathStrandingSaveReader.ReadCatalogue(data, Account, DateTime.UtcNow.Ticks, "Fixture", "Test"));
    }
    [Fact]
    public void CandidateSelectionIsOrderedBoundedAndSpecificSaveCannotEscapeProfile()
    {
        string Save(string slot, DateTime stamp) { var folder = Path.Combine(dir, slot); Directory.CreateDirectory(folder); var path = Path.Combine(folder, "checkpoint.dat"); File.WriteAllBytes(path, Container(Account)); File.SetLastWriteTimeUtc(path, stamp); return path; }
        var old = Save("manualsave1", DateTime.UtcNow.AddMinutes(-2)); var latest = Save("autosave2", DateTime.UtcNow.AddMinutes(-1)); Save("other-account", DateTime.UtcNow);
        Assert.Equal(latest, DeathStrandingTrackerSource.Candidates(dir, null!).First().FullName);
        Assert.Equal(old, DeathStrandingTrackerSource.Candidates(dir, old).Single().FullName);
        Assert.Throws<InvalidDataException>(() => DeathStrandingTrackerSource.Candidates(Path.GetDirectoryName(old)!, latest));
        Assert.Equal("1", DeathStrandingTrackerSource.ProfileNumber(Account));
    }
    [Fact]
    public async Task StableCopyPreservesBytesAndHonoursCancellation()
    {
        var path = Path.Combine(dir, "checkpoint.dat"); var bytes = Container(Account); File.WriteAllBytes(path, bytes);
        Assert.Equal(bytes, (await DeathStrandingTrackerSource.ReadStableAsync(path, default)).Bytes); Assert.Equal(bytes, File.ReadAllBytes(path));
        using var token = new CancellationTokenSource(); token.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DeathStrandingTrackerSource.ReadStableAsync(path, token.Token));
    }
    [Fact]
    public async Task ACheckpointTemporarilyLockedByTheGameIsRetried()
    {
        var path = Path.Combine(dir, "checkpoint.dat"); var bytes = Container(Account); File.WriteAllBytes(path, bytes);
        var gameWrite = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var read = DeathStrandingTrackerSource.ReadStableAsync(path, default);
        Assert.False(read.IsCompleted); gameWrite.Dispose();
        Assert.Equal(bytes, (await read).Bytes);
    }
    [Fact]
    public void UnfabricatedLockedBlueprintsRemainDifferentFromAvailableOnes()
    {
        var data = Catalogue(); var definition = AchievementTrackingCatalog.DeathStranding();
        uint target = unchecked((uint)long.Parse(definition.Items[0].Identifiers["CatalogueNameCode"]));
        var index = Array.IndexOf(DeathStrandingSaveReader.CatalogueKeys(), target); Put(data, 68 + index * 8, (uint)index << 14);
        var snapshot = DeathStrandingSaveReader.ReadCatalogue(data, Account, DateTime.UtcNow.Ticks, "Fixture", "Test");
        Assert.Equal(TrackerAvailability.Locked, snapshot.Items[0].Availability); Assert.Equal(0, snapshot.Items[0].Current);
        Assert.Equal(TrackerAvailability.Available, snapshot.Items[1].Availability);
    }
    [Fact]
    public void SnapshotRetentionDeletesOnlyKnownFilesInManagedRunDirectories()
    {
        var root = Path.Combine(dir, "runs"); Directory.CreateDirectory(root);
        for (int i = 0; i < 5; i++)
        {
            var run = Path.Combine(root, "run-20260101T000000000000" + i + "-" + new string((char)('a' + i), 32)); Directory.CreateDirectory(run);
            File.WriteAllText(Path.Combine(run, ".lock"), ""); File.WriteAllText(Path.Combine(run, "input.dat"), "private fixture");
        }
        var unrelated = Path.Combine(root, "user-files"); Directory.CreateDirectory(unrelated); File.WriteAllText(Path.Combine(unrelated, "input.dat"), "Keep");
        DeathStrandingTrackerSource.Prune(root, 3);
        Assert.Equal(4, Directory.GetDirectories(root).Length); Assert.Equal("Keep", File.ReadAllText(Path.Combine(unrelated, "input.dat")));
        DeathStrandingTrackerSource.Prune(root, 0); Assert.Single(Directory.GetDirectories(root));
    }
    static Process Sleeper()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
        var start = new ProcessStartInfo(path) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Seconds 20" }) start.ArgumentList.Add(arg);
        return Process.Start(start)!;
    }
    [Fact]
    public async Task WorkerTimeoutKillsOnlyTheStartedChild()
    {
        using var process = Sleeper();
        await Assert.ThrowsAsync<TimeoutException>(() => DeathStrandingTrackerSource.WaitWorkerAsync(process, TimeSpan.FromMilliseconds(200), default));
        Assert.True(process.HasExited);
    }
    [Fact]
    public async Task WorkerCancellationKillsTheStartedChild()
    {
        using var process = Sleeper(); using var token = new CancellationTokenSource(200);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DeathStrandingTrackerSource.WaitWorkerAsync(process, TimeSpan.FromSeconds(10), token.Token));
        Assert.True(process.HasExited);
    }
    [Fact]
    public void SourceSetupOffersTheGameReaderOnlyForTheSupportedTracker()
    {
        AchievementCollectionAccessTests.OnSta(() =>
        {
            var window = new TrackerSourceWindow(null!, null!, null!, AchievementTrackingCatalog.DeathStranding(), Account);
            try
            {
                var root = (FrameworkElement)window.Content; root.Measure(new Size(656, 710)); root.Arrange(new Rect(0, 0, 656, 710)); root.UpdateLayout();
                var picker = Descendants(root).OfType<ComboBox>().Single(c => AutomationProperties.GetName(c) == "Tracker source type");
                Assert.Equal(3, picker.Items.Count); Assert.Equal(DeathStrandingTrackerSource.AdapterId, ((ComboBoxItem)picker.SelectedItem).Tag);
                var output = Environment.GetEnvironmentVariable("PIXELVAULT_TRACKING_REVIEW_OUTPUT");
                if (!string.IsNullOrWhiteSpace(output))
                {
                    Directory.CreateDirectory(output); var bitmap = new RenderTargetBitmap(656, 710, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
                    var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using var file = File.Create(Path.Combine(output, "death-stranding-source.png")); png.Save(file);
                }
            }
            finally { window.Close(); }
        });
    }
    static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root; for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    [Fact]
    public async Task OptionalLocalReview_ReadsRealCopiedSavesThroughTheWorkerAndRefreshPipeline()
    {
        var statePath = Environment.GetEnvironmentVariable("PIXELVAULT_DS_REVIEW_STATE");
        var game = Environment.GetEnvironmentVariable("PIXELVAULT_DS_REVIEW_GAME");
        if (string.IsNullOrWhiteSpace(statePath) || string.IsNullOrWhiteSpace(game)) return;
        using var state = JsonDocument.Parse(File.ReadAllText(statePath));
        var original = state.RootElement.GetProperty("SourceSave").GetString()!; var decoded = state.RootElement.GetProperty("DecodedSave").GetString()!;
        var decodedBytes = File.ReadAllBytes(decoded); int offset = 24 + int.Parse(Encoding.ASCII.GetString(decodedBytes, 0, 8).TrimEnd('\0')) + int.Parse(Encoding.ASCII.GetString(decodedBytes, 8, 8).TrimEnd('\0'));
        var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(decodedBytes.AsSpan(offset + 8, 4));
        var account = Encoding.ASCII.GetString(decodedBytes, offset + 12, length).Substring(6);
        var hash = SHA256.HashData(File.ReadAllBytes(original));
        Assert.Equal(DeathStrandingSaveReader.DecryptContainer(decodedBytes, account), DeathStrandingSaveReader.DecryptContainer(File.ReadAllBytes(original), account));
        var profile = Path.Combine(dir, "relocated saves"); var slot = Path.Combine(profile, "manualsave1"); Directory.CreateDirectory(slot);
        var copied = Path.Combine(slot, "checkpoint.dat"); File.Copy(original, copied); File.SetLastWriteTimeUtc(copied, File.GetLastWriteTimeUtc(original));
        var source = new DeathStrandingTrackerSource(Path.Combine(dir, "achievement-tracking"));
        var store = new AchievementTrackingStore(dir); var service = new TrackerRefreshService(store, new[] { source });
        service.Configure(AchievementTrackingCatalog.DeathStrandingId, account, source.Id, "1", profile, "Relocated test profile", game);
        var plan = await service.PrepareAsync(AchievementTrackingCatalog.DeathStrandingId, account, default);
        Assert.Equal(108, plan.Rows.Count); Assert.Equal(35, plan.Rows.Count(r => r.Incoming == 1)); service.Apply(plan, false, null!);
        var count = store.History(plan.TrackerId, account).Count;
        File.SetLastWriteTimeUtc(copied, DateTime.UtcNow.AddMinutes(-1));
        Assert.Equal(0, service.Apply(await service.PrepareAsync(plan.TrackerId, account, default), false, null!)); Assert.Equal(count, store.History(plan.TrackerId, account).Count);
        // A more recent corrupt checkpoint is skipped visibly, while a different account must never be skipped silently.
        var badSlot = Path.Combine(profile, "autosave9"); Directory.CreateDirectory(badSlot); var bad = Path.Combine(badSlot, "checkpoint.dat"); File.WriteAllBytes(bad, new byte[24]);
        var fallback = await service.PrepareAsync(plan.TrackerId, account, default); Assert.Contains("skipped 1", fallback.Source); service.Cancel(fallback);
        File.WriteAllBytes(bad, Container("76561197960265730"));
        await Assert.ThrowsAsync<DeathStrandingReaderException>(() => service.PrepareAsync(plan.TrackerId, account, default));
        Assert.Equal(35, store.Load(plan.TrackerId, account).Count(s => s.Complete)); Assert.Equal(hash, SHA256.HashData(File.ReadAllBytes(original)));
        File.Delete(bad); var automaticTime = DateTime.UtcNow;
        store.SaveAutomation(plan.TrackerId, account, store.Source(plan.TrackerId, account).Id, TrackerAutomationMode.SaveChanges, 5, false);
        using (var monitor = new TrackerAutomationMonitor(store, service, () => automaticTime))
        {
            monitor.SetAccounts(account, ""); await monitor.TickAsync(); automaticTime = automaticTime.AddSeconds(5); await monitor.TickAsync();
            Assert.Equal(TrackerRefreshOutcome.Unchanged, store.SourceStatus(plan.TrackerId, account).Outcome);
            Assert.Contains("no progress changes", store.Automation(plan.TrackerId, account).State.Message);
            Assert.Equal(count, store.History(plan.TrackerId, account).Count); Assert.Equal(hash, SHA256.HashData(File.ReadAllBytes(original)));
        }
        Assert.True(Directory.EnumerateDirectories(Path.Combine(dir, "achievement-tracking", "sources", "death-stranding-dc"), "run-*", SearchOption.AllDirectories).Count() <= 3);
    }
}
