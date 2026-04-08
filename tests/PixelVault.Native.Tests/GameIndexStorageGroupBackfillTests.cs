using Xunit;

namespace PixelVaultNative.Tests;

public sealed class GameIndexStorageGroupBackfillTests
{
    [Fact]
    public void SameFoldedTitle_DifferentNonSteamPlatforms_SharesStorageGroupId()
    {
        var rows = new List<GameIndexEditorRow>
        {
            new()
            {
                GameId = "G1",
                Name = "Doom 2016",
                PlatformLabel = "Steam",
                FolderPath = string.Empty
            },
            new()
            {
                GameId = "G2",
                Name = "Doom 2016",
                PlatformLabel = "PS5",
                FolderPath = string.Empty
            }
        };

        var changed = GameIndexStorageGroupBackfill.AssignDeterministicStorageGroupIds(
            rows,
            (n, _) => (n ?? string.Empty).Trim(),
            n => (n ?? string.Empty).Trim(),
            p => (p ?? string.Empty).Trim(),
            t => (t ?? string.Empty).Trim(),
            g => (g ?? string.Empty).Trim());
        Assert.True(changed);
        Assert.False(string.IsNullOrWhiteSpace(rows[0].StorageGroupId));
        Assert.Equal(rows[0].StorageGroupId, rows[1].StorageGroupId, ignoreCase: true);
    }

    [Fact]
    public void SteamRows_DifferentAppIds_GetDistinctStorageGroupIds()
    {
        var rows = new List<GameIndexEditorRow>
        {
            new()
            {
                GameId = "G1",
                Name = "Hades",
                PlatformLabel = "Steam",
                SteamAppId = "100",
                FolderPath = string.Empty
            },
            new()
            {
                GameId = "G2",
                Name = "Hades",
                PlatformLabel = "Steam",
                SteamAppId = "200",
                FolderPath = string.Empty
            }
        };

        GameIndexStorageGroupBackfill.AssignDeterministicStorageGroupIds(
            rows,
            (n, _) => (n ?? string.Empty).Trim(),
            n => (n ?? string.Empty).Trim(),
            p => (p ?? string.Empty).Trim(),
            t => (t ?? string.Empty).Trim(),
            g => (g ?? string.Empty).Trim());
        Assert.False(string.IsNullOrWhiteSpace(rows[0].StorageGroupId));
        Assert.False(string.IsNullOrWhiteSpace(rows[1].StorageGroupId));
        Assert.False(string.Equals(rows[0].StorageGroupId, rows[1].StorageGroupId, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EmulationRows_DifferentNonSteamId_GetDistinctStorageGroupIds()
    {
        var rows = new List<GameIndexEditorRow>
        {
            new()
            {
                GameId = "G1",
                Name = "Chrono Trigger",
                PlatformLabel = "Emulation",
                NonSteamId = "A",
                FolderPath = string.Empty
            },
            new()
            {
                GameId = "G2",
                Name = "Chrono Trigger",
                PlatformLabel = "Emulation",
                NonSteamId = "B",
                FolderPath = string.Empty
            }
        };

        GameIndexStorageGroupBackfill.AssignDeterministicStorageGroupIds(
            rows,
            (n, _) => (n ?? string.Empty).Trim(),
            n => (n ?? string.Empty).Trim(),
            p => (p ?? string.Empty).Trim(),
            t => (t ?? string.Empty).Trim(),
            g => (g ?? string.Empty).Trim());
        Assert.False(string.Equals(rows[0].StorageGroupId, rows[1].StorageGroupId, StringComparison.OrdinalIgnoreCase));
    }
}
