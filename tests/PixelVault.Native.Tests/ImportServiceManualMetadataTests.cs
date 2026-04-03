#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace PixelVaultNative.Tests;

/// <summary>Minimal <see cref="IMetadataService"/> so <see cref="ImportService"/> can be constructed for focused tests.</summary>
sealed class StubMetadataService : IMetadataService
{
    public string[] BuildExifArgs(string file, DateTime dt, string[] platformTags, bool preserveFileTimes, string comment, bool addPhotographyTag) => Array.Empty<string>();

    public string[] BuildExifArgs(string file, DateTime dt, string[] platformTags, IEnumerable<string> extraTags, bool preserveFileTimes, string comment, bool addPhotographyTag) => Array.Empty<string>();

    public string[] BuildExifArgs(string file, DateTime dt, string[] platformTags, IEnumerable<string> extraTags, bool preserveFileTimes, string comment, bool addPhotographyTag, bool writeDateMetadata, bool writeCommentMetadata, bool writeTagMetadata) => Array.Empty<string>();

    public string[] ReadEmbeddedKeywordTagsDirect(string file, CancellationToken cancellationToken = default) => Array.Empty<string>();

    public string ReadEmbeddedCommentDirect(string file, CancellationToken cancellationToken = default) => string.Empty;

    public DateTime? ReadEmbeddedCaptureDateDirect(string file, CancellationToken cancellationToken = default) => null;

    public Dictionary<string, string[]> ReadEmbeddedKeywordTagsBatch(IEnumerable<string> files, CancellationToken cancellationToken = default) => new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, EmbeddedMetadataSnapshot> ReadEmbeddedMetadataBatch(IEnumerable<string> files, CancellationToken cancellationToken = default) => new Dictionary<string, EmbeddedMetadataSnapshot>(StringComparer.OrdinalIgnoreCase);

    public Task<Dictionary<string, string[]>> ReadEmbeddedKeywordTagsBatchAsync(IEnumerable<string> files, CancellationToken cancellationToken = default) => Task.FromResult(ReadEmbeddedKeywordTagsBatch(files, cancellationToken));

    public Task<Dictionary<string, EmbeddedMetadataSnapshot>> ReadEmbeddedMetadataBatchAsync(IEnumerable<string> files, CancellationToken cancellationToken = default) => Task.FromResult(ReadEmbeddedMetadataBatch(files, cancellationToken));

    public void EnsureExifTool() { }

    public void RunExifToolBatch(IReadOnlyList<ExifWriteRequest> requests) { }

    public int RunExifWriteRequests(List<ExifWriteRequest> requests, int totalCount, int alreadyCompleted, Action<int, int, string> progress = null, CancellationToken cancellationToken = default(CancellationToken)) => 0;
}

/// <summary><see cref="ICoverService.SteamNameAsync"/> returns a fixed title for app id <c>123</c>; other members are inert.</summary>
sealed class StubCoverService : ICoverService
{
    public Task<string> SteamNameAsync(string appId, CancellationToken cancellationToken = default) =>
        Task.FromResult(string.Equals(appId, "123", StringComparison.Ordinal) ? "Resolved Store Title" : string.Empty);

    public string TryResolveSteamGridDbIdBySteamAppId(string steamAppId, CancellationToken cancellationToken = default) => null;

    public string TryResolveSteamGridDbIdByName(string title, CancellationToken cancellationToken = default) => null;

    public List<Tuple<string, string>> SearchSteamAppMatches(string title, CancellationToken cancellationToken = default) => new List<Tuple<string, string>>();

    public string TryResolveSteamAppId(string title, CancellationToken cancellationToken = default) => null;

    public string SteamName(string appId, CancellationToken cancellationToken = default) => null;

    public Task<string> TryResolveSteamGridDbIdBySteamAppIdAsync(string steamAppId, CancellationToken cancellationToken = default) => Task.FromResult<string>(null);

    public Task<string> TryResolveSteamGridDbIdByNameAsync(string title, CancellationToken cancellationToken = default) => Task.FromResult<string>(null);

    public Task<List<Tuple<string, string>>> SearchSteamAppMatchesAsync(string title, CancellationToken cancellationToken = default) => Task.FromResult(new List<Tuple<string, string>>());

    public Task<string> TryResolveSteamAppIdAsync(string title, CancellationToken cancellationToken = default) => Task.FromResult<string>(null);

    public string CustomCoverPath(LibraryFolderInfo folder) => null;

    public void SaveCustomCover(LibraryFolderInfo folder, string sourcePath) { }

    public void ClearCustomCover(LibraryFolderInfo folder) { }

    public string CachedCoverPath(string title) => null;

    public void DeleteCachedCover(string title) { }

    public bool HasDedicatedLibraryCover(LibraryFolderInfo folder) => false;

