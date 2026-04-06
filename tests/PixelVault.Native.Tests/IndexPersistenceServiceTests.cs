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
    public void SaveAndLoadSavedGameIndexRows_RoundTripsIndexAddedUtcTicks()
    {
        using var harness = new IndexPersistenceHarness();
        var existingFile = harness.CreateFile("library\\cover.png");
        var addedTicks = new DateTime(2024, 6, 15, 14, 30, 0, DateTimeKind.Utc).Ticks;

        harness.Service.SaveSavedGameIndexRows(
            harness.LibraryRoot,
            new[]
            {
                new GameIndexEditorRow
                {
                    GameId = "G00099",
                    Name = "Test Game",
                    PlatformLabel = "Steam",
                    SteamAppId = string.Empty,
                    SteamGridDbId = string.Empty,
                    FileCount = 1,
                    FolderPath = Path.Combine(harness.RootPath, "library"),
                    PreviewImagePath = existingFile,
                    FilePaths = new[] { existingFile },
                    IndexAddedUtcTicks = addedTicks
                }
            });

        var rows = harness.Service.LoadSavedGameIndexRows(harness.LibraryRoot);
        var row = Assert.Single(rows);
        Assert.Equal(addedTicks, row.IndexAddedUtcTicks);
    }

    [Fact]
    public void SaveAndLoadSavedGameIndexRows_RoundTripsCollectionMetadata()
    {
        using var harness = new IndexPersistenceHarness();
        var existingFile = harness.CreateFile("library\\collection-cover.png");
        var completedTicks = new DateTime(2025, 11, 3, 18, 45, 0, DateTimeKind.Utc).Ticks;

        harness.Service.SaveSavedGameIndexRows(
            harness.LibraryRoot,
            new[]
            {
                new GameIndexEditorRow
                {
                    GameId = "G00125",
                    Name = "Control",
                    PlatformLabel = "Steam",
                    SteamAppId = string.Empty,
                    SteamGridDbId = string.Empty,
                    FileCount = 1,
                    FolderPath = Path.Combine(harness.RootPath, "library"),
                    PreviewImagePath = existingFile,
                    FilePaths = new[] { existingFile },
                    IsCompleted100Percent = true,
                    CompletedUtcTicks = completedTicks,
                    IsFavorite = true,
                    IsShowcase = true,
                    CollectionNotes = "Foundation complete; ready for showcase shelf."
                }
            });

        var rows = harness.Service.LoadSavedGameIndexRows(harness.LibraryRoot);

        var row = Assert.Single(rows);
        Assert.True(row.IsCompleted100Percent);
        Assert.Equal(completedTicks, row.CompletedUtcTicks);
        Assert.True(row.IsFavorite);
        Assert.True(row.IsShowcase);
        Assert.Equal("Foundation complete; ready for showcase shelf.", row.CollectionNotes);
    }

    [Fact]
    public void LoadSavedGameIndexRows_BackfillsExternalIdsFromPriorRootDatabase()
    {
        using var harness = new IndexPersistenceHarness();

        var priorRoot = Path.Combine(harness.RootPath, "legacy-library-root");
        Directory.CreateDirectory(priorRoot);

        harness.Service.SaveSavedGameIndexRows(
            priorRoot,
            new[]
            {
                new GameIndexEditorRow
                {
                    GameId = "G00999",
                    Name = "Alan Wake",
                    PlatformLabel = "Steam",
                    SteamAppId = "108710",
                    SteamGridDbId = "9991",
                    FileCount = 12,
                    FolderPath = "E:\\Game Captures\\Alan Wake",
                    FilePaths = Array.Empty<string>()
                }
            });

        harness.Service.SaveSavedGameIndexRows(
            harness.LibraryRoot,
            new[]
            {
                new GameIndexEditorRow
                {
                    GameId = "G00001",
                    Name = "Alan Wake",
                    PlatformLabel = "Steam",
                    SteamAppId = string.Empty,
                    SteamGridDbId = string.Empty,
                    FileCount = 12,
                    FolderPath = "E:\\Game Captures\\Alan Wake",
                    FilePaths = Array.Empty<string>()
                }
            });

        var rows = harness.Service.LoadSavedGameIndexRows(harness.LibraryRoot);

        var row = Assert.Single(rows);
        Assert.Equal("G00001", row.GameId);
        Assert.Equal("108710", row.SteamAppId);
        Assert.Equal("9991", row.SteamGridDbId);
        Assert.Equal("E:\\Game Captures\\Alan Wake", row.FolderPath);
    }

    [Fact]
    public void LoadSavedGameIndexRows_BackfillSucceedsWhenDonorIndexLacksIndexAddedUtcTicksColumn()
    {
        using var harness = new IndexPersistenceHarness();
        var priorRoot = Path.Combine(harness.RootPath, "prior-lib");
        Directory.CreateDirectory(priorRoot);
        var donorPath = Path.Combine(
            harness.CacheRoot,
            "pixelvault-index-" + Regex.Replace(priorRoot.ToLowerInvariant(), @"[^a-z0-9]+", "_").Trim('_') + ".sqlite");

        var builder = new SqliteConnectionStringBuilder { DataSource = donorPath, Mode = SqliteOpenMode.ReadWriteCreate };
        using (var conn = new SqliteConnection(builder.ToString()))
        {
            conn.Open();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
CREATE TABLE game_index (
    root TEXT NOT NULL,
    game_id TEXT NOT NULL,
    folder_path TEXT NOT NULL DEFAULT '',
    name TEXT NOT NULL DEFAULT '',
    platform_label TEXT NOT NULL DEFAULT '',
    steam_app_id TEXT NOT NULL DEFAULT '',
    steam_grid_db_id TEXT NOT NULL DEFAULT '',
    file_count INTEGER NOT NULL DEFAULT 0,
    preview_image_path TEXT NOT NULL DEFAULT '',
    file_paths TEXT NOT NULL DEFAULT '',
    PRIMARY KEY (root, game_id)
);
INSERT INTO game_index (root, game_id, folder_path, name, platform_label, steam_app_id, steam_grid_db_id, file_count, preview_image_path, file_paths)
VALUES ($root, 'G00999', 'E:/Game Captures/Alan Wake', 'Alan Wake', 'Steam', '108710', '9991', 12, '', '');";
                cmd.Parameters.AddWithValue("$root", priorRoot);
                cmd.ExecuteNonQuery();
            }
        }

        harness.Service.SaveSavedGameIndexRows(
            harness.LibraryRoot,
            new[]
            {
                new GameIndexEditorRow
                {
                    GameId = "G00001",
                    Name = "Alan Wake",
                    PlatformLabel = "Steam",
                    SteamAppId = string.Empty,
                    SteamGridDbId = string.Empty,
                    FileCount = 12,
                    FolderPath = "E:/Game Captures/Alan Wake",
                    FilePaths = Array.Empty<string>()
                }
            });

        var rows = harness.Service.LoadSavedGameIndexRows(harness.LibraryRoot);

        var row = Assert.Single(rows);
        Assert.Equal("G00001", row.GameId);
        Assert.Equal("108710", row.SteamAppId);
        Assert.Equal("9991", row.SteamGridDbId);
    }

    [Fact]
    public void LoadSavedGameIndexRows_UpgradesExistingDatabaseWithCollectionMetadataColumns()
    {
        using var harness = new IndexPersistenceHarness();
        var existingFile = harness.CreateFile("library\\upgrade-cover.png");

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
CREATE TABLE game_index (
    root TEXT NOT NULL,
    game_id TEXT NOT NULL,
    folder_path TEXT NOT NULL DEFAULT '',
    name TEXT NOT NULL DEFAULT '',
    platform_label TEXT NOT NULL DEFAULT '',
    steam_app_id TEXT NOT NULL DEFAULT '',
    steam_grid_db_id TEXT NOT NULL DEFAULT '',
    file_count INTEGER NOT NULL DEFAULT 0,
    preview_image_path TEXT NOT NULL DEFAULT '',
    file_paths TEXT NOT NULL DEFAULT '',
    index_added_utc_ticks INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (root, game_id)
);
INSERT INTO game_index (root, game_id, folder_path, name, platform_label, steam_app_id, steam_grid_db_id, file_count, preview_image_path, file_paths, index_added_utc_ticks)
VALUES ($root, 'G00045', 'E:/Game Captures/Control', 'Control', 'Steam', '', '', 1, $previewImagePath, $filePaths, 0);";
            command.Parameters.AddWithValue("$root", harness.LibraryRoot);
            command.Parameters.AddWithValue("$previewImagePath", existingFile);
            command.Parameters.AddWithValue("$filePaths", existingFile);
            command.ExecuteNonQuery();
        }

        var loaded = harness.Service.LoadSavedGameIndexRows(harness.LibraryRoot);

        var row = Assert.Single(loaded);
        Assert.Equal("G00045", row.GameId);
        Assert.False(row.IsCompleted100Percent);
        Assert.Equal(0L, row.CompletedUtcTicks);
        Assert.False(row.IsFavorite);
        Assert.False(row.IsShowcase);
        Assert.Equal(string.Empty, row.CollectionNotes);

        using var verifyConnection = new SqliteConnection(builder.ToString());
        verifyConnection.Open();
        using var verifyCommand = verifyConnection.CreateCommand();
        verifyCommand.CommandText = "SELECT COUNT(1) FROM pragma_table_info('game_index') WHERE name = 'is_completed_100_percent';";
        Assert.Equal(1, Convert.ToInt32(verifyCommand.ExecuteScalar()));
        verifyCommand.CommandText = "SELECT COUNT(1) FROM pragma_table_info('game_index') WHERE name = 'completed_utc_ticks';";
        Assert.Equal(1, Convert.ToInt32(verifyCommand.ExecuteScalar()));
        verifyCommand.CommandText = "SELECT COUNT(1) FROM pragma_table_info('game_index') WHERE name = 'is_favorite';";
        Assert.Equal(1, Convert.ToInt32(verifyCommand.ExecuteScalar()));
        verifyCommand.CommandText = "SELECT COUNT(1) FROM pragma_table_info('game_index') WHERE name = 'is_showcase';";
        Assert.Equal(1, Convert.ToInt32(verifyCommand.ExecuteScalar()));
        verifyCommand.CommandText = "SELECT COUNT(1) FROM pragma_table_info('game_index') WHERE name = 'collection_notes';";
        Assert.Equal(1, Convert.ToInt32(verifyCommand.ExecuteScalar()));
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
        Assert.Equal(0L, entry.IndexAddedUtcTicks);
        Assert.Equal(harness.LibraryRoot, harness.LastAliasApplyRoot);
        Assert.NotNull(harness.LastAliasApplyMap);
        Assert.Equal("G00077", harness.LastAliasApplyMap!["G00001"]);
    }

    [Fact]
    public void SaveLibraryMetadataIndexEntries_RetainsMissingFilePathsOnLoad()
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

        Assert.Equal(2, loaded.Count);
        Assert.True(loaded.ContainsKey(existingFile));
        Assert.True(loaded.ContainsKey(missingFile));
        Assert.Equal("G00010", loaded[existingFile].GameId);
        Assert.Equal("G99999", loaded[missingFile].GameId);
    }

    [Fact]
    public void SaveLibraryMetadataIndexEntries_RoundTripsIndexAddedUtcTicks()
    {
        using var harness = new IndexPersistenceHarness();
        var file = harness.CreateFile("metadata\\indexed.png");
        var addedTicks = new DateTime(2025, 1, 20, 8, 15, 0, DateTimeKind.Utc).Ticks;

        harness.Service.SaveLibraryMetadataIndexEntries(
            harness.LibraryRoot,
            new Dictionary<string, LibraryMetadataIndexEntry>(StringComparer.OrdinalIgnoreCase)
            {
                [file] = new LibraryMetadataIndexEntry
                {
                    FilePath = file,
                    Stamp = "s1",
                    GameId = "G00001",
                    ConsoleLabel = "Steam",
                    TagText = "Steam",
                    CaptureUtcTicks = 100,
                    Starred = false,
                    IndexAddedUtcTicks = addedTicks
                }
            });

        var loaded = harness.Service.LoadLibraryMetadataIndexEntries(harness.LibraryRoot);
        var entry = Assert.Single(loaded.Values);
        Assert.Equal(addedTicks, entry.IndexAddedUtcTicks);
    }

    [Fact]
    public void LoadLibraryMetadataIndexEntriesForFilePaths_ReturnsOnlyRequestedRows()
    {
        using var harness = new IndexPersistenceHarness();
        var fileA = harness.CreateFile("metadata\\a.png");
        var fileB = harness.CreateFile("metadata\\b.png");
        var fileC = harness.CreateFile("metadata\\c.png");

        harness.Service.SaveLibraryMetadataIndexEntries(
            harness.LibraryRoot,
            new Dictionary<string, LibraryMetadataIndexEntry>(StringComparer.OrdinalIgnoreCase)
            {
                [fileA] = new LibraryMetadataIndexEntry { FilePath = fileA, Stamp = "sa", GameId = "G1", ConsoleLabel = "Steam", TagText = "Steam", CaptureUtcTicks = 10 },
                [fileB] = new LibraryMetadataIndexEntry { FilePath = fileB, Stamp = "sb", GameId = "G2", ConsoleLabel = "Steam", TagText = "Steam", CaptureUtcTicks = 20 },
                [fileC] = new LibraryMetadataIndexEntry { FilePath = fileC, Stamp = "sc", GameId = "G3", ConsoleLabel = "Steam", TagText = "Steam", CaptureUtcTicks = 30 }
            });

        var subset = harness.Service.LoadLibraryMetadataIndexEntriesForFilePaths(harness.LibraryRoot, new[] { fileB, fileA, Path.Combine(harness.RootPath, "metadata", "missing.png") });

        Assert.Equal(2, subset.Count);
        Assert.Equal("sa", subset[fileA].Stamp);
        Assert.Equal("sb", subset[fileB].Stamp);
        Assert.False(subset.ContainsKey(fileC));
    }

    [Fact]
    public void UpsertLibraryMetadataIndexEntries_MergesWithoutDeletingOtherFiles()
    {
        using var harness = new IndexPersistenceHarness();
        var fileA = harness.CreateFile("metadata\\upsert-a.png");
        var fileB = harness.CreateFile("metadata\\upsert-b.png");

        harness.Service.SaveLibraryMetadataIndexEntries(
            harness.LibraryRoot,
            new Dictionary<string, LibraryMetadataIndexEntry>(StringComparer.OrdinalIgnoreCase)
            {
                [fileA] = new LibraryMetadataIndexEntry { FilePath = fileA, Stamp = "oldA", GameId = "G9", ConsoleLabel = "Steam", TagText = "Steam", CaptureUtcTicks = 1 },
                [fileB] = new LibraryMetadataIndexEntry { FilePath = fileB, Stamp = "keepB", GameId = "G8", ConsoleLabel = "Steam", TagText = "Steam", CaptureUtcTicks = 2 }
            });

        harness.Service.UpsertLibraryMetadataIndexEntries(
            harness.LibraryRoot,
            new[]
            {
                new LibraryMetadataIndexEntry { FilePath = fileA, Stamp = "newA", GameId = "G9", ConsoleLabel = "Steam", TagText = "Steam", CaptureUtcTicks = 100 }
            });

        var loaded = harness.Service.LoadLibraryMetadataIndexEntries(harness.LibraryRoot);
        Assert.Equal(2, loaded.Count);
        Assert.Equal("newA", loaded[fileA].Stamp);
        Assert.Equal(100L, loaded[fileA].CaptureUtcTicks);
        Assert.Equal("keepB", loaded[fileB].Stamp);
        Assert.Equal(2L, loaded[fileB].CaptureUtcTicks);
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
        verifyCommand.CommandText = "SELECT COUNT(1) FROM pragma_table_info('photo_index') WHERE name = 'index_added_utc_ticks';";
        Assert.Equal(1, Convert.ToInt32(verifyCommand.ExecuteScalar()));
    }

    [Fact]
    public void LoadLibraryMetadataIndexEntries_PreservesStoredConsoleLabelWhenTagsAreSparse()
    {
        using var harness = new IndexPersistenceHarness();
        var existingFile = harness.CreateFile("metadata\\steam-without-tag.png");

        harness.Service.SaveLibraryMetadataIndexEntries(
            harness.LibraryRoot,
            new Dictionary<string, LibraryMetadataIndexEntry>(StringComparer.OrdinalIgnoreCase)
            {
                [existingFile] = new LibraryMetadataIndexEntry
                {
                    FilePath = existingFile,
                    Stamp = "steam-stamp",
                    GameId = "G00091",
                    ConsoleLabel = "Steam",
                    TagText = "Action;Portrait",
                    CaptureUtcTicks = 0
                }
            });

        var loaded = harness.Service.LoadLibraryMetadataIndexEntries(harness.LibraryRoot);

        var entry = Assert.Single(loaded.Values);
        Assert.Equal("G00091", entry.GameId);
        Assert.Equal("Steam", entry.ConsoleLabel);
        Assert.Equal("Action;Portrait", entry.TagText);
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
        public string RootPath { get; } = string.Empty;
        public string CacheRoot { get; } = string.Empty;
        public string LibraryRoot { get; } = string.Empty;
        public string IndexDatabasePath { get; } = string.Empty;
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
            Directory.CreateDirectory(LibraryRoot!);

            Service = new IndexPersistenceService(new IndexPersistenceServiceDependencies
            {
                CacheRoot = CacheRoot,
                SafeCacheName = value => Regex.Replace((value ?? string.Empty).ToLowerInvariant(), @"[^a-z0-9]+", "_").Trim('_'),
                NormalizeGameId = value => (value ?? string.Empty).Trim().ToUpperInvariant(),
                NormalizeGameIndexName = value => (value ?? string.Empty).Trim(),
                NormalizeConsoleLabel = value => (value ?? string.Empty).Trim(),
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
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory!);
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
