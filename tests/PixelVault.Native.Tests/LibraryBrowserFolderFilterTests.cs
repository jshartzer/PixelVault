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
    public void MatchesFilter_NeedsSteam_RequiresSteamPlatformAndBlankAppId()
    {
        var steamNoId = new MainWindow.LibraryBrowserFolderView
        {
            PrimaryPlatformLabel = "Steam",
            SteamAppId = "",
            PlatformLabels = new[] { "Steam" }
        };
        Assert.True(MainWindow.LibraryBrowserFolderViewMatchesFilter("needssteam", steamNoId, Norm));

        var steamWithId = new MainWindow.LibraryBrowserFolderView
        {
            PrimaryPlatformLabel = "Steam",
            SteamAppId = "123",
            PlatformLabels = new[] { "Steam" }
        };
        Assert.False(MainWindow.LibraryBrowserFolderViewMatchesFilter("needssteam", steamWithId, Norm));

        var xbox = new MainWindow.LibraryBrowserFolderView
        {
            PrimaryPlatformLabel = "Xbox",
            SteamAppId = "",
            PlatformLabels = new[] { "Xbox" }
        };
        Assert.False(MainWindow.LibraryBrowserFolderViewMatchesFilter("needssteam", xbox, Norm));
    }

    [Fact]
    public void MatchesFilter_MissingGameId_UsesGameIdField()
    {
        var noId = new MainWindow.LibraryBrowserFolderView { GameId = "", FileCount = 1 };
        Assert.True(MainWindow.LibraryBrowserFolderViewMatchesFilter("missinggameid", noId, Norm));

        var hasId = new MainWindow.LibraryBrowserFolderView { GameId = "abc-123", FileCount = 1 };
        Assert.False(MainWindow.LibraryBrowserFolderViewMatchesFilter("missinggameid", hasId, Norm));
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
