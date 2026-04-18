using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class LibraryBrowserFolderFilterTests
{
    static string Norm(string s) => (s ?? string.Empty).Trim();

    [Fact]
    public void MatchesFilter_All_AlwaysTrue()
    {
        var f = new LibraryBrowserFolderView { PrimaryPlatformLabel = "Xbox", FileCount = 1 };
        Assert.True(MainWindow.LibraryBrowserFolderViewMatchesFilter("all", f, Norm));
    }

    [Fact]
    public void MatchesFilter_MissingId_WhenGameIndexIdBlank()
    {
        var noId = new LibraryBrowserFolderView { GameId = "", FileCount = 1 };
        Assert.True(MainWindow.LibraryBrowserFolderViewMatchesFilter("missingid", noId, Norm));

        var hasIdNoStid = new LibraryBrowserFolderView { GameId = "abc-123", FileCount = 1 };
        Assert.True(MainWindow.LibraryBrowserFolderViewMatchesFilter("missingid", hasIdNoStid, Norm));

        var hasIdWithStid = new LibraryBrowserFolderView { GameId = "abc-123", SteamGridDbId = "5528", FileCount = 1 };
        Assert.False(MainWindow.LibraryBrowserFolderViewMatchesFilter("missingid", hasIdWithStid, Norm));
    }

    [Fact]
    public void MatchesFilter_MissingId_WhenSteamTagged_AndSteamAppBlank()
    {
        var steamNoApp = new LibraryBrowserFolderView
        {
            PrimaryPlatformLabel = "Steam",
            SteamAppId = "",
            SteamGridDbId = "5528",
            GameId = "g",
            PlatformLabels = new[] { "Steam" }
        };
        Assert.True(MainWindow.LibraryBrowserFolderViewMatchesFilter("missingid", steamNoApp, Norm));

        var steamWithApp = new LibraryBrowserFolderView
        {
            PrimaryPlatformLabel = "Steam",
            SteamAppId = "123",
            SteamGridDbId = "5528",
            GameId = "g",
            PlatformLabels = new[] { "Steam" }
        };
        Assert.False(MainWindow.LibraryBrowserFolderViewMatchesFilter("missingid", steamWithApp, Norm));
    }

    [Fact]
    public void MatchesFilter_MissingId_WhenSteamTagged_AndSteamGridBlank()
    {
        var steamNoGrid = new LibraryBrowserFolderView
        {
            PrimaryPlatformLabel = "Steam",
            SteamGridDbId = "",
            SteamAppId = "123",
            GameId = "g",
            PlatformLabels = new[] { "Steam" }
        };
        Assert.True(MainWindow.LibraryBrowserFolderViewMatchesFilter("missingid", steamNoGrid, Norm));

        var steamWithGrid = new LibraryBrowserFolderView
        {
            PrimaryPlatformLabel = "Steam",
            SteamGridDbId = "5528",
            SteamAppId = "123",
            GameId = "g",
            PlatformLabels = new[] { "Steam" }
        };
        Assert.False(MainWindow.LibraryBrowserFolderViewMatchesFilter("missingid", steamWithGrid, Norm));
    }

    [Fact]
    public void MatchesFilter_MissingId_NonSteam_RequiresStid_NotSteamAppId()
    {
        var xboxMissingStid = new LibraryBrowserFolderView
        {
            PrimaryPlatformLabel = "Xbox",
            SteamAppId = "",
            SteamGridDbId = "",
            GameId = "g1",
            PlatformLabels = new[] { "Xbox" }
        };
        Assert.True(MainWindow.LibraryBrowserFolderViewMatchesFilter("missingid", xboxMissingStid, Norm));

        var xboxComplete = new LibraryBrowserFolderView
        {
            PrimaryPlatformLabel = "Xbox",
            SteamAppId = "",
            SteamGridDbId = "999",
            GameId = "g1",
            PlatformLabels = new[] { "Xbox" }
        };
        Assert.False(MainWindow.LibraryBrowserFolderViewMatchesFilter("missingid", xboxComplete, Norm));
    }

    [Fact]
    public void MatchesFilter_MissingId_WhenEmulationTagged_AndRetroAchievementsBlank()
    {
        var emuNoRa = new LibraryBrowserFolderView
        {
            PrimaryPlatformLabel = "Emulation",
            SteamAppId = "",
            SteamGridDbId = "100",
            GameId = "gEmu",
            RetroAchievementsGameId = "",
            PlatformLabels = new[] { "Emulation" }
        };
        Assert.True(MainWindow.LibraryBrowserFolderViewMatchesFilter("missingid", emuNoRa, Norm));

        var emuWithRa = new LibraryBrowserFolderView
        {
            PrimaryPlatformLabel = "Emulation",
            SteamAppId = "",
            SteamGridDbId = "100",
            GameId = "gEmu",
            RetroAchievementsGameId = "12345",
            PlatformLabels = new[] { "Emulation" }
        };
        Assert.False(MainWindow.LibraryBrowserFolderViewMatchesFilter("missingid", emuWithRa, Norm));
    }

    [Fact]
    public void MatchesFilter_MissingNonSteamId_WhenEmulationTagged_AndShortcutIdBlank()
    {
        var emuNoShortcutId = new LibraryBrowserFolderView
        {
            PrimaryPlatformLabel = "Emulation",
            NonSteamId = "",
            SteamGridDbId = "100",
            GameId = "gEmu",
            RetroAchievementsGameId = "12345",
            PlatformLabels = new[] { "Emulation" }
        };
        Assert.True(MainWindow.LibraryBrowserFolderViewMatchesFilter("missingnonsteamid", emuNoShortcutId, Norm));

        var emuWithShortcutId = new LibraryBrowserFolderView
        {
            PrimaryPlatformLabel = "Emulation",
            NonSteamId = "16245548604121415680",
            SteamGridDbId = "100",
            GameId = "gEmu",
            RetroAchievementsGameId = "12345",
            PlatformLabels = new[] { "Emulation" }
        };
        Assert.False(MainWindow.LibraryBrowserFolderViewMatchesFilter("missingnonsteamid", emuWithShortcutId, Norm));
    }

    [Fact]
    public void MatchesFilter_NoCover_UsesPreviewPathOnly()
    {
        var noPath = new LibraryBrowserFolderView { PreviewImagePath = "" };
        Assert.True(MainWindow.LibraryBrowserFolderViewMatchesFilter("nocover", noPath, Norm));

        var hasPath = new LibraryBrowserFolderView { PreviewImagePath = @"D:\a.png" };
        Assert.False(MainWindow.LibraryBrowserFolderViewMatchesFilter("nocover", hasPath, Norm));
    }
}
