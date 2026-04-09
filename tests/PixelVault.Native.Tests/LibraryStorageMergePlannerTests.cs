using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class LibraryStorageMergePlannerTests
{
    [Fact]
    public void PlanDryRun_SameStorageGroup_TwoFolders_ProducesMovesToCanonicalPath()
    {
        var root = Path.Combine(Path.GetTempPath(), "pv-merge-plan-" + Guid.NewGuid().ToString("N"));
        try
        {
            var canonical = Path.Combine(root, "MyGame");
            var steam = Path.Combine(root, "MyGame - Steam");
            var ps = Path.Combine(root, "MyGame - PS5");
            Directory.CreateDirectory(steam);
            Directory.CreateDirectory(ps);
            var f1 = Path.Combine(steam, "1.png");
            var f2 = Path.Combine(ps, "2.png");
            File.WriteAllText(f1, "x");
            File.WriteAllText(f2, "x");

            var rows = new List<GameIndexEditorRow>
            {
                new()
                {
                    GameId = "g1",
                    Name = "MyGame",
                    PlatformLabel = "Steam",
                    StorageGroupId = "SG1",
                    FilePaths = new[] { f1 },
                    FolderPath = steam
                },
                new()
                {
                    GameId = "g2",
                    Name = "MyGame",
                    PlatformLabel = "PS5",
                    StorageGroupId = "SG1",
                    FilePaths = new[] { f2 },
                    FolderPath = ps
                }
            };
            var titleCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["MyGame"] = 2 };

            var result = LibraryStorageMergePlanner.PlanDryRun(
                root,
                rows,
                (n, _) => (n ?? string.Empty).Trim(),
                n => string.IsNullOrWhiteSpace(n) ? "Unknown Game" : n.Trim(),
                p => (p ?? string.Empty).Trim(),
                titleCounts,
                File.Exists,
                p => p.EndsWith(".png", StringComparison.OrdinalIgnoreCase),
                dir => Directory.Exists(dir) ? Directory.EnumerateFiles(dir) : Enumerable.Empty<string>());

            Assert.Single(result.Groups);
            Assert.Equal(2, result.TotalFileMoves);
            Assert.Equal(0, result.TotalConflictRenames);
            Assert.Equal(canonical, result.Groups[0].TargetDirectory, ignoreCase: true);
            Assert.Equal(2, result.Groups[0].DirectoriesThatMayBeRemovedIfEmpty.Count);
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public void PlanDryRun_EmptyStorageGroup_Skips()
    {
        var root = Path.Combine(Path.GetTempPath(), "pv-merge-skip-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var rows = new List<GameIndexEditorRow>
            {
                new()
                {
                    GameId = "a",
                    Name = "Solo",
                    PlatformLabel = "Steam",
                    StorageGroupId = string.Empty,
                    FilePaths = Array.Empty<string>(),
                    FolderPath = string.Empty
                }
            };
            var result = LibraryStorageMergePlanner.PlanDryRun(
                root,
                rows,
                (n, _) => (n ?? string.Empty).Trim(),
                n => n,
                p => p,
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
                File.Exists,
                _ => true,
                dir => Directory.EnumerateFiles(dir));
            Assert.Empty(result.Groups);
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
        }
    }
}
