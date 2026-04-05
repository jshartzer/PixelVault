using System.Linq;
using System.Text.RegularExpressions;
using SQLitePCL;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class FilenameParserServiceTests
{
    public FilenameParserServiceTests()
    {
        Batteries_V2.Init();
    }

    [Fact]
    public void Parse_SteamScreenshotWithAppId_ReturnsSteamAppIdAndTimestamp()
    {
        var parser = CreateParser();

        var parsed = parser.Parse("2561580_20260326221306_1.png", string.Empty);

        Assert.Equal("Steam", parsed.PlatformLabel);
        Assert.Contains("Steam", parsed.PlatformTags);
        Assert.Equal("2561580", parsed.SteamAppId);
        Assert.Equal(new DateTime(2026, 3, 26, 22, 13, 06), parsed.CaptureTime);
        Assert.Equal(DateTimeKind.Local, parsed.CaptureTime!.Value.Kind);
        Assert.False(parsed.RoutesToManualWhenMissingSteamAppId);
    }

    [Fact]
    public void Parse_SteamManualExport_RoutesToManualAndKeepsTimestamp()
    {
        var parser = CreateParser();

        var parsed = parser.Parse("20200525124119_1.jpg", string.Empty);

        Assert.Equal("Steam", parsed.PlatformLabel);
        Assert.Contains("Steam", parsed.PlatformTags);
        Assert.Equal(string.Empty, parsed.SteamAppId);
        Assert.True(parsed.RoutesToManualWhenMissingSteamAppId);
        Assert.Equal(new DateTime(2020, 5, 25, 12, 41, 19), parsed.CaptureTime);
        Assert.Equal(DateTimeKind.Local, parsed.CaptureTime!.Value.Kind);
    }

    [Fact]
    public void Parse_XboxCapture_WritesFileTimesLikeSteamAndCapturesTitleHint()
    {
        var parser = CreateParser();

        var parsed = parser.Parse("Halo Infinite-2024_03_12-13_04_05.png", string.Empty);

        Assert.Equal("Xbox", parsed.PlatformLabel);
        Assert.Contains("Xbox", parsed.PlatformTags);
        Assert.False(parsed.PreserveFileTimes);
        Assert.Equal("Halo Infinite", parsed.GameTitleHint);
        Assert.Equal(new DateTime(2024, 3, 12, 13, 4, 5), parsed.CaptureTime);
        Assert.Equal(DateTimeKind.Local, parsed.CaptureTime!.Value.Kind);
    }

    [Fact]
    public void Parse_XboxCapture_UnderscoreInTitle_ParsesFilenameDate()
    {
        var parser = CreateParser();

        var parsed = parser.Parse("The Witcher 3_ Wild Hunt-2018_01_03-04_05_23.png", string.Empty);

        Assert.Equal("Xbox", parsed.PlatformLabel);
        Assert.Equal("The Witcher 3: Wild Hunt", parsed.GameTitleHint);
        Assert.Equal(new DateTime(2018, 1, 3, 4, 5, 23), parsed.CaptureTime);
        Assert.Equal("xbox_capture", parsed.ConventionId);
    }

    [Fact]
    public void Parse_XboxCapture_WithHyphenSeparatedTime_WritesFileTimesLikeSteam()
    {
        var parser = CreateParser();

        var parsed = parser.Parse("Human Fall Flat-2026_03_31-00-09-35.png", string.Empty);

        Assert.Equal("Xbox", parsed.PlatformLabel);
        Assert.Contains("Xbox", parsed.PlatformTags);
        Assert.False(parsed.PreserveFileTimes);
        Assert.Equal("Human Fall Flat", parsed.GameTitleHint);
        Assert.Equal(new DateTime(2026, 3, 31, 0, 9, 35), parsed.CaptureTime);
        Assert.Equal(DateTimeKind.Local, parsed.CaptureTime!.Value.Kind);
        Assert.Equal("xbox_capture_hyphen_time", parsed.ConventionId);
    }

    [Fact]
    public void Parse_XboxPcCapture_WithAmPmTimestamp_UsesXboxPcRule()
    {
        var parser = CreateParser();

        var parsed = parser.Parse("Human Fall Flat 4_4_2026 7_23_36 PM.png", string.Empty);

        Assert.Equal("Xbox PC", parsed.PlatformLabel);
        Assert.Contains("Platform:Xbox PC", parsed.PlatformTags);
        Assert.True(parsed.PreserveFileTimes);
        Assert.Equal("Human Fall Flat", parsed.GameTitleHint);
        Assert.Equal(new DateTime(2026, 4, 4, 19, 23, 36), parsed.CaptureTime);
        Assert.Equal(DateTimeKind.Local, parsed.CaptureTime!.Value.Kind);
        Assert.Equal("xbox_pc_capture_ampm", parsed.ConventionId);
    }

    [Fact]
    public void Parse_XboxPcCapture_WithTrailingDigitInTitle_KeepsDigitAsPartOfGameName()
    {
        var parser = CreateParser();

        var parsed = parser.Parse("Forza Horizon 5 4_4_2026 7_23_36 PM.png", string.Empty);

        Assert.Equal("Xbox PC", parsed.PlatformLabel);
        Assert.Equal("Forza Horizon 5", parsed.GameTitleHint);
        Assert.Equal(new DateTime(2026, 4, 4, 19, 23, 36), parsed.CaptureTime);
        Assert.Equal("xbox_pc_capture_ampm", parsed.ConventionId);
    }

    [Fact]
    public void GetGameTitleHint_XboxPcCaptureBaseName_UsesTrailingTimestampInsteadOfFirstUnderscore()
    {
        var parser = CreateParser();

        var title = parser.GetGameTitleHint("PowerWash Simulator 4_4_2026 7_16_35 PM", string.Empty);

        Assert.Equal("PowerWash Simulator", title);
    }

    [Fact]
    public void GetGameTitleHint_XboxPcCaptureScreenshotBaseName_DoesNotKeepDateDigitInTitle()
    {
        var parser = CreateParser();

        var title = parser.GetGameTitleHint("Screenshot 4_4_2026 7_21_47 PM", string.Empty);

        Assert.Equal("Screenshot", title);
    }

    [Fact]
    public void Parse_GenericDate_IsoAtStartOfFileName_ParsesDate()
    {
        var parser = CreateParser();

        var parsed = parser.Parse("2011-05-16_00001.jpg", string.Empty);

        Assert.Equal(new DateTime(2011, 5, 16), parsed.CaptureTime);
        Assert.Equal(DateTimeKind.Local, parsed.CaptureTime!.Value.Kind);
        Assert.Equal("generic-date-match", parsed.ConventionId);
    }

    [Fact]
    public void Parse_GenericDate_CompactAfterUnderscore_ParsesDate()
    {
        var parser = CreateParser();

        var parsed = parser.Parse("IMG_20110516_0001.jpg", string.Empty);

        Assert.Equal(new DateTime(2011, 5, 16), parsed.CaptureTime);
        Assert.Equal("generic-date-match", parsed.ConventionId);
    }

    [Fact]
    public void Parse_GenericDate_DottedDate_ParsesDate()
    {
        var parser = CreateParser();

        var parsed = parser.Parse("vacation_2011.05.16.jpg", string.Empty);

        Assert.Equal(new DateTime(2011, 5, 16), parsed.CaptureTime);
        Assert.Equal("generic-date-match", parsed.ConventionId);
    }

    [Fact]
    public void Parse_Ps5Share_WithSegmentAndFractionalSeconds_UsesPs5Rule()
    {
        var parser = CreateParser();

        var parsed = parser.Parse("Astro's Playroom_CLIMBING RUN_2023101311054800.jpg", string.Empty);

        Assert.Equal("PS5", parsed.PlatformLabel);
        Assert.Contains("PS5", parsed.PlatformTags);
        Assert.Contains("PlayStation", parsed.PlatformTags);
        Assert.Equal("Astro's Playroom", parsed.GameTitleHint);
        Assert.Equal(new DateTime(2023, 10, 13, 11, 5, 48), parsed.CaptureTime);
        Assert.Equal(DateTimeKind.Local, parsed.CaptureTime!.Value.Kind);
        Assert.Equal("ps5_share_segmented_fractional", parsed.ConventionId);
    }

    [Fact]
    public void Parse_CustomDatabaseConvention_ExtendsBuiltInRules()
    {
        using var harness = new FilenameConventionHarness();
        harness.IndexPersistence.SaveFilenameConventions(
            harness.LibraryRoot,
            new[]
            {
                new FilenameConventionRule
                {
                    ConventionId = "switch_album",
                    Name = "Switch Album",
                    Enabled = true,
                    Priority = 1400,
                    Pattern = "Switch_[title]_[yyyy][MM][dd].[ext:image]",
                    PatternText = "Switch_[title]_[yyyy][MM][dd].[ext:image]",
                    PlatformLabel = "Switch",
                    PlatformTagsText = "Switch;Nintendo",
                    TitleGroup = "title",
                    TimestampGroup = "stamp",
                    TimestampFormat = "yyyyMMdd",
                    ConfidenceLabel = "UserRule"
                }
            });

        var parser = CreateParser(root => harness.IndexPersistence.LoadFilenameConventions(root));

        var parsed = parser.Parse("Switch_Mario Odyssey_20260327.png", harness.LibraryRoot);

        Assert.Equal("Switch", parsed.PlatformLabel);
        Assert.Contains("Nintendo", parsed.PlatformTags);
        Assert.Contains("Switch", parsed.PlatformTags);
        Assert.Equal("Mario Odyssey", parsed.GameTitleHint);
        Assert.Equal(new DateTime(2026, 3, 27), parsed.CaptureTime);
        Assert.Equal(DateTimeKind.Local, parsed.CaptureTime!.Value.Kind);
        Assert.Equal("switch_album", parsed.ConventionId);
        Assert.Equal("UserRule", parsed.ConfidenceLabel);
    }

    [Fact]
    public void Parse_RenamedSteamScreenshot_UsesKnownSteamGameIndexRow()
    {
        var parser = CreateParser();

        var parsed = parser.Parse("Retro Rewind - Video Store Simulator_20260328191702_1.png", "library-root");

        Assert.Equal("Steam", parsed.PlatformLabel);
        Assert.Contains("Steam", parsed.PlatformTags);
        Assert.Equal(string.Empty, parsed.SteamAppId);
        Assert.Equal("steam_renamed_title_timestamp", parsed.ConventionId);
        Assert.Equal("Heuristic", parsed.ConfidenceLabel);
        Assert.Equal(new DateTime(2026, 3, 28, 19, 17, 02), parsed.CaptureTime);
        Assert.Equal(DateTimeKind.Local, parsed.CaptureTime!.Value.Kind);
    }

    [Fact]
    public void Parse_RenamedSteamScreenshot_WithKnownGameIndexRow_FillsSteamAppId()
    {
        var parser = CreateParser(
            loadSavedGameIndexRows: _ => new List<GameIndexEditorRow>
            {
                new()
                {
                    GameId = "G00001",
                    Name = "Retro Rewind - Video Store Simulator",
                    PlatformLabel = "Steam",
                    SteamAppId = "2561580"
                }
            });

        var parsed = parser.Parse("Retro Rewind - Video Store Simulator_20260328191702_1.png", "library-root");

        Assert.Equal("Steam", parsed.PlatformLabel);
        Assert.Contains("Steam", parsed.PlatformTags);
        Assert.Equal("2561580", parsed.SteamAppId);
        Assert.Equal("steam_renamed_title_timestamp", parsed.ConventionId);
        Assert.Equal("Heuristic", parsed.ConfidenceLabel);
    }

    [Fact]
    public void Parse_RenamedSteamScreenshot_CachesKnownSteamLookupPerRoot()
    {
        var loadCount = 0;
        var parser = CreateParser(
            loadSavedGameIndexRows: _ =>
            {
                loadCount++;
                return new List<GameIndexEditorRow>
                {
                    new()
                    {
                        GameId = "G00001",
                        Name = "Retro Rewind - Video Store Simulator",
                        PlatformLabel = "Steam",
                        SteamAppId = "2561580"
                    }
                };
            });

        var first = parser.Parse("Retro Rewind - Video Store Simulator_20260328191702_1.png", "library-root");
        var second = parser.Parse("Retro Rewind - Video Store Simulator_20260329145155_1.png", "library-root");

        Assert.Equal("2561580", first.SteamAppId);
        Assert.Equal("2561580", second.SteamAppId);
        Assert.Equal(1, loadCount);
    }

    [Fact]
    public void InvalidateRules_ClearsKnownSteamLookupCache()
    {
        var loadCount = 0;
        var parser = CreateParser(
            loadSavedGameIndexRows: _ =>
            {
                loadCount++;
                return new List<GameIndexEditorRow>
                {
                    new()
                    {
                        GameId = "G00001",
                        Name = "Retro Rewind - Video Store Simulator",
                        PlatformLabel = "Steam",
                        SteamAppId = "2561580"
                    }
                };
            });

        var first = parser.Parse("Retro Rewind - Video Store Simulator_20260328191702_1.png", "library-root");
        parser.InvalidateRules("library-root");
        var second = parser.Parse("Retro Rewind - Video Store Simulator_20260329145155_1.png", "library-root");

        Assert.Equal("2561580", first.SteamAppId);
        Assert.Equal("2561580", second.SteamAppId);
        Assert.Equal(2, loadCount);
    }

    [Fact]
    public void GetConventionRules_BuiltInsExposeReadablePatternText()
    {
        var parser = CreateParser();

        var steamRule = parser.GetConventionRules(string.Empty).First(rule => rule.ConventionId == "steam_screenshot_appid");

        Assert.Equal("[appid]_[yyyy][MM][dd][HH][mm][ss][opt-counter].[ext:media]", steamRule.PatternText);
    }

    static FilenameParserService CreateParser(
        Func<string, List<FilenameConventionRule>>? loadCustomConventions = null,
        Func<string, List<GameIndexEditorRow>>? loadSavedGameIndexRows = null)
    {
        return new FilenameParserService(new FilenameParserServiceDependencies
        {
            LoadCustomConventions = loadCustomConventions ?? (_ => new List<FilenameConventionRule>()),
            LoadSavedGameIndexRows = loadSavedGameIndexRows ?? (_ => new List<GameIndexEditorRow>()),
            NormalizeGameIndexName = value => Regex.Replace((value ?? string.Empty).Trim(), "\\s+", " "),
            ParseTagText = value => (value ?? string.Empty)
                .Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(tag => Regex.Replace(tag, "\\s+", " ").Trim())
                .Where(tag => !string.IsNullOrWhiteSpace(tag)),
            IsVideo = file =>
            {
                var extension = Path.GetExtension(file ?? string.Empty).ToLowerInvariant();
                return extension == ".mp4" || extension == ".mkv" || extension == ".avi" || extension == ".mov" || extension == ".wmv" || extension == ".webm";
            },
            NormalizeConsoleLabel = value =>
            {
                var normalized = (value ?? string.Empty).Trim();
                if (string.Equals(normalized, "PS5", StringComparison.OrdinalIgnoreCase)) return "PS5";
                if (string.Equals(normalized, "PlayStation", StringComparison.OrdinalIgnoreCase)) return "PlayStation";
                if (string.Equals(normalized, "Steam", StringComparison.OrdinalIgnoreCase)) return "Steam";
                if (string.Equals(normalized, "Xbox PC", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(normalized, "Xbox/Windows", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(normalized, "Xbox Windows", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(normalized, "Xbox on Windows", StringComparison.OrdinalIgnoreCase)) return "Xbox PC";
                if (string.Equals(normalized, "Xbox", StringComparison.OrdinalIgnoreCase)) return "Xbox";
                if (string.Equals(normalized, "PC", StringComparison.OrdinalIgnoreCase)) return "PC";
                if (string.IsNullOrWhiteSpace(normalized)) return "Other";
                return normalized;
            }
        });
    }

    sealed class FilenameConventionHarness : IDisposable
    {
        public string RootPath { get; }
        public string CacheRoot { get; }
        public string LibraryRoot { get; }
        public IndexPersistenceService IndexPersistence { get; }

        public FilenameConventionHarness()
        {
            RootPath = Path.Combine(Path.GetTempPath(), "PixelVault.Native.Tests", Guid.NewGuid().ToString("N"));
            CacheRoot = Path.Combine(RootPath, "cache");
            LibraryRoot = Path.Combine(RootPath, "library-root");
            Directory.CreateDirectory(CacheRoot);
            Directory.CreateDirectory(LibraryRoot);

            IndexPersistence = new IndexPersistenceService(new IndexPersistenceServiceDependencies
            {
                CacheRoot = CacheRoot,
                SafeCacheName = value => Regex.Replace((value ?? string.Empty).ToLowerInvariant(), @"[^a-z0-9]+", "_").Trim('_'),
                NormalizeGameId = value => (value ?? string.Empty).Trim().ToUpperInvariant(),
                DisplayExternalIdValue = value => value == "<CLEARED>" ? string.Empty : value,
                IsClearedExternalIdValue = value => string.Equals(value, "<CLEARED>", StringComparison.OrdinalIgnoreCase),
                SerializeExternalIdValue = (value, suppressAutoResolve) =>
                    suppressAutoResolve && string.IsNullOrWhiteSpace(value) ? "<CLEARED>" : (value ?? string.Empty).Trim(),
                MergeGameIndexRows = rows => rows.Where(row => row != null).ToList()!,
                BuildGameIdAliasMap = (sourceRows, normalizedRows) => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                HasGameIdAliasChanges = _ => false,
                ParseInt = value => int.TryParse(value, out var parsed) ? parsed : 0,
                ParseTagText = value => (value ?? string.Empty).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(tag => tag.Trim()),
                DetermineConsoleLabelFromTags = tags => tags.FirstOrDefault() ?? string.Empty,
                RewriteGameIdAliasesInLibraryFolderCacheFile = (_, _) => { },
                ApplyGameIdAliasesToCachedMetadataIndex = (_, _) => { }
            });
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(RootPath)) Directory.Delete(RootPath, true);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public void NormalizeColonStandin_UnderscoreSpace_BecomesColonSpace()
    {
        Assert.Equal("The Witcher 3: Wild Hunt", FilenameParserService.NormalizeColonStandinUnderscoresForGameTitle("The Witcher 3_ Wild Hunt"));
        Assert.Equal("A: B: C", FilenameParserService.NormalizeColonStandinUnderscoresForGameTitle("A_ B_ C"));
    }

    [Fact]
    public void NormalizeColonStandin_SnakeCaseOrNoSpace_Unchanged()
    {
        Assert.Equal("My_Favorite_Game", FilenameParserService.NormalizeColonStandinUnderscoresForGameTitle("My_Favorite_Game"));
        Assert.Equal("Halo Infinite", FilenameParserService.NormalizeColonStandinUnderscoresForGameTitle("Halo Infinite"));
    }
}
