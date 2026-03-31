using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using SQLitePCL;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class IndexPersistenceServiceTests
{
    public IndexPersistenceServiceTests()
    {
        Batteries_V2.Init();
    }

    [Fact]
    public void SaveAndLoadSavedGameIndexRows_RoundTripsNormalizedRows()
    {
        using var harness = new IndexPersistenceHarness();
        var existingFile = harness.CreateFile("library\\shot-01.png");
        var missingFile = Path.Combine(harness.RootPath, "library", "missing.png");

        harness.Service.SaveSavedGameIndexRows(
            harness.LibraryRoot,
            new[]
            {
                new GameIndexEditorRow
                {
                    GameId = "  g00042 ",
                    Name = " Horizon Zero Dawn ",
                    PlatformLabel = " Steam ",
                    SteamAppId = "12345",
                    SteamGridDbId = string.Empty,
                    SuppressSteamGridDbIdAutoResolve = true,
                    FileCount = 9,
                    FolderPath = " C:\\Library\\HZD ",
                    PreviewImagePath = " C:\\Library\\HZD\\cover.png ",
                    FilePaths = new[] { existingFile, missingFile }
                }
            });

        var rows = harness.Service.LoadSavedGameIndexRows(harness.LibraryRoot);

        var row = Assert.Single(rows);
        Assert.Equal("G00042", row.GameId);
        Assert.Equal(" Horizon Zero Dawn ", row.Name);
        Assert.Equal(" Steam ", row.PlatformLabel);
        Assert.Equal("12345", row.SteamAppId);
        Assert.True(row.SuppressSteamGridDbIdAutoResolve);
        Assert.Equal(9, row.FileCount);
        Assert.Equal(" C:\\Library\\HZD ", row.FolderPath);
        Assert.Equal(" C:\\Library\\HZD\\cover.png ", row.PreviewImagePath);
        Assert.Single(row.FilePaths);
        Assert.Equal(existingFile, row.FilePaths[0]);
    }

    [Fact]
    public void ApplyGameIdAliases_RewritesPhotoIndexRowsAndNotifiesCachedMetadataLayer()
    {
        using var harness = new IndexPersistenceHarness();
        var file = harness.CreateFile("metadata\\capture.png");

        harness.Service.SaveLibraryMetadataIndexEntries(
            harness.LibraryRoot,
            new Dictionary<string, LibraryMetadataIndexEntry>(StringComparer.OrdinalIgnoreCase)
            {
                [file] = new LibraryMetadataIndexEntry
                {
                    FilePath = file,
                    Stamp = "stamp-1",
                    GameId = "g00001",
                    ConsoleLabel = "Steam",
                    TagText = "Steam;Action",
                    CaptureUtcTicks = new DateTime(2026, 3, 30, 12, 34, 56, DateTimeKind.Utc).Ticks
                }
            });

        harness.Service.ApplyGameIdAliases(
            harness.LibraryRoot,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["G00001"] = "G00077"
            });

        var loaded = harness.Service.LoadLibraryMetadataIndexEntries(harness.LibraryRoot);

        var entry = Assert.Single(loaded.Values);
        Assert.Equal("G00077", entry.GameId);
        Assert.Equal("Steam", entry.ConsoleLabel);
        Assert.Equal("Steam;Action", entry.TagText);
        Assert.Equal(new DateTime(2026, 3, 30, 12, 34, 56, DateTimeKind.Utc).Ticks, entry.CaptureUtcTicks);
        Assert.Equal(harness.LibraryRoot, harness.LastAliasApplyRoot);
        Assert.NotNull(harness.LastAliasApplyMap);
        Assert.Equal("G00077", harness.LastAliasApplyMap!["G00001"]);
    }

    [Fact]
    public void SaveLibraryMetadataIndexEntries_IgnoresMissingFilesOnLoad()
    {
        using var harness = new IndexPersistenceHarness();
        var existingFile = harness.CreateFile("metadata\\keep.png");
        var missingFile = Path.Combine(harness.RootPath, "metadata", "gone.png");

        harness.Service.SaveLibraryMetadataIndexEntries(
            harness.LibraryRoot,
            new Dictionary<string, LibraryMetadataIndexEntry>(StringComparer.OrdinalIgnoreCase)
            {
                [existingFile] = new LibraryMetadataIndexEntry
                {
                    FilePath = existingFile,
                    Stamp = "existing",
                    GameId = "G00010",
                    ConsoleLabel = "Steam",
                    TagText = "Steam;Photo",
                    CaptureUtcTicks = 12345
                },
                [missingFile] = new LibraryMetadataIndexEntry
                {
                    FilePath = missingFile,
                    Stamp = "missing",
                    GameId = "G99999",
                    ConsoleLabel = "Other",
                    TagText = "Other",
                    CaptureUtcTicks = 67890
                }
            });

        var loaded = harness.Service.LoadLibraryMetadataIndexEntries(harness.LibraryRoot);

        Assert.Single(loaded);
        Assert.True(loaded.ContainsKey(existingFile));
        Assert.False(loaded.ContainsKey(missingFile));
    }

    [Fact]
    public void LoadSavedGameIndexRows_MigratesLegacyGameIndexFileIntoDatabase()
    {
        using var harness = new IndexPersistenceHarness();
        var existingFile = harness.CreateFile("legacy\\cover.png");
        harness.WriteLegacyGameIndexFile(
            harness.LibraryRoot,
            "G00021\tC:\\Legacy\\Game\tLegacy Game\tSteam\t12345\t67890\t4\tC:\\Legacy\\Game\\cover.png\t" + existingFile);

        var rows = harness.Service.LoadSavedGameIndexRows(harness.LibraryRoot);

        var row = Assert.Single(rows);
        Assert.Equal("G00021", row.GameId);
        Assert.Equal("Legacy Game", row.Name);
        Assert.Equal("Steam", row.PlatformLabel);
        Assert.Equal("12345", row.SteamAppId);
        Assert.Equal("67890", row.SteamGridDbId);
        Assert.Single(row.FilePaths);
        Assert.Equal(existingFile, row.FilePaths[0]);

        var reloaded = harness.Service.LoadSavedGameIndexRows(harness.LibraryRoot);
        Assert.Single(reloaded);
        Assert.Equal("G00021", reloaded[0].GameId);
    }

    [Fact]
    public void LoadLibraryMetadataIndexEntries_MigratesLegacyMetadataFileIntoDatabase()
    {
        using var harness = new IndexPersistenceHarness();
        var existingFile = harness.CreateFile("legacy\\capture.png");
        harness.WriteLegacyMetadataIndexFile(
            harness.LibraryRoot,
            existingFile + "\tstamp-legacy\tG00055\tSteam;Photo");

        var loaded = harness.Service.LoadLibraryMetadataIndexEntries(harness.LibraryRoot);

        var entry = Assert.Single(loaded.Values);
        Assert.Equal(existingFile, entry.FilePath);
        Assert.Equal("stamp-legacy", entry.Stamp);
        Assert.Equal(string.Empty, entry.GameId);
        Assert.Equal("Steam", entry.ConsoleLabel);
        Assert.Equal("Steam;Photo", entry.TagText);
        Assert.Equal(0, entry.CaptureUtcTicks);

        var reloaded = harness.Service.LoadLibraryMetadataIndexEntries(harness.LibraryRoot);
        Assert.Single(reloaded);
        Assert.True(reloaded.ContainsKey(existingFile));
    }

    [Fact]
    public void LoadLibraryMetadataIndexEntries_UpgradesExistingDatabaseWithCaptureTicksColumn()
    {
        using var harness = new IndexPersistenceHarness();
        var existingFile = harness.CreateFile("metadata\\upgrade.png");

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = harness.IndexDatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        };
        using (var connection = new SqliteConnection(builder.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = @"
CREATE TABLE photo_index (
    root TEXT NOT NULL,
    file_path TEXT NOT NULL,
    stamp TEXT NOT NULL DEFAULT '',
    game_id TEXT NOT NULL DEFAULT '',
    console_label TEXT NOT NULL DEFAULT '',
    tag_text TEXT NOT NULL DEFAULT '',
    PRIMARY KEY (root, file_path)
);
INSERT INTO photo_index (root, file_path, stamp, game_id, console_label, tag_text)
VALUES ($root, $filePath, $stamp, $gameId, $consoleLabel, $tagText);";
            command.Parameters.AddWithValue("$root", harness.LibraryRoot);
            command.Parameters.AddWithValue("$filePath", existingFile);
            command.Parameters.AddWithValue("$stamp", "legacy-db-stamp");
            command.Parameters.AddWithValue("$gameId", "G00088");
            command.Parameters.AddWithValue("$consoleLabel", "Steam");
            command.Parameters.AddWithValue("$tagText", "Steam;Photo");
            command.ExecuteNonQuery();
        }

        var loaded = harness.Service.LoadLibraryMetadataIndexEntries(harness.LibraryRoot);

        var entry = Assert.Single(loaded.Values);
        Assert.Equal(existingFile, entry.FilePath);
        Assert.Equal("legacy-db-stamp", entry.Stamp);
        Assert.Equal("G00088", entry.GameId);
        Assert.Equal("Steam", entry.ConsoleLabel);
        Assert.Equal("Steam;Photo", entry.TagText);
        Assert.Equal(0, entry.CaptureUtcTicks);

        using var verifyConnection = new SqliteConnection(builder.ToString());
        verifyConnection.Open();
        using var verifyCommand = verifyConnection.CreateCommand();
        verifyCommand.CommandText = "SELECT COUNT(1) FROM pragma_table_info('photo_index') WHERE name = 'capture_utc_ticks';";
        var columnCount = Convert.ToInt32(verifyCommand.ExecuteScalar());
        Assert.Equal(1, columnCount);
    }

    [Fact]
    public void SaveAndLoadFilenameConventions_RoundTripsCustomRules()
    {
        using var harness = new IndexPersistenceHarness();

        harness.Service.SaveFilenameConventions(
            harness.LibraryRoot,
            new[]
            {
                new FilenameConventionRule
                {
                    ConventionId = "custom_steam_manual",
                    Name = "Steam Manual Override",
                    Enabled = false,
                    Priority = 1200,
                    Pattern = "[yyyy][MM][dd][HH][mm][ss].[ext:image]",
                    PatternText = "[yyyy][MM][dd][HH][mm][ss].[ext:image]",
                    PlatformLabel = "Steam",
                    PlatformTagsText = "Steam",
                    TimestampGroup = "stamp",
                    TimestampFormat = "yyyyMMddHHmmss",
                    RoutesToManualWhenMissingSteamAppId = true,
                    ConfidenceLabel = "CustomOverride"
                }
            });

        var rules = harness.Service.LoadFilenameConventions(harness.LibraryRoot);

        var rule = Assert.Single(rules);
        Assert.Equal("custom_steam_manual", rule.ConventionId);
        Assert.Equal("Steam Manual Override", rule.Name);
        Assert.False(rule.Enabled);
        Assert.Equal(1200, rule.Priority);
        Assert.Equal("Steam", rule.PlatformLabel);
        Assert.Equal("Steam", rule.PlatformTagsText);
        Assert.Equal("[yyyy][MM][dd][HH][mm][ss].[ext:image]", rule.Pattern);
        Assert.Equal("stamp", rule.TimestampGroup);
        Assert.Equal("yyyyMMddHHmmss", rule.TimestampFormat);
        Assert.True(rule.RoutesToManualWhenMissingSteamAppId);
        Assert.Equal("CustomOverride", rule.ConfidenceLabel);
    }

    [Fact]
    public void RecordFilenameConventionSample_AccumulatesOccurrences()
    {
        using var harness = new IndexPersistenceHarness();

        harness.Service.RecordFilenameConventionSample(
            harness.LibraryRoot,
            "20200525124119_1.jpg",
            new FilenameParseResult
            {
                PlatformLabel = "Steam",
                ConventionId = "steam_manual_export"
            });

        harness.Service.RecordFilenameConventionSample(
            harness.LibraryRoot,
            "20200525124119_1.jpg",
            new FilenameParseResult
            {
                PlatformLabel = "Steam",
                ConventionId = "steam_manual_export"
            });

        var samples = harness.Service.LoadFilenameConventionSamples(harness.LibraryRoot, 20);

        var sample = Assert.Single(samples);
        Assert.Equal("20200525124119_1.jpg", sample.FileName);
        Assert.Equal("Steam", sample.SuggestedPlatformLabel);
        Assert.Equal("steam_manual_export", sample.SuggestedConventionId);
        Assert.Equal(2, sample.OccurrenceCount);
        Assert.True(sample.LastSeenUtcTicks >= sample.FirstSeenUtcTicks);
    }

    sealed class IndexPersistenceHarness : IDisposable
    {
        public string RootPath { get; }
        public string CacheRoot { get; }
        public string LibraryRoot { get; }
        public string IndexDatabasePath { get; }
        public IndexPersistenceService Service { get; }
        public string? LastAliasApplyRoot { get; private set; }
        public Dictionary<string, string>? LastAliasApplyMap { get; private set; }

        public IndexPersistenceHarness()
        {
            RootPath = Path.Combine(Path.GetTempPath(), "PixelVault.Native.Tests", Guid.NewGuid().ToString("N"));
            CacheRoot = Path.Combine(RootPath, "cache");
            LibraryRoot = Path.Combine(RootPath, "library-root");
            IndexDatabasePath = Path.Combine(CacheRoot, "pixelvault-index-" + Regex.Replace((LibraryRoot ?? string.Empty).ToLowerInvariant(), @"[^a-z0-9]+", "_").Trim('_') + ".sqlite");
            Directory.CreateDirectory(CacheRoot);
            Directory.CreateDirectory(LibraryRoot);

            Service = new IndexPersistenceService(new IndexPersistenceServiceDependencies
            {
                CacheRoot = CacheRoot,
                SafeCacheName = value => Regex.Replace((value ?? string.Empty).ToLowerInvariant(), @"[^a-z0-9]+", "_").Trim('_'),
                NormalizeGameId = value => (value ?? string.Empty).Trim().ToUpperInvariant(),
                DisplayExternalIdValue = value => value == "<CLEARED>" ? string.Empty : value,
                IsClearedExternalIdValue = value => string.Equals(value, "<CLEARED>", StringComparison.OrdinalIgnoreCase),
                SerializeExternalIdValue = (value, suppressAutoResolve) =>
                    suppressAutoResolve && string.IsNullOrWhiteSpace(value) ? "<CLEARED>" : (value ?? string.Empty).Trim(),
                MergeGameIndexRows = rows => rows.Where(row => row != null).ToList()!,
                BuildGameIdAliasMap = (sourceRows, normalizedRows) =>
                {
                    var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var row in normalizedRows.Where(row => row != null && !string.IsNullOrWhiteSpace(row.GameId)))
                    {
                        map[(row.GameId ?? string.Empty).Trim().ToUpperInvariant()] = (row.GameId ?? string.Empty).Trim().ToUpperInvariant();
                    }
                    foreach (var row in sourceRows.Where(row => row != null && !string.IsNullOrWhiteSpace(row.GameId)))
                    {
                        var normalized = (row.GameId ?? string.Empty).Trim().ToUpperInvariant();
                        if (!map.ContainsKey(normalized)) map[normalized] = normalized;
                    }
                    return map;
                },
                HasGameIdAliasChanges = aliasMap => (aliasMap ?? new Dictionary<string, string>()).Any(pair => !string.Equals(pair.Key, pair.Value, StringComparison.OrdinalIgnoreCase)),
                ParseInt = value => int.TryParse(value, out var parsed) ? parsed : 0,
                ParseTagText = value => (value ?? string.Empty).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(tag => tag.Trim()),
                DetermineConsoleLabelFromTags = tags =>
                {
                    var list = (tags ?? Enumerable.Empty<string>()).Where(tag => !string.IsNullOrWhiteSpace(tag)).ToList();
                    if (list.Any(tag => string.Equals(tag, "Steam", StringComparison.OrdinalIgnoreCase))) return "Steam";
                    if (list.Any(tag => string.Equals(tag, "Xbox", StringComparison.OrdinalIgnoreCase))) return "Xbox";
                    if (list.Any(tag => string.Equals(tag, "PS5", StringComparison.OrdinalIgnoreCase))) return "PS5";
                    return "Other";
                },
                RewriteGameIdAliasesInLibraryFolderCacheFile = (_, _) => { },
                ApplyGameIdAliasesToCachedMetadataIndex = (root, aliasMap) =>
                {
                    LastAliasApplyRoot = root;
                    LastAliasApplyMap = aliasMap == null
                        ? null
                        : new Dictionary<string, string>(aliasMap, StringComparer.OrdinalIgnoreCase);
                }
            });
        }

        public string CreateFile(string relativePath)
        {
            var fullPath = Path.Combine(RootPath, relativePath);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(fullPath, "test");
            return fullPath;
        }

        public void WriteLegacyGameIndexFile(string root, params string[] rows)
        {
            var path = Path.Combine(CacheRoot, "game-index-" + Regex.Replace((root ?? string.Empty).ToLowerInvariant(), @"[^a-z0-9]+", "_").Trim('_') + ".cache");
            File.WriteAllLines(path, new[] { root ?? string.Empty }.Concat(rows ?? Array.Empty<string>()));
        }

        public void WriteLegacyMetadataIndexFile(string root, params string[] rows)
        {
            var path = Path.Combine(CacheRoot, "library-metadata-index-" + Regex.Replace((root ?? string.Empty).ToLowerInvariant(), @"[^a-z0-9]+", "_").Trim('_') + ".cache");
            File.WriteAllLines(path, new[] { root ?? string.Empty }.Concat(rows ?? Array.Empty<string>()));
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
