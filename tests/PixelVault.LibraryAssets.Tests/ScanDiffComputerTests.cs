using PixelVault.LibraryAssets.Models;
using PixelVault.LibraryAssets.Scanning;
using Xunit;

namespace PixelVault.LibraryAssets.Tests;

public class ScanDiffComputerTests
{
    [Fact]
    public void Compute_EmptyDisk_OneActiveAsset_YieldsMissing()
    {
        var root = OperatingSystem.IsWindows() ? @"C:\lib" : "/lib";
        var id = Guid.NewGuid();
        var assets = new[]
        {
            new LibraryAssetRecord
            {
                AssetId = id,
                LibraryRoot = root,
                AbsolutePath = Path.Combine(root, "a.png"),
                ContentFingerprint = "old",
                Lifecycle = AssetLifecycle.Active,
            }
        };

        var diff = ScanDiffComputer.Compute(root, Array.Empty<DiskAssetObservation>(), assets);
        Assert.Single(diff.Entries);
        Assert.Equal(ScanDiffKind.Missing, diff.Entries[0].Kind);
        Assert.Equal(id, diff.Entries[0].AssetId);
    }

    [Fact]
    public void Compute_FileOnDisk_NoAsset_YieldsAdded()
    {
        var root = OperatingSystem.IsWindows() ? @"C:\lib" : "/lib";
        var path = Path.Combine(root, "b.png");
        var diff = ScanDiffComputer.Compute(root, new[] { new DiskAssetObservation(path, "fp1") }, Array.Empty<LibraryAssetRecord>());
        Assert.Single(diff.Entries);
        Assert.Equal(ScanDiffKind.Added, diff.Entries[0].Kind);
        Assert.Equal("fp1", diff.Entries[0].NewFingerprint);
    }

    [Fact]
    public void Compute_FingerprintChange_YieldsUpdated()
    {
        var root = OperatingSystem.IsWindows() ? @"C:\lib" : "/lib";
        var path = Path.Combine(root, "c.png");
        var id = Guid.NewGuid();
        var assets = new[]
        {
            new LibraryAssetRecord
            {
                AssetId = id,
                LibraryRoot = root,
                AbsolutePath = path,
                ContentFingerprint = "v1",
                Lifecycle = AssetLifecycle.Active,
            }
        };
        var diff = ScanDiffComputer.Compute(root, new[] { new DiskAssetObservation(path, "v2") }, assets);
        Assert.Single(diff.Entries);
        Assert.Equal(ScanDiffKind.Updated, diff.Entries[0].Kind);
        Assert.Equal("v1", diff.Entries[0].PreviousFingerprint);
        Assert.Equal("v2", diff.Entries[0].NewFingerprint);
    }

    [Fact]
    public void Compute_MissingAsset_Reappears_YieldsUpdated()
    {
        var root = OperatingSystem.IsWindows() ? @"C:\lib" : "/lib";
        var path = Path.Combine(root, "d.png");
        var id = Guid.NewGuid();
        var assets = new[]
        {
            new LibraryAssetRecord
            {
                AssetId = id,
                LibraryRoot = root,
                AbsolutePath = path,
                ContentFingerprint = "v1",
                Lifecycle = AssetLifecycle.Missing,
                MissingSinceUtc = DateTime.UtcNow.AddDays(-1),
            }
        };
        var diff = ScanDiffComputer.Compute(root, new[] { new DiskAssetObservation(path, "v2") }, assets);
        Assert.Single(diff.Entries);
        Assert.Equal(ScanDiffKind.Updated, diff.Entries[0].Kind);
    }
}
