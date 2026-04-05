using System.IO;
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class SettingsServiceTests
{
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
                "library_folder_tile_size=200",
                "library_folder_sort_mode=recent",
                "library_grouping_mode=console",
                "troubleshooting_logging_enabled=1"
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
            Assert.Equal(200, loaded.LibraryFolderTileSize);
            Assert.Equal("recent", loaded.LibraryFolderSortMode);
            Assert.Equal("console", loaded.LibraryGroupingMode);
            Assert.True(loaded.TroubleshootingLoggingEnabled);
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
                LibraryFolderTileSize = 180,
                LibraryFolderSortMode = "photos",
                LibraryGroupingMode = "console",
                TroubleshootingLoggingEnabled = true
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
            Assert.Equal(SettingsService.NormalizeLibraryFolderTileSize(original.LibraryFolderTileSize), loaded.LibraryFolderTileSize);
            Assert.Equal("photos", loaded.LibraryFolderSortMode);
            Assert.Equal("console", loaded.LibraryGroupingMode);
            Assert.True(loaded.TroubleshootingLoggingEnabled);
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
    public void LoadFromIni_FallsBackToAncestorToolsFolder_WhenSavedPathsAreInvalid()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "pv-tools-" + Guid.NewGuid().ToString("N"));
        var appRoot = Path.Combine(tempRoot, "src", "PixelVault.Native", "bin", "Release", "net8.0-windows", "win-x64");
        var toolsRoot = Path.Combine(tempRoot, "tools");
        var settingsPath = Path.Combine(tempRoot, "PixelVault.settings.ini");
        var exifStub = Path.Combine(toolsRoot, "exiftool.exe");
        var ffmpegStub = Path.Combine(toolsRoot, "ffmpeg.exe");

        try
        {
            Directory.CreateDirectory(appRoot);
            Directory.CreateDirectory(toolsRoot);
            File.WriteAllText(exifStub, string.Empty);
            File.WriteAllText(ffmpegStub, string.Empty);
            File.WriteAllLines(settingsPath, new[]
            {
                @"exiftool=C:\missing\exiftool.exe",
                @"ffmpeg=C:\missing\ffmpeg.exe"
            });

            var svc = new SettingsService();
            var loaded = svc.LoadFromIni(settingsPath, new AppSettings(), appRoot, () => string.Empty, () => string.Empty);

            Assert.Equal(exifStub, loaded.ExifToolPath, ignoreCase: true);
            Assert.Equal(ffmpegStub, loaded.FfmpegPath, ignoreCase: true);
        }
        finally
        {
            try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void PersistResolvedToolPaths_RewritesOnlyInvalidSavedToolEntries()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "pv-settings-heal-" + Guid.NewGuid().ToString("N"));
        var settingsPath = Path.Combine(tempRoot, "PixelVault.settings.ini");
        var toolsRoot = Path.Combine(tempRoot, "tools");
        var exifStub = Path.Combine(toolsRoot, "exiftool.exe");
        var ffmpegStub = Path.Combine(toolsRoot, "ffmpeg.exe");

        try
        {
            Directory.CreateDirectory(toolsRoot);
            File.WriteAllText(exifStub, string.Empty);
            File.WriteAllText(ffmpegStub, string.Empty);
            File.WriteAllLines(settingsPath, new[]
            {
                "source=C:\\upload",
                @"exiftool=C:\missing\exiftool.exe",
                @"ffmpeg=C:\missing\ffmpeg.exe",
                "steamgriddb_token=keep-me"
            });

            var svc = new SettingsService();
            var changed = svc.PersistResolvedToolPaths(settingsPath, exifStub, ffmpegStub);
            var lines = File.ReadAllLines(settingsPath);

            Assert.True(changed);
            Assert.Contains("exiftool=" + exifStub, lines, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("ffmpeg=" + ffmpegStub, lines, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("steamgriddb_token=keep-me", lines);
        }
        finally
        {
            try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true); } catch { /* ignore */ }
        }
    }
}
