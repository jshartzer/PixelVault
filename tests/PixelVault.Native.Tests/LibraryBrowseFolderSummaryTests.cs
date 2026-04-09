using PixelVaultNative;
using Xunit;

namespace PixelVault.Native.Tests;

public sealed class LibraryBrowseFolderSummaryTests
{
    static string Norm(string s) => (s ?? string.Empty).Trim();

    [Fact]
    public void FromFolderView_Copies_Core_Fields()
    {
        var view = new MainWindow.LibraryBrowserFolderView
        {
            ViewKey = "vk1",
            GameId = "game-1",
            Name = "Example Game",
            PrimaryFolderPath = @"D:\lib\example",
            PrimaryPlatformLabel = "Steam",
            PlatformLabels = new[] { "Steam", "PC" },
            PlatformSummaryText = "Steam · PC",
            FileCount = 42,
            PreviewImagePath = @"D:\covers\a.png",
            NewestCaptureUtcTicks = 100,
            NewestRecentSortUtcTicks = 200,
            SteamAppId = "123",
            NonSteamId = "16245548604121415680",
            SteamGridDbId = "sg456",
            IsCompleted100Percent = true,
            CompletedUtcTicks = 300,
            IsMergedAcrossPlatforms = true,
            IsTimelineProjection = false
        };

        var s = LibraryBrowseFolderSummary.FromFolderView(view);
        Assert.NotNull(s);
        Assert.Equal("vk1", s.ViewKey);
        Assert.Equal("game-1", s.GameId);
        Assert.Equal("Example Game", s.Name);
        Assert.Equal(@"D:\lib\example", s.PrimaryFolderPath);
        Assert.Equal("Steam", s.PrimaryPlatformLabel);
        Assert.Equal(new[] { "Steam", "PC" }, s.PlatformLabels);
        Assert.Equal("Steam · PC", s.PlatformSummaryText);
        Assert.Equal(42, s.FileCount);
        Assert.Equal(@"D:\covers\a.png", s.PreviewImagePath);
        Assert.Equal(100, s.NewestCaptureUtcTicks);
        Assert.Equal(200, s.NewestRecentSortUtcTicks);
        Assert.Equal("123", s.SteamAppId);
        Assert.Equal("16245548604121415680", s.NonSteamId);
        Assert.Equal("sg456", s.SteamGridDbId);
        Assert.True(s.IsCompleted100Percent);
        Assert.Equal(300, s.CompletedUtcTicks);
        Assert.True(s.IsMergedAcrossPlatforms);
        Assert.False(s.IsTimelineProjection);
        Assert.False(s.PendingGameAssignment);
    }

    [Fact]
    public void MatchesFilter_MissingId_Includes_PendingGameAssignment_Even_With_GameId()
    {
        var view = new MainWindow.LibraryBrowserFolderView
        {
            GameId = "G00001",
            PrimaryPlatformLabel = "Steam",
            SteamAppId = "1",
            SteamGridDbId = "grid",
            FileCount = 1,
            PendingGameAssignment = true
        };
        var s = LibraryBrowseFolderSummary.FromFolderView(view);
        Assert.NotNull(s);
        Assert.True(s.PendingGameAssignment);
        Assert.True(LibraryBrowseFolderSummary.MatchesFilter("missingid", s, Norm));
    }

    [Fact]
    public void FromFolderView_Null_Returns_Null()
    {
        Assert.Null(LibraryBrowseFolderSummary.FromFolderView(null));
    }

    [Fact]
    public void FromFolderView_Clones_PlatformLabels()
    {
        var labels = new[] { "PS5" };
        var view = new MainWindow.LibraryBrowserFolderView { PlatformLabels = labels };
        var s = LibraryBrowseFolderSummary.FromFolderView(view);
        Assert.NotNull(s);
        labels[0] = "Xbox";
        Assert.Equal("PS5", s.PlatformLabels[0]);
    }

    [Fact]
    public void MatchesFilter_Delegates_Same_As_View_Wrapper()
    {
        var cases = new[]
        {
            new MainWindow.LibraryBrowserFolderView { PrimaryPlatformLabel = "Xbox", FileCount = 1 },
            new MainWindow.LibraryBrowserFolderView { GameId = "", FileCount = 1 },
            new MainWindow.LibraryBrowserFolderView
            {
                PrimaryPlatformLabel = "Steam",
                SteamAppId = "",
                SteamGridDbId = "5528",
                GameId = "g",
                PlatformLabels = new[] { "Steam" }
            },
            new MainWindow.LibraryBrowserFolderView
            {
                PrimaryPlatformLabel = "Emulation",
                NonSteamId = "",
                SteamGridDbId = "5528",
                RetroAchievementsGameId = "258",
                GameId = "g",
                PlatformLabels = new[] { "Emulation" }
            },
            new MainWindow.LibraryBrowserFolderView { IsCompleted100Percent = true, FileCount = 1 },
            new MainWindow.LibraryBrowserFolderView { FileCount = 30, PlatformLabels = new[] { "PC" } },
            new MainWindow.LibraryBrowserFolderView { PreviewImagePath = "", FileCount = 1 }
        };
        var modes = new[] { "all", "missingid", "missingnonsteamid", "completed", "large", "nocover", "crossplatform" };
        foreach (var view in cases)
        {
            var summary = LibraryBrowseFolderSummary.FromFolderView(view);
            foreach (var mode in modes)
            {
                var fromView = MainWindow.LibraryBrowserFolderViewMatchesFilter(mode, view, Norm);
                var fromSummary = LibraryBrowseFolderSummary.MatchesFilter(mode, summary, Norm);
                Assert.Equal(fromView, fromSummary);
            }
        }
    }

    [Fact]
    public void MatchesFilter_NullFolder_False()
    {
        Assert.False(LibraryBrowseFolderSummary.MatchesFilter("completed", null, Norm));
    }

    [Fact]
    public void IsSteamTagged_Primary_Or_Label()
    {
        var primary = new MainWindow.LibraryBrowserFolderView
        {
            PrimaryPlatformLabel = "Steam",
            PlatformLabels = new[] { "PC" }
        };
        Assert.True(LibraryBrowseFolderSummary.IsSteamTagged(LibraryBrowseFolderSummary.FromFolderView(primary), Norm));

        var labelOnly = new MainWindow.LibraryBrowserFolderView
        {
            PrimaryPlatformLabel = "Xbox",
            PlatformLabels = new[] { "Steam" }
        };
        Assert.True(LibraryBrowseFolderSummary.IsSteamTagged(LibraryBrowseFolderSummary.FromFolderView(labelOnly), Norm));

        Assert.False(LibraryBrowseFolderSummary.IsSteamTagged(null, Norm));
    }

    [Fact]
    public void IsEmulationTagged_Primary_Or_Label()
    {
        var primary = new MainWindow.LibraryBrowserFolderView
        {
            PrimaryPlatformLabel = "Emulation",
            PlatformLabels = new[] { "PC" }
        };
        Assert.True(LibraryBrowseFolderSummary.IsEmulationTagged(LibraryBrowseFolderSummary.FromFolderView(primary), Norm));

        var labelOnly = new MainWindow.LibraryBrowserFolderView
        {
            PrimaryPlatformLabel = "Xbox",
            PlatformLabels = new[] { "Emulation" }
        };
        Assert.True(LibraryBrowseFolderSummary.IsEmulationTagged(LibraryBrowseFolderSummary.FromFolderView(labelOnly), Norm));
    }
}
