using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class LibraryBrowserFolderFilterTests
{
    static string Norm(string s) => (s ?? string.Empty).Trim();

    [Fact]
    public void MatchesFilter_All_AlwaysTrue()
    {
        var f = new MainWindow.LibraryBrowserFolderView { PrimaryPlatformLabel = "Xbox", FileCount = 1 };
        Assert.True(MainWindow.LibraryBrowserFolderViewMatchesFilter("all", f, Norm));
    }

    [Fact]
    public void MatchesFilter_MissingId_WhenGameIndexIdBlank()
    {
        var noId = new MainWindow.LibraryBrowserFolderView { GameId = "", FileCount = 1 };
        Assert.True(MainWindow.LibraryBrowserFolderViewMatchesFilter("missingid", noId, Norm));

        var hasId = new MainWindow.LibraryBrowserFolderView { GameId = "abc-123", FileCount = 1 };
        Assert.False(MainWindow.LibraryBrowserFolderViewMatchesFilter("missingid", hasId, Norm));
    }

    [Fact]
    public void MatchesFilter_MissingId_WhenSteamTagged_AndSteamAppBlank()
    {
        var steamNoApp = new MainWindow.LibraryBrowserFolderView
        {
            PrimaryPlatformLabel = "Steam",
            SteamAppId = "",
            SteamGridDbId = "5528",
            GameId = "g",
            PlatformLabels = new[] { "Steam" }
        };
        Assert.True(MainWindow.LibraryBrowserFolderViewMatchesFilter("missingid", steamNoApp, Norm));

        var steamWithApp = new MainWindow.LibraryBrowserFolderView
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
        var steamNoGrid = new MainWindow.LibraryBrowserFolderView
        {
            PrimaryPlatformLabel = "Steam",
            SteamGridDbId = "",
            SteamAppId = "123",
            GameId = "g",
            PlatformLabels = new[] { "Steam" }
        };
        Assert.True(MainWindow.LibraryBrowserFolderViewMatchesFilter("missingid", steamNoGrid, Norm));

        var steamWithGrid = new MainWindow.LibraryBrowserFolderView
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
    public void MatchesFilter_MissingId_NonSteam_IgnoresSteamFields_WhenGameIdPresent()
    {
        var xbox = new MainWindow.LibraryBrowserFolderView
        {
            PrimaryPlatformLabel = "Xbox",
            SteamAppId = "",
            SteamGridDbId = "",
            GameId = "g1",
            PlatformLabels = new[] { "Xbox" }
        };
        Assert.False(MainWindow.LibraryBrowserFolderViewMatchesFilter("missingid", xbox, Norm));
    }

    [Fact]
    public void MatchesFilter_NoCover_UsesPreviewPathOnly()
    {
        var noPath = new MainWindow.LibraryBrowserFolderView { PreviewImagePath = "" };
        Assert.True(MainWindow.LibraryBrowserFolderViewMatchesFilter("nocover", noPath, Norm));

        var hasPath = new MainWindow.LibraryBrowserFolderView { PreviewImagePath = @"D:\a.png" };
        Assert.False(MainWindow.LibraryBrowserFolderViewMatchesFilter("nocover", hasPath, Norm));
    }
}
