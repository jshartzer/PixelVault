using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class LibraryPlacementServiceTests
{
    static readonly Func<string, string, string> Norm = (n, _) => (n ?? string.Empty).Trim();
    static readonly Func<string, string> Safe = n => string.IsNullOrWhiteSpace(n) ? "Unknown Game" : n.Trim();
    static readonly Func<string, string> Plat = p => (p ?? string.Empty).Trim();

    [Fact]
    public void SharedStorageGroupId_DoesNotAppendPlatformSuffix()
    {
        var rows = new List<GameIndexEditorRow>
        {
            new()
            {
                GameId = "A",
                Name = "Doom",
                PlatformLabel = "Steam",
                StorageGroupId = "SG1",
                FolderPath = string.Empty
            },
            new()
            {
                GameId = "B",
                Name = "Doom",
                PlatformLabel = "PS5",
                StorageGroupId = "SG1",
                FolderPath = string.Empty
            }
        };
        var titleCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["Doom"] = 2 };

        var steam = LibraryPlacementService.BuildCanonicalStorageFolderName(rows[0], rows, Norm, Safe, Plat, titleCounts);
        var ps5 = LibraryPlacementService.BuildCanonicalStorageFolderName(rows[1], rows, Norm, Safe, Plat, titleCounts);

        Assert.Equal("Doom", steam);
        Assert.Equal("Doom", ps5);
    }

    [Fact]
    public void EmptyStorageGroupId_UsesLegacyPlatformSuffixWhenDuplicateTitles()
    {
        var rows = new List<GameIndexEditorRow>
        {
            new()
            {
                GameId = "A",
                Name = "Doom",
                PlatformLabel = "Steam",
                StorageGroupId = string.Empty,
                FolderPath = string.Empty
            },
            new()
            {
                GameId = "B",
                Name = "Doom",
                PlatformLabel = "PS5",
                StorageGroupId = string.Empty,
                FolderPath = string.Empty
            }
        };
        var titleCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["Doom"] = 2 };

        var steam = LibraryPlacementService.BuildCanonicalStorageFolderName(rows[0], rows, Norm, Safe, Plat, titleCounts);
        var ps5 = LibraryPlacementService.BuildCanonicalStorageFolderName(rows[1], rows, Norm, Safe, Plat, titleCounts);

        Assert.Equal("Doom - Steam", steam);
        Assert.Equal("Doom - PS5", ps5);
    }

    [Fact]
    public void StorageGroupPicksFirstGameIdWithNonEmptyName()
    {
        var rows = new List<GameIndexEditorRow>
        {
            new() { GameId = "Z", Name = string.Empty, PlatformLabel = "Steam", StorageGroupId = "G", FolderPath = string.Empty },
            new() { GameId = "A", Name = "Hades", PlatformLabel = "PS5", StorageGroupId = "G", FolderPath = string.Empty }
        };
        var titleCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        var leaf = LibraryPlacementService.BuildCanonicalStorageFolderName(rows[0], rows, Norm, Safe, Plat, titleCounts);
        Assert.Equal("Hades", leaf);
    }

    [Fact]
    public void SharedStorageGroup_UsesPreferredFolderName_WhenProvided()
    {
        var rows = new List<GameIndexEditorRow>
        {
            new() { GameId = "A", Name = "Hellblade- Senua's Sacrifice", PlatformLabel = "Steam", StorageGroupId = "SG1", FolderPath = string.Empty },
            new() { GameId = "B", Name = "Hellblade Senua's Sacrifice", PlatformLabel = "Xbox", StorageGroupId = "SG1", FolderPath = string.Empty }
        };
        var titleCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        var leaf = LibraryPlacementService.BuildCanonicalStorageFolderName(rows[1], rows, Norm, Safe, Plat, titleCounts, "Hellblade: Senua's Sacrifice");

        Assert.Equal("Hellblade: Senua's Sacrifice", leaf);
    }

    [Fact]
    public void TryResolveImportSort_MatchesSteam_ByAppId()
    {
        var rows = new List<GameIndexEditorRow>
        {
            new() { GameId = "g1", Name = "Hades", PlatformLabel = "Steam", SteamAppId = "1145360", StorageGroupId = "SG", FolderPath = string.Empty }
        };
        var parse = new FilenameParseResult { SteamAppId = "1145360", PlatformLabel = "Steam" };
        var hit = LibraryPlacementService.TryResolveGameIndexRowForImportSort(
            parse,
            rows,
            Plat,
            s => s.Trim(),
            (name, pl) => Norm(name, string.Empty) + "|" + Plat(pl));
        Assert.Same(rows[0], hit);
    }

    [Fact]
    public void TryResolveImportSort_MatchesIdentity_FromTitleHint()
    {
        var rows = new List<GameIndexEditorRow>
        {
            new() { GameId = "x", Name = "Doom", PlatformLabel = "PS5", StorageGroupId = string.Empty, FolderPath = string.Empty }
        };
        var parse = new FilenameParseResult { GameTitleHint = "Doom", PlatformLabel = "PS5" };
        string Id(string n, string pl) => Norm(n, string.Empty) + "|" + Plat(pl);
        var hit = LibraryPlacementService.TryResolveGameIndexRowForImportSort(parse, rows, Plat, s => s.Trim(), Id);
        Assert.Same(rows[0], hit);
    }

    [Fact]
    public void ResolveImportSortFolderLeaf_UsesGameIndex_WhenResolved()
    {
        var rows = new List<GameIndexEditorRow>
        {
            new() { GameId = "A", Name = "Quake", PlatformLabel = "Steam", StorageGroupId = "Z", FolderPath = string.Empty }
        };
        var titleCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["Quake"] = 2 };
        var leaf = LibraryPlacementService.ResolveImportSortFolderLeaf(
            rows[0],
            rows,
            "steam_123.png",
            Safe,
            fn => "ignored",
            Norm,
            Plat,
            titleCounts);
        Assert.Equal("Quake", leaf);
    }

    [Fact]
    public void PlanImportRootSort_BuildsTargetPath_AndFlagsIndexResolution()
    {
        var rows = new List<GameIndexEditorRow>
        {
            new() { Name = "Portal", PlatformLabel = "Steam", StorageGroupId = "S", FolderPath = string.Empty }
        };
        var dest = Path.Combine(Path.GetTempPath(), "pv-import-sort-test-dest");
        var plan = LibraryPlacementService.PlanImportRootSort(
            Path.Combine(dest, "capture.png"),
            dest,
            rows[0],
            rows,
            Safe,
            _ => "WrongTitle",
            Norm,
            Plat,
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));
        Assert.True(plan.ResolvedFromGameIndex);
        Assert.Equal(Path.Combine(dest, "Portal"), plan.TargetDirectory);
        Assert.Equal("capture.png", plan.TargetFileName);
        Assert.Equal(Path.Combine(dest, "Portal", "capture.png"), plan.TargetPath);
    }
}
