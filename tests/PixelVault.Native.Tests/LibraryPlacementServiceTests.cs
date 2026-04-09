using System.Collections.Generic;
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
}