    public string TryDownloadSteamCover(string title, string appId, CancellationToken cancellationToken = default) => null;

    public string TryDownloadSteamGridDbCover(string title, string steamGridDbId, CancellationToken cancellationToken = default) => null;

    public Task<string> TryDownloadSteamCoverAsync(string title, string appId, CancellationToken cancellationToken = default) => Task.FromResult<string>(null);

    public Task<string> TryDownloadSteamGridDbCoverAsync(string title, string steamGridDbId, CancellationToken cancellationToken = default) => Task.FromResult<string>(null);
}

public sealed class ImportServiceManualMetadataTests
{
    static ImportService CreateServiceWithManualMetadataDeps(
        ICoverService cover,
        Func<string, string> normalizeGameIndexName,
        Func<IEnumerable<GameIndexEditorRow>, string, string, string, GameIndexEditorRow> resolveExisting = null,
        Func<ManualMetadataItem, string> platformLabel = null,
        Func<ManualMetadataItem, bool> groupingIdentity = null,
        Action<string, IEnumerable<GameIndexEditorRow>> saveRows = null)
    {
        return new ImportService(new ImportServiceDependencies
        {
            FileSystem = new FileSystemService(),
            MetadataService = new StubMetadataService(),
            GetFileCreationTime = _ => DateTime.MinValue,
            GetFileLastWriteTime = _ => DateTime.MinValue,
            CoverService = cover,
            NormalizeGameIndexName = normalizeGameIndexName ?? (s => s?.Trim() ?? string.Empty),
            GetGameNameFromFileName = fn => Path.GetFileNameWithoutExtension(fn ?? string.Empty),
            ResolveExistingGameIndexRowForAssignment = resolveExisting,
            DetermineManualMetadataPlatformLabel = platformLabel,
            ManualMetadataChangesGroupingIdentity = groupingIdentity,
            SaveSavedGameIndexRows = saveRows
        });
    }

    [Fact]
    public async Task ApplyImportAndEditSteamStoreTitlesWhenGameNameUnchangedAsync_Updates_Title_When_Steam_Tag_And_Name_Unchanged()
    {
        var svc = CreateServiceWithManualMetadataDeps(new StubCoverService(), s => s.Trim());
        var item = new ManualMetadataItem
        {
            TagSteam = true,
            GameName = "  hint  ",
            OriginalGameName = "  hint  ",
            SteamAppId = "app_123_x"
        };
        await svc.ApplyImportAndEditSteamStoreTitlesWhenGameNameUnchangedAsync(new[] { item }, CancellationToken.None);
        Assert.Equal("Resolved Store Title", item.GameName);
    }

    [Fact]
    public async Task ApplyImportAndEditSteamStoreTitlesWhenGameNameUnchangedAsync_Skips_When_User_Changed_Title()
    {
        var svc = CreateServiceWithManualMetadataDeps(new StubCoverService(), s => s.Trim());
        var item = new ManualMetadataItem
        {
            TagSteam = true,
            GameName = "My Custom Title",
            OriginalGameName = "hint",
            SteamAppId = "123"
        };
        await svc.ApplyImportAndEditSteamStoreTitlesWhenGameNameUnchangedAsync(new[] { item }, CancellationToken.None);
        Assert.Equal("My Custom Title", item.GameName);
    }

    [Fact]
    public void FinalizeManualMetadataItemsAgainstGameIndex_Assigns_GameId_And_Pushes_SteamAppId_To_Row()
    {
        var row = new GameIndexEditorRow
        {
            GameId = "G1",
            Name = "Canonical Name",
            PlatformLabel = "Steam",
            SteamAppId = string.Empty
        };
        var rows = new List<GameIndexEditorRow> { row };
        string savedRoot = null;
        IEnumerable<GameIndexEditorRow> savedSnapshot = null;

        var svc = CreateServiceWithManualMetadataDeps(
            new StubCoverService(),
            s => s.Trim(),
            (all, name, platform, pref) => row,
            _ => "Steam",
            _ => false,
            (root, list) =>
            {
                savedRoot = root;
                savedSnapshot = list.ToList();
            });

        var item = new ManualMetadataItem
        {
            GameName = "Canonical Name",
            FilePath = @"D:\captures\shot.png",
            SteamAppId = "999",
            GameId = string.Empty,
            DeleteBeforeProcessing = false
        };

        svc.FinalizeManualMetadataItemsAgainstGameIndex(@"D:\lib", rows, new[] { item });

        Assert.Equal(@"D:\lib", savedRoot);
        Assert.NotNull(savedSnapshot);
        Assert.Equal("G1", item.GameId);
        Assert.Equal("Canonical Name", item.GameName);
        Assert.Equal("999", row.SteamAppId);
        Assert.False(row.SuppressSteamAppIdAutoResolve);
    }
}
