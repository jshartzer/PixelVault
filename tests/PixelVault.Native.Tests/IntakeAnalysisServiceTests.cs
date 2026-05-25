using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class IntakeAnalysisServiceTests
{
    [Fact]
    public void AnalyzeFiles_XboxTagAndCaptureTime_CanUpdateMetadata()
    {
        var temp = Path.Combine(Path.GetTempPath(), "pv_intake_test_" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            File.WriteAllBytes(temp, new byte[] { 0 });
            var svc = new IntakeAnalysisService(
                _ => new FilenameParseResult
                {
                    MatchedConvention = true,
                    PlatformTags = new[] { "Xbox" },
                    CaptureTime = new DateTime(2024, 1, 1, 12, 0, 0)
                },
                _ => false,
                _ => DateTime.MinValue);

            var map = svc.AnalyzeFiles(new[] { temp });

            var a = Assert.Single(map.Values);
            Assert.True(a.CanUpdateMetadata);
            Assert.Equal(Path.GetFileName(temp), a.FileName);
        }
        finally
        {
            try
            {
                File.Delete(temp);
            }
            catch
            {
                // ignore
            }
        }
    }

    [Fact]
    public void AnalyzeFiles_MissingSteamAppIdWhenRequired_CannotUpdateMetadata()
    {
        var temp = Path.Combine(Path.GetTempPath(), "pv_intake_test_" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            File.WriteAllBytes(temp, new byte[] { 0 });
            var svc = new IntakeAnalysisService(
                _ => new FilenameParseResult
                {
                    MatchedConvention = true,
                    PlatformTags = new[] { "Steam" },
                    CaptureTime = new DateTime(2024, 1, 1),
                    RoutesToManualWhenMissingSteamAppId = true,
                    SteamAppId = string.Empty
                },
                _ => false,
                _ => DateTime.MinValue);

            var map = svc.AnalyzeFiles(new[] { temp });

            var a = Assert.Single(map.Values);
            Assert.False(a.CanUpdateMetadata);
        }
        finally
        {
            try
            {
                File.Delete(temp);
            }
            catch
            {
                // ignore
            }
        }
    }

    [Fact]
    public void AnalyzeFiles_NintendoExifMake_MarksSwitchWithoutFilenameConvention()
    {
        var temp = Path.Combine(Path.GetTempPath(), "pv_intake_test_" + Guid.NewGuid().ToString("N") + ".jpg");
        try
        {
            File.WriteAllBytes(temp, new byte[] { 0 });
            var svc = new IntakeAnalysisService(
                _ => new FilenameParseResult
                {
                    PlatformLabel = "Other",
                    PlatformTags = Array.Empty<string>()
                },
                _ => false,
                _ => DateTime.MinValue,
                (files, _) => new Dictionary<string, EmbeddedMetadataSnapshot>(StringComparer.OrdinalIgnoreCase)
                {
                    [temp] = new EmbeddedMetadataSnapshot { CameraMake = "Nintendo co., ltd" }
                });

            var map = svc.AnalyzeFiles(new[] { temp });

            var a = Assert.Single(map.Values);
            Assert.Equal("Switch", a.Parsed.PlatformLabel);
            Assert.Contains("Switch", a.Parsed.PlatformTags);
            Assert.Contains("Nintendo", a.Parsed.PlatformTags);
        }
        finally
        {
            try
            {
                File.Delete(temp);
            }
            catch
            {
                // ignore
            }
        }
    }

    [Fact]
    public void AnalyzeFiles_XboxGameBarRecording_UsesFilenameDateAndPcTag()
    {
        var root = Path.Combine(Path.GetTempPath(), "pv_intake_test_" + Guid.NewGuid().ToString("N"));
        var temp = Path.Combine(root, "Diablo IV 2026-05-07 19-54-29.mp4");
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllBytes(temp, new byte[] { 0 });
            var parser = new FilenameParserService(new FilenameParserServiceDependencies
            {
                LoadCustomConventions = _ => new List<FilenameConventionRule>(),
                LoadSavedGameIndexRows = _ => new List<GameIndexEditorRow>(),
                NormalizeGameIndexName = value => (value ?? string.Empty).Trim(),
                ParseTagText = value => (value ?? string.Empty).Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries),
                IsVideo = file => string.Equals(Path.GetExtension(file), ".mp4", StringComparison.OrdinalIgnoreCase),
                NormalizeConsoleLabel = MainWindow.NormalizeConsoleLabel
            });
            var svc = new IntakeAnalysisService(
                file => parser.Parse(file, string.Empty),
                file => string.Equals(Path.GetExtension(file), ".mp4", StringComparison.OrdinalIgnoreCase),
                _ => DateTime.MinValue);

            var map = svc.AnalyzeFiles(new[] { temp });

            var a = Assert.Single(map.Values);
            Assert.True(a.CanUpdateMetadata);
            Assert.True(a.PreserveFileTimes);
            Assert.Equal("PC", a.Parsed.PlatformLabel);
            Assert.Contains("PC", a.Parsed.PlatformTags);
            Assert.Equal("Diablo IV", a.Parsed.GameTitleHint);
            Assert.Equal(new DateTime(2026, 5, 7, 19, 54, 29), a.CaptureTime);
        }
        finally
        {
            try
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
            catch
            {
                // ignore
            }
        }
    }

    [Fact]
    public void AnalyzeFiles_TitlelessSwitchAlbumCapture_UsesUploadFolderTitleHint()
    {
        var root = Path.Combine(Path.GetTempPath(), "pv_intake_test_" + Guid.NewGuid().ToString("N"));
        var gameFolder = Path.Combine(root, "Mario Kart World");
        var temp = Path.Combine(gameFolder, "2026050719542900-8AEDFF741E2D23FBED39474178692DAF.jpg");
        try
        {
            Directory.CreateDirectory(gameFolder);
            File.WriteAllBytes(temp, new byte[] { 0 });
            var parser = new FilenameParserService(new FilenameParserServiceDependencies
            {
                LoadCustomConventions = _ => new List<FilenameConventionRule>(),
                LoadSavedGameIndexRows = _ => new List<GameIndexEditorRow>(),
                NormalizeGameIndexName = value => (value ?? string.Empty).Trim(),
                ParseTagText = value => (value ?? string.Empty).Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries),
                IsVideo = _ => false,
                NormalizeConsoleLabel = MainWindow.NormalizeConsoleLabel
            });
            var svc = new IntakeAnalysisService(
                file => parser.Parse(file, string.Empty),
                _ => false,
                _ => DateTime.MinValue,
                null,
                file => string.Equals(file, temp, StringComparison.OrdinalIgnoreCase) ? "Mario Kart World" : string.Empty);

            var map = svc.AnalyzeFiles(new[] { temp });

            var a = Assert.Single(map.Values);
            Assert.True(a.CanUpdateMetadata);
            Assert.Equal("Switch", a.Parsed.PlatformLabel);
            Assert.Contains("Switch", a.Parsed.PlatformTags);
            Assert.Equal("Mario Kart World", a.Parsed.GameTitleHint);
            Assert.Equal(new DateTime(2026, 5, 7, 19, 54, 29), a.CaptureTime);
        }
        finally
        {
            try
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
            catch
            {
                // ignore
            }
        }
    }

    [Fact]
    public void AnalyzeFiles_GenericXboxCapture_UsesUploadFolderTitleHint()
    {
        var root = Path.Combine(Path.GetTempPath(), "pv_intake_test_" + Guid.NewGuid().ToString("N"));
        var gameFolder = Path.Combine(root, "Luna Abyss");
        var temp = Path.Combine(gameFolder, "Screenshot 5_22_2026 9_32_48 PM.png");
        try
        {
            Directory.CreateDirectory(gameFolder);
            File.WriteAllBytes(temp, new byte[] { 0 });
            var svc = new IntakeAnalysisService(
                _ => new FilenameParseResult
                {
                    MatchedConvention = true,
                    ConventionId = "xbox_pc_capture_ampm",
                    ConventionName = "PC Capture (Windows Xbox App)",
                    ConfidenceLabel = "ExplicitPattern",
                    PlatformLabel = "Xbox",
                    PlatformTags = new[] { "Xbox" },
                    GameTitleHint = "Screenshot",
                    CaptureTime = new DateTime(2026, 5, 22, 21, 32, 48)
                },
                _ => false,
                _ => DateTime.MinValue,
                null,
                file => string.Equals(file, temp, StringComparison.OrdinalIgnoreCase) ? "Luna Abyss" : string.Empty);

            var map = svc.AnalyzeFiles(new[] { temp });

            var a = Assert.Single(map.Values);
            Assert.True(a.CanUpdateMetadata);
            Assert.Equal("Xbox", a.Parsed.PlatformLabel);
            Assert.Contains("Xbox", a.Parsed.PlatformTags);
            Assert.Equal("Luna Abyss", a.Parsed.GameTitleHint);
            Assert.Equal(new DateTime(2026, 5, 22, 21, 32, 48), a.CaptureTime);
        }
        finally
        {
            try
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
            catch
            {
                // ignore
            }
        }
    }

    [Fact]
    public void AnalyzeFiles_TitlelessSwitchCaptureSuffix_UsesUploadFolderTitleHint()
    {
        var root = Path.Combine(Path.GetTempPath(), "pv_intake_test_" + Guid.NewGuid().ToString("N"));
        var gameFolder = Path.Combine(root, "Mario Kart World");
        var temp = Path.Combine(gameFolder, "2018032120305000_c.jpg");
        try
        {
            Directory.CreateDirectory(gameFolder);
            File.WriteAllBytes(temp, new byte[] { 0 });
            var parser = new FilenameParserService(new FilenameParserServiceDependencies
            {
                LoadCustomConventions = _ => new List<FilenameConventionRule>(),
                LoadSavedGameIndexRows = _ => new List<GameIndexEditorRow>(),
                NormalizeGameIndexName = value => (value ?? string.Empty).Trim(),
                ParseTagText = value => (value ?? string.Empty).Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries),
                IsVideo = _ => false,
                NormalizeConsoleLabel = MainWindow.NormalizeConsoleLabel
            });
            var svc = new IntakeAnalysisService(
                file => parser.Parse(file, string.Empty),
                _ => false,
                _ => DateTime.MinValue,
                null,
                file => string.Equals(file, temp, StringComparison.OrdinalIgnoreCase) ? "Mario Kart World" : string.Empty);

            var map = svc.AnalyzeFiles(new[] { temp });

            var a = Assert.Single(map.Values);
            Assert.True(a.CanUpdateMetadata);
            Assert.Equal("Switch", a.Parsed.PlatformLabel);
            Assert.Contains("Switch", a.Parsed.PlatformTags);
            Assert.Equal("Mario Kart World", a.Parsed.GameTitleHint);
            Assert.Equal(new DateTime(2018, 3, 21, 20, 30, 50), a.CaptureTime);
        }
        finally
        {
            try
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
            catch
            {
                // ignore
            }
        }
    }

    [Fact]
    public void FilenameAutoIntakeModes_Normalize_UnknownDefaultsToManualOnly()
    {
        Assert.Equal(FilenameAutoIntakeModes.ManualOnly, FilenameAutoIntakeModes.Normalize(null));
        Assert.Equal(FilenameAutoIntakeModes.ManualOnly, FilenameAutoIntakeModes.Normalize(""));
        Assert.Equal(FilenameAutoIntakeModes.ManualOnly, FilenameAutoIntakeModes.Normalize("bogus"));
        Assert.Equal(FilenameAutoIntakeModes.TrustedExactMatch, FilenameAutoIntakeModes.Normalize("TrustedExactMatch"));
        Assert.Equal(FilenameAutoIntakeModes.TrustedExactMatch, FilenameAutoIntakeModes.Normalize("trustedexactmatch"));
    }
}
