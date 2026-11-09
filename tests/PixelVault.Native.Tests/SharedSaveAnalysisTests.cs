using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class SharedSaveAnalysisTests
{
    const string Account = "76561197960265729";
    static byte[] Payload()
    {
        var keys = DeathStrandingSaveReader.CatalogueKeys();
        var data = new byte[64 + keys.Length * 8];
        for (int i = 0; i < keys.Length; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(64 + i * 8), keys[i]);
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(68 + i * 8), (uint)i << 14 | 0xa0000000u);
        }
        var rows = DeathStrandingInterviewReader.References().OrderBy(r => r.Key).ToList();
        var interviews = new byte[25 + rows.Count * 13];
        Convert.FromHexString("879ca01f131c7ece01").CopyTo(interviews, 15); interviews[24] = (byte)rows.Count;
        for (int i = 0; i < rows.Count; i++)
        {
            var row = interviews.AsSpan(25 + i * 13, 13);
            BinaryPrimitives.WriteUInt32LittleEndian(row, rows[i].Key); row[4] = rows[i].Value;
            BinaryPrimitives.WriteUInt64LittleEndian(row[5..], rows[i].Key == 39 ? 67UL : 0UL);
        }
        return data.Concat(interviews).ToArray();
    }
    [Fact]
    public void OnePayloadProjectsSupportedTablesIndependentlyAndNeverInventsCenturyProgress()
    {
        var request = new DeathStrandingReadRequest { Account = Account, ObservedUtc = DateTime.UtcNow.Ticks, SourceName = "manualsave1/checkpoint.dat" };
        var result = DeathStrandingSaveWorker.Analyze(Payload(), request, "save-hash", "dll-hash");
        DeathStrandingTrackerSource.ValidateAnalysis(result, Account, "save-hash", "dll-hash");
        Assert.Equal(14, result.Projections.Count);
        Assert.All(result.Projections[AchievementTrackingCatalog.DeathStrandingId].Snapshot.Items, item => Assert.Equal(1, item.Current));
        Assert.Equal(67, Assert.Single(result.Projections[DeathStrandingInterviewReader.TrackerId].Snapshot.Items).Current);
        Assert.Equal("UnsupportedFormat", result.Projections[AchievementTrackingCatalog.DeathStrandingOrdersId].ErrorCode);
        Assert.Null(result.Projections[AchievementTrackingCatalog.DeathStrandingOrdersId].Snapshot);
        Assert.DoesNotContain(result.Projections.Keys, id => id.StartsWith("century."));
        Assert.Equal("UnsupportedFormat", result.Projections[DeathStrandingInterviewCatalogueReader.TrackerId].ErrorCode);
        result.Projections[DeathStrandingInterviewReader.TrackerId].Snapshot.Account = "another-account";
        Assert.Throws<InvalidDataException>(() => DeathStrandingTrackerSource.ValidateAnalysis(result, Account, "save-hash", "dll-hash"));
    }
    [Fact]
    public async Task ConcurrentConsumersShareOneAnalysisAndDifferentSavesNeverAnalyzeTogether()
    {
        var cache = new SharedSaveAnalysisCache(); int calls = 0, active = 0, maximum = 0;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<string> Analyze(CancellationToken token)
        {
            calls++; maximum = Math.Max(maximum, ++active); started.TrySetResult();
            await release.Task.WaitAsync(token); active--; return "observations";
        }
        var first = cache.GetAsync("save1", Analyze, default); await started.Task;
        var second = cache.GetAsync("save1", Analyze, default);
        var third = cache.GetAsync("save2", Analyze, default);
        Assert.Equal(1, calls); release.SetResult();
        Assert.All(await Task.WhenAll(first, second, third), value => Assert.Equal("observations", value));
        Assert.Equal(2, calls); Assert.Equal(1, maximum);
    }
    [Fact]
    public async Task CancellationOfWaitingConsumerDoesNotCancelTheActiveAnalysis()
    {
        var cache = new SharedSaveAnalysisCache();
        var release = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = cache.GetAsync("save", _ => release.Task, default);
        using var cancel = new CancellationTokenSource();
        var waiting = cache.GetAsync("save", _ => throw new Exception("Must reuse"), cancel.Token);
        cancel.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        release.SetResult("valid"); Assert.Equal("valid", await active);
        Assert.Equal("valid", await cache.GetAsync("save", _ => throw new Exception("Must reuse"), default));
    }
    [Fact]
    public async Task FailedOrCancelledAnalysisIsRetriedAndNeverPoisonsCache()
    {
        var cache = new SharedSaveAnalysisCache();
        await Assert.ThrowsAsync<IOException>(() => cache.GetAsync("save", _ => throw new IOException("Busy"), default));
        using var cancel = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cache.GetAsync("save", ct =>
        { cancel.Cancel(); ct.ThrowIfCancellationRequested(); return Task.FromResult("invalid"); }, cancel.Token));
        Assert.Equal("valid", await cache.GetAsync("save", _ => Task.FromResult("valid"), default));
    }
    [Fact]
    public async Task CacheEvictsLeastRecentlyUsedAndExpiresObservations()
    {
        var now = DateTime.UtcNow; var cache = new SharedSaveAnalysisCache(2, () => now); int calls = 0;
        Task<string> Read(CancellationToken _) => Task.FromResult((++calls).ToString());
        Assert.Equal("1", await cache.GetAsync("a", Read, default));
        Assert.Equal("2", await cache.GetAsync("b", Read, default));
        Assert.Equal("1", await cache.GetAsync("a", Read, default));
        Assert.Equal("3", await cache.GetAsync("c", Read, default));
        Assert.Equal("4", await cache.GetAsync("b", Read, default));
        now = now.AddMinutes(31);
        Assert.Equal("5", await cache.GetAsync("b", Read, default));
    }
    [Fact]
    public async Task OversizedResultsAreRejectedWithoutRetainingThem()
    {
        var cache = new SharedSaveAnalysisCache();
        await Assert.ThrowsAsync<InvalidDataException>(() => cache.GetAsync("save", _ => Task.FromResult(new string('x', 4 * 1024 * 1024 + 1)), default));
        Assert.Equal("valid", await cache.GetAsync("save", _ => Task.FromResult("valid"), default));
    }
    [Theory]
    [InlineData("fingerprint")][InlineData("dll")][InlineData("missing")][InlineData("mixed")]
    public void MismatchedOrAmbiguousBatchCannotBeReused(string mutation)
    {
        var request = new DeathStrandingReadRequest { Account = Account, ObservedUtc = DateTime.UtcNow.Ticks, SourceName = "fixture" };
        var result = DeathStrandingSaveWorker.Analyze(Payload(), request, "save", "dll");
        switch (mutation)
        {
            case "fingerprint": result.Fingerprint = "other"; break;
            case "dll": result.GameDllSha256 = "other"; break;
            case "missing": result.Projections.Remove(DeathStrandingInterviewReader.TrackerId); break;
            case "mixed": result.Projections[AchievementTrackingCatalog.DeathStrandingId].ErrorCode = "InvalidSave"; break;
        }
        Assert.Throws<InvalidDataException>(() => DeathStrandingTrackerSource.ValidateAnalysis(result, Account, "save", "dll"));
    }
    [Fact]
    public void CacheIdentitySeparatesAccountsSavesGameLibrariesAndPrivateWorkspaces()
    {
        string Key(string scope = "scope", string worker = "worker", string game = "game", string account = Account, string save = "save", string dll = "dll")
            => DeathStrandingTrackerSource.AnalysisKey(Path.GetFullPath(scope), Path.GetFullPath(worker), Path.GetFullPath(game), account, save, dll);
        var original = Key();
        Assert.NotEqual(original, Key(scope: "other")); Assert.NotEqual(original, Key(worker: "other"));
        Assert.NotEqual(original, Key(game: "other")); Assert.NotEqual(original, Key(account: "76561197960265730"));
        Assert.NotEqual(original, Key(save: "changed")); Assert.NotEqual(original, Key(dll: "updated"));
    }
    [Fact]
    public async Task CopyAndChangeProbeCanCoexistWithAGameWriteHandle()
    {
        var path = Path.Combine(Path.GetTempPath(), "PV-SharedWrite-" + Guid.NewGuid().ToString("N") + ".dat");
        var bytes = new byte[1024]; File.WriteAllBytes(path, bytes);
        try
        {
            using var game = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            Assert.Equal(bytes, (await DeathStrandingTrackerSource.ReadStableAsync(path, default)).Bytes);
            Assert.NotEmpty(TrackerSourceFiles.Signature(new[] { new FileInfo(path) }, 2048, allowConcurrentWrites: true));
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public async Task RealSourceFlowSharesWorkerAcrossTrackersAndInvalidatesOnContentAndDllChanges()
    {
        var dir = Path.Combine(Path.GetTempPath(), "PV-SharedSave-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        try
        {
            var game = Path.Combine(dir, "game"); Directory.CreateDirectory(game);
            var worker = Path.Combine(dir, "worker.exe"); File.WriteAllText(worker, "fixture");
            File.WriteAllText(Path.Combine(game, "ds.exe"), "fixture");
            var dll = Path.Combine(game, "oo2core_7_win64.dll"); File.WriteAllText(dll, "dll fixture");
            var saves = Path.Combine(dir, "saves"); Directory.CreateDirectory(saves);
            var checkpoint = Path.Combine(saves, "checkpoint.dat"); File.WriteAllBytes(checkpoint, new byte[100]);
            var before = SHA256.HashData(File.ReadAllBytes(checkpoint)); var stamp = File.GetLastWriteTimeUtc(checkpoint);
            var cache = new SharedSaveAnalysisCache(); int runs = 0;
            Task<DeathStrandingReadResult> Read(string run, CancellationToken _)
            {
                runs++;
                var request = JsonSerializer.Deserialize<DeathStrandingReadRequest>(File.ReadAllText(Path.Combine(run, "request.json")))!;
                Assert.True(request.AnalyzeAll);
                return Task.FromResult(DeathStrandingSaveWorker.Analyze(Payload(), request,
                    Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(run, "input.dat")))), Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(dll)))));
            }
            var first = new DeathStrandingTrackerSource(dir, worker, cache, Read);
            var second = new DeathStrandingTrackerSource(dir, worker, cache, Read);
            TrackerSourceBinding Binding(string id) => new() { TrackerId = id, AdapterId = first.Id, AdapterVersion = "1", Account = Account,
                Location = saves, GameDirectory = game, KeepSnapshots = false };
            var fabrication = AchievementTrackingCatalog.DeathStranding(); var interview = DeathStrandingInterviewReader.Definition();
            var observation = await first.ObserveAsync(Binding(fabrication.Id), fabrication, default);
            var interviews = await second.ObserveAsync(Binding(interview.Id), interview, default);
            Assert.Equal(1, runs); Assert.Equal(observation.Fingerprint, interviews.Fingerprint);
            Assert.Equal(67, Assert.Single(JsonSerializer.Deserialize<AchievementTrackerSnapshot>(interviews.SnapshotJson)!.Items).Current);
            // Identical bytes in another slot reuse analysis, with that consumer's current metadata.
            var slot = Path.Combine(saves, "manualsave1"); Directory.CreateDirectory(slot);
            var moved = Path.Combine(slot, "checkpoint.dat"); File.Copy(checkpoint, moved); File.SetLastWriteTimeUtc(moved, stamp.AddSeconds(3));
            var rebased = await first.ObserveAsync(Binding(fabrication.Id), fabrication, default);
            var snapshot = JsonSerializer.Deserialize<AchievementTrackerSnapshot>(rebased.SnapshotJson)!;
            Assert.Equal(1, runs); Assert.Contains("manualsave1", snapshot.Source); Assert.Equal(File.GetLastWriteTimeUtc(moved).Ticks, snapshot.ObservedUtcTicks);
            var bytes = File.ReadAllBytes(moved); bytes[50] = 1; File.WriteAllBytes(moved, bytes); File.SetLastWriteTimeUtc(moved, stamp.AddSeconds(3));
            var changed = await first.ObserveAsync(Binding(fabrication.Id), fabrication, default);
            Assert.Equal(2, runs); Assert.NotEqual(observation.Fingerprint, changed.Fingerprint);
            File.WriteAllText(dll, "updated dll");
            await second.ObserveAsync(Binding(interview.Id), interview, default); Assert.Equal(3, runs);
            Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(checkpoint)));
            Assert.Empty(Directory.EnumerateFiles(Path.Combine(dir, "sources"), "input.dat", SearchOption.AllDirectories));
        }
        finally { Directory.Delete(dir, true); }
    }
}
