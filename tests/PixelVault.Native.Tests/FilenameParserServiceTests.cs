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
    public void Parse_XboxCapture_PreservesFileTimesAndCapturesTitleHint()
    {
        var parser = CreateParser();

        var parsed = parser.Parse("Halo Infinite-2024_03_12-13_04_05.png", string.Empty);

        Assert.Equal("Xbox", parsed.PlatformLabel);
        Assert.Contains("Xbox", parsed.PlatformTags);
        Assert.True(parsed.PreserveFileTimes);
        Assert.Equal("Halo Infinite", parsed.GameTitleHint);
        Assert.Equal(new DateTime(2024, 3, 12, 13, 4, 5), parsed.CaptureTime);
        Assert.Equal(DateTimeKind.Local, parsed.CaptureTime!.Value.Kind);
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
    public void GetConventionRules_BuiltInsExposeReadablePatternText()
    {
        var parser = CreateParser();

        var steamRule = parser.GetConventionRules(string.Empty).First(rule => rule.ConventionId == "steam_screenshot_appid");

        Assert.Equal("[appid]_[yyyy][MM][dd][HH][mm][ss][opt-counter].[ext:media]", steamRule.PatternText);
    }

    static FilenameParserService CreateParser(Func<string, List<FilenameConventionRule>>? loadCustomConventions = null)
    {
        return new FilenameParserService(new FilenameParserServiceDependencies
        {
            LoadCustomConventions = loadCustomConventions ?? (_ => new List<FilenameConventionRule>()),
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
}
