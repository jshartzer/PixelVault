using System.Collections.Generic;
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class LibraryBrowserCombinedMergeTests
{
    static string Norm(string s) => (s ?? string.Empty).Trim();

    static LibraryFolderInfo F(string platform, string steamAppId, string steamGrid = "")
    {
        return new LibraryFolderInfo
        {
            PlatformLabel = platform,
            SteamAppId = steamAppId,
            SteamGridDbId = steamGrid
        };
    }

    [Fact]
    public void MergeExternalIds_PrefersSteamPlatformWhenSourcesDisagree()
    {
        var folders = new List<LibraryFolderInfo>
        {
            F("Steam", "2050650", "g1"),
            F("Xbox", "0000000", "g2")
        };
        Assert.Equal("2050650", MainWindow.MergeLibraryBrowserExternalIdsForCombinedView(folders, f => f.SteamAppId, Norm));
    }

    [Fact]
    public void MergeExternalIds_SingleConsensusWithoutSteamLabeledFolder()
    {
        var folders = new List<LibraryFolderInfo>
        {
            F("Xbox", "2050650"),
            F("PlayStation 5", "2050650")
        };
        Assert.Equal("2050650", MainWindow.MergeLibraryBrowserExternalIdsForCombinedView(folders, f => f.SteamAppId, Norm));
    }

    [Fact]
    public void MergeExternalIds_EmptyWhenSteamLabeledFoldersConflict()
    {
        var folders = new List<LibraryFolderInfo>
        {
            F("Steam", "111"),
            F("Steam", "222")
        };
        Assert.Equal(string.Empty, MainWindow.MergeLibraryBrowserExternalIdsForCombinedView(folders, f => f.SteamAppId, Norm));
    }

    [Fact]
    public void MissingId_FilterFalse_OnMergedRow_WhenAllIdsPresent()
    {
        var merged = new LibraryBrowserFolderView
        {
            PrimaryPlatformLabel = "Xbox",
            SteamAppId = "2050650",
            SteamGridDbId = "5528",
            GameId = "game-row",
            PlatformLabels = new[] { "Steam", "Xbox" },
            IsMergedAcrossPlatforms = true
        };
        Assert.False(MainWindow.LibraryBrowserFolderViewMatchesFilter("missingid", merged, Norm));
    }

    [Fact]
    public void MissingId_FilterTrue_OnMergedRow_WhenGameIdStillMissing()
    {
        var merged = new LibraryBrowserFolderView
        {
            PrimaryPlatformLabel = "Xbox",
            SteamAppId = "2050650",
            SteamGridDbId = "5528",
            GameId = "",
            PlatformLabels = new[] { "Steam", "Xbox" },
            IsMergedAcrossPlatforms = true
        };
        Assert.True(MainWindow.LibraryBrowserFolderViewMatchesFilter("missingid", merged, Norm));
    }

    [Fact]
    public void MissingId_FilterTrue_OnMergedRow_WhenEmulationTagged_AndRetroAchievementsMissing()
    {
        var merged = new LibraryBrowserFolderView
        {
            PrimaryPlatformLabel = "Emulation",
            SteamAppId = "",
            SteamGridDbId = "5528",
            RetroAchievementsGameId = "",
            GameId = "game-row",
            PlatformLabels = new[] { "Emulation" },
            IsMergedAcrossPlatforms = true
        };
        Assert.True(MainWindow.LibraryBrowserFolderViewMatchesFilter("missingid", merged, Norm));
    }
}
