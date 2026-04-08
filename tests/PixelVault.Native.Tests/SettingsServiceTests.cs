using System.IO;
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class SettingsServiceTests
{
    [Theory]
    [InlineData("timeline")]
    [InlineData("photo timeline")]
    [InlineData("capture timeline")]
    public void NormalizeLibraryGroupingMode_SupportsTimelineAliases(string raw)
    {
        Assert.Equal("timeline", SettingsService.NormalizeLibraryGroupingMode(raw));
    }

    [Theory]
    [InlineData("alphabetical", "alpha")]
    [InlineData("date captured", "captured")]
    [InlineData("date added", "added")]
    [InlineData("most photos", "photos")]
    [InlineData("platform", "alpha")]
    public void NormalizeLibraryFolderSortMode_SupportsMenuAliases(string raw, string expected)
    {
        Assert.Equal(expected, SettingsService.NormalizeLibraryFolderSortMode(raw));
    }

    [Theory]
    [InlineData(100, 160)]
    [InlineData(280, 280)]
    [InlineData(9999, 1400)]
    public void NormalizeLibraryPhotoTileSize_ClampsToRange(int input, int expected)
    {
        Assert.Equal(expected, SettingsService.NormalizeLibraryPhotoTileSize(input));
    }

    [Theory]
    [InlineData(40, 48)]
    [InlineData(200, 200)]
    [InlineData(5000, 1000)]
    public void NormalizeLibraryFolderTileSize_ClampsToRange(int input, int expected)
    {
        Assert.Equal(expected, SettingsService.NormalizeLibraryFolderTileSize(input));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(5, 5)]
    [InlineData(99, 12)]
    public void NormalizeLibraryFolderGridColumnCount_Clamps(int input, int expected)
    {
        Assert.Equal(expected, SettingsService.NormalizeLibraryFolderGridColumnCount(input));
    }

    [Theory]
    [InlineData(9, 8)]
    [InlineData(3, 3)]
    public void NormalizeLibraryPhotoGridColumnCount_Clamps(int input, int expected)
    {
        Assert.Equal(expected, SettingsService.NormalizeLibraryPhotoGridColumnCount(input));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(9, 2)]
    public void NormalizeLibraryPhotoRailFolderGridColumnCount_ClampsToTwoOrAuto(int input, int expected)
    {
        Assert.Equal(expected, SettingsService.NormalizeLibraryPhotoRailFolderGridColumnCount(input));
    }

    [Theory]
    [InlineData("100%", "completed")]
    [InlineData("cross-platform", "crossplatform")]
    [InlineData("25+ captures", "large")]
    [InlineData("all", "all")]
    [InlineData("missingid", "missingid")]
    [InlineData("missing id", "missingid")]
    [InlineData("needssteam", "missingid")]
    [InlineData("missing steam app id", "missingid")]
    [InlineData("needssteamgrid", "missingid")]
    [InlineData("missing steam grid", "missingid")]
    [InlineData("steam griddb", "missingid")]
    [InlineData("nocover", "nocover")]
    [InlineData("no cover", "nocover")]
    [InlineData("missinggameid", "missingid")]
    [InlineData("needs game id", "missingid")]
    public void NormalizeLibraryFolderFilterMode_SupportsAliases(string raw, string expected)
    {
        Assert.Equal(expected, SettingsService.NormalizeLibraryFolderFilterMode(raw));
    }

    [Fact]
    public void SerializeSourceRoots_NormalizesSeparatorsAndDedupes()
    {
        var raw = "  C:\\a  \r\nC:\\b\nC:\\a  ";
        var s = SettingsService.SerializeSourceRoots(raw);
        Assert.Equal(@"C:\a;C:\b", s, ignoreCase: true);
    }

    [Fact]
    public void LoadFromIni_MergesOverInitial_WhenFileExists()
    {
        var path = Path.Combine(Path.GetTempPath(), "PixelVault-settings-test-" + Guid.NewGuid().ToString("N") + ".ini");
        try
        {
            File.WriteAllLines(path, new[]
            {
                "source=C:\\upload",
                "destination=C:\\dest",
                "library=C:\\lib",
                "exiftool=C:\\tools\\exiftool.exe",
                "ffmpeg=",
                "steamgriddb_token=secret",
                "steam_web_api_key=steamkey",
                "retroachievements_api_key=rakey",
                "library_folder_tile_size=200",
                "library_photo_tile_size=410",
                "library_folder_grid_columns=3",
                "library_photo_grid_columns=4",
                "library_folder_sort_mode=recent",
                "library_folder_filter_mode=100%",
                "library_grouping_mode=console",
                "troubleshooting_logging_enabled=1",
                "library_double_click_set_folder_cover=1"
            });

            var initial = new AppSettings
            {
                SourceRootsSerialized = "OLD",
                DestinationRoot = "OLD",
                LibraryRoot = "OLD",
                ExifToolPath = Path.Combine(Path.GetTempPath(), "missing-exif.exe"),
                FfmpegPath = string.Empty,
                SteamGridDbApiToken = string.Empty,
                LibraryFolderTileSize = 240,
                LibraryPhotoTileSize = 260,
                LibraryFolderGridColumnCount = 1,
                LibraryPhotoGridColumnCount = 2,
                LibraryFolderSortMode = "platform",
                LibraryGroupingMode = "all",
                TroubleshootingLoggingEnabled = false
            };

            var svc = new SettingsService();
            var appRoot = Path.GetTempPath();
            var loaded = svc.LoadFromIni(path, initial, appRoot, () => string.Empty, () => string.Empty);

            Assert.Equal(SettingsService.SerializeSourceRoots(@"C:\upload"), loaded.SourceRootsSerialized, ignoreCase: true);
            Assert.Equal(@"C:\dest", loaded.DestinationRoot, ignoreCase: true);
            Assert.Equal(@"C:\lib", loaded.LibraryRoot, ignoreCase: true);
            Assert.Equal(@"C:\tools\exiftool.exe", loaded.ExifToolPath, ignoreCase: true);
            Assert.Equal("secret", loaded.SteamGridDbApiToken);
            Assert.Equal("steamkey", loaded.SteamWebApiKey);
            Assert.Equal("rakey", loaded.RetroAchievementsApiKey);
            Assert.Equal(200, loaded.LibraryFolderTileSize);
            Assert.Equal(410, loaded.LibraryPhotoTileSize);
            Assert.Equal(3, loaded.LibraryFolderGridColumnCount);
            Assert.Equal(4, loaded.LibraryPhotoGridColumnCount);
            Assert.Equal("added", loaded.LibraryFolderSortMode);
            Assert.Equal("completed", loaded.LibraryFolderFilterMode);
            Assert.Equal("console", loaded.LibraryGroupingMode);
            Assert.True(loaded.TroubleshootingLoggingEnabled);
            Assert.True(loaded.LibraryDoubleClickSetsFolderCover);
            Assert.Empty(loaded.LibraryIndexAnchor ?? string.Empty);
        }
        finally
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void SaveToIni_WhenLibraryPathChanges_PersistsPreviousPathAsLibraryIndexAnchor()
    {
        var path = Path.Combine(Path.GetTempPath(), "pv-anchor-" + Guid.NewGuid().ToString("N") + ".ini");
        try
        {
            var svc = new SettingsService();
            svc.SaveToIni(path, new AppSettings { LibraryRoot = @"D:\lib-a", LibraryFolderTileSize = 200 });

            var afterFirst = svc.LoadFromIni(path, new AppSettings(), Path.GetTempPath(), () => string.Empty, () => string.Empty);
            Assert.Equal(@"D:\lib-a", afterFirst.LibraryRoot, ignoreCase: true);
            Assert.Equal(@"D:\lib-a", afterFirst.LibraryIndexAnchor, ignoreCase: true);

            svc.SaveToIni(path, new AppSettings { LibraryRoot = @"D:\lib-b", LibraryFolderTileSize = 200 });
            var afterChange = svc.LoadFromIni(path, new AppSettings(), Path.GetTempPath(), () => string.Empty, () => string.Empty);
            Assert.Equal(@"D:\lib-b", afterChange.LibraryRoot, ignoreCase: true);
            Assert.Equal(@"D:\lib-a", afterChange.LibraryIndexAnchor, ignoreCase: true);
        }
        finally
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void SaveToIni_ThenLoadFromIni_RoundTrips()
    {
        var path = Path.Combine(Path.GetTempPath(), "PixelVault-settings-rt-" + Guid.NewGuid().ToString("N") + ".ini");
        var exifStub = Path.Combine(Path.GetTempPath(), "pv-rt-exif-" + Guid.NewGuid().ToString("N") + ".exe");
        var ffmpegStub = Path.Combine(Path.GetTempPath(), "pv-rt-ff-" + Guid.NewGuid().ToString("N") + ".exe");
        try
        {
            File.WriteAllText(exifStub, string.Empty);
            File.WriteAllText(ffmpegStub, string.Empty);

            var original = new AppSettings
            {
                SourceRootsSerialized = SettingsService.SerializeSourceRoots("D:\\in1\r\nD:\\in2"),
                DestinationRoot = @"D:\out",
                LibraryRoot = @"D:\lib",
                ExifToolPath = exifStub,
                FfmpegPath = ffmpegStub,
                SteamGridDbApiToken = "tok",
                SteamWebApiKey = "sw",
                RetroAchievementsApiKey = "ra",
                LibraryFolderTileSize = 180,
                LibraryPhotoTileSize = 310,
                LibraryFolderGridColumnCount = 2,
                LibraryPhotoGridColumnCount = 0,
                LibraryFolderSortMode = "photos",
                LibraryFolderFilterMode = "large",
                LibraryGroupingMode = "console",
                LibraryPhotoRailFolderTileSize = 190,
                LibraryPhotoRailFolderSortMode = "captured",
                LibraryPhotoRailFolderFilterMode = "large",
                LibraryPhotoRailFolderGridColumnCount = 2,
                TroubleshootingLoggingEnabled = true,
                LibraryDoubleClickSetsFolderCover = true,
                LibraryBrowserFolderPaneWidth = 542.25,
                StarredExportFolder = @"D:\immich-drop"
            };

            var svc = new SettingsService();
            svc.SaveToIni(path, original);

            var blank = new AppSettings();
            var loaded = svc.LoadFromIni(path, blank, Path.GetTempPath(), () => string.Empty, () => string.Empty);

            Assert.Equal(original.SourceRootsSerialized, loaded.SourceRootsSerialized, ignoreCase: true);
            Assert.Equal(original.DestinationRoot, loaded.DestinationRoot, ignoreCase: true);
            Assert.Equal(original.LibraryRoot, loaded.LibraryRoot, ignoreCase: true);
            Assert.Equal(original.ExifToolPath, loaded.ExifToolPath, ignoreCase: true);
            Assert.Equal(original.FfmpegPath, loaded.FfmpegPath, ignoreCase: true);
            Assert.Equal(original.SteamGridDbApiToken, loaded.SteamGridDbApiToken);
            Assert.Equal(original.SteamWebApiKey, loaded.SteamWebApiKey);
            Assert.Equal(original.RetroAchievementsApiKey, loaded.RetroAchievementsApiKey);
            Assert.Equal(SettingsService.NormalizeLibraryFolderTileSize(original.LibraryFolderTileSize), loaded.LibraryFolderTileSize);
            Assert.Equal(SettingsService.NormalizeLibraryPhotoTileSize(original.LibraryPhotoTileSize), loaded.LibraryPhotoTileSize);
            Assert.Equal(SettingsService.NormalizeLibraryFolderGridColumnCount(original.LibraryFolderGridColumnCount), loaded.LibraryFolderGridColumnCount);
            Assert.Equal(SettingsService.NormalizeLibraryPhotoGridColumnCount(original.LibraryPhotoGridColumnCount), loaded.LibraryPhotoGridColumnCount);
            Assert.Equal("photos", loaded.LibraryFolderSortMode);
            Assert.Equal("large", loaded.LibraryFolderFilterMode);
            Assert.Equal("console", loaded.LibraryGroupingMode);
            Assert.Equal(SettingsService.NormalizeLibraryFolderTileSize(original.LibraryPhotoRailFolderTileSize), loaded.LibraryPhotoRailFolderTileSize);
            Assert.Equal("captured", loaded.LibraryPhotoRailFolderSortMode);
            Assert.Equal("large", loaded.LibraryPhotoRailFolderFilterMode);
            Assert.Equal(2, loaded.LibraryPhotoRailFolderGridColumnCount);
            Assert.True(loaded.TroubleshootingLoggingEnabled);
            Assert.True(loaded.LibraryDoubleClickSetsFolderCover);
            Assert.Equal(542.25, loaded.LibraryBrowserFolderPaneWidth, 5);
            Assert.Equal(@"D:\immich-drop", loaded.StarredExportFolder, ignoreCase: true);
            Assert.Equal(loaded.LibraryRoot ?? string.Empty, loaded.LibraryIndexAnchor ?? string.Empty, ignoreCase: true);
        }
        finally
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { /* ignore */ }
            try { if (File.Exists(exifStub)) File.Delete(exifStub); } catch { /* ignore */ }
            try { if (File.Exists(ffmpegStub)) File.Delete(ffmpegStub); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void SaveToIni_ThenLoadFromIni_RoundTripsTimelineGrouping()
    {
        var path = Path.Combine(Path.GetTempPath(), "PixelVault-settings-timeline-" + Guid.NewGuid().ToString("N") + ".ini");
        try
        {
            var svc = new SettingsService();
            svc.SaveToIni(path, new AppSettings
            {
                LibraryRoot = @"D:\lib",
                LibraryGroupingMode = "timeline",
                LibraryFolderTileSize = 220
            });

            var loaded = svc.LoadFromIni(path, new AppSettings(), Path.GetTempPath(), () => string.Empty, () => string.Empty);

            Assert.Equal("timeline", loaded.LibraryGroupingMode);
        }
        finally
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { /* ignore */ }
        }
    }
}
