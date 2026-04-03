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

sealed class StubGameIndexEditorAssignmentService : IGameIndexEditorAssignmentService
{
    public Func<IEnumerable<GameIndexEditorRow>, string, string, string, GameIndexEditorRow> Resolve;
    public Action<string, IEnumerable<GameIndexEditorRow>> Save;
    public Func<IEnumerable<GameIndexEditorRow>, string, string, string, bool> NeedsPlaceholder;
    public Func<List<GameIndexEditorRow>, string, string, string, GameIndexEditorRow> EnsureMaster;

    public GameIndexEditorRow ResolveExistingGameIndexRowForAssignment(IEnumerable<GameIndexEditorRow> rows, string name, string platformLabel, string preferredGameId) =>
        Resolve == null ? null : Resolve(rows, name, platformLabel, preferredGameId);

    public void SaveSavedGameIndexRows(string root, IEnumerable<GameIndexEditorRow> rows) =>
        Save?.Invoke(root, rows);

    public bool ManualMetadataMasterRecordNeedsNewPlaceholder(IEnumerable<GameIndexEditorRow> rows, string normalizedName, string platformLabel, string preferredGameId) =>
        NeedsPlaceholder != null && NeedsPlaceholder(rows, normalizedName, platformLabel, preferredGameId);

    public GameIndexEditorRow EnsureManualMetadataMasterRow(List<GameIndexEditorRow> rows, string name, string platformLabel, string preferredGameId) =>
        EnsureMaster == null ? null : EnsureMaster(rows, name, platformLabel, preferredGameId);
}

public sealed class ImportServiceManualMetadataTests
{
    static ImportService CreateServiceWithManualMetadataDeps(
        ICoverService cover,
        Func<string, string> normalizeGameIndexName,
        StubGameIndexEditorAssignmentService assignment = null,
        Func<ManualMetadataItem, string> platformLabel = null,
        Func<ManualMetadataItem, bool> groupingIdentity = null)
    {
        var stub = assignment ?? new StubGameIndexEditorAssignmentService();
        return new ImportService(new ImportServiceDependencies
        {
            FileSystem = new FileSystemService(),
            MetadataService = new StubMetadataService(),
            GetFileCreationTime = _ => DateTime.MinValue,
            GetFileLastWriteTime = _ => DateTime.MinValue,
            CoverService = cover,
            NormalizeGameIndexName = normalizeGameIndexName ?? (s => s?.Trim() ?? string.Empty),
            GetGameNameFromFileName = fn => Path.GetFileNameWithoutExtension(fn ?? string.Empty),
            DetermineManualMetadataPlatformLabel = platformLabel,
            ManualMetadataChangesGroupingIdentity = groupingIdentity,
            GameIndexEditorAssignment = stub,
            BuildManualMetadataGameTitleChoiceLabel = (name, platform) => (name ?? "") + " | " + (platform ?? ""),
            ParseManualMetadataTagText = s => (s ?? string.Empty)
                .Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            CleanTag = s => string.IsNullOrWhiteSpace(s) ? string.Empty : s.Trim()
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

        var assignmentStub = new StubGameIndexEditorAssignmentService
        {
            Resolve = (all, name, platform, pref) => row,
            Save = (root, list) =>
            {
                savedRoot = root;
                savedSnapshot = list.ToList();
            }
        };
        var svc = CreateServiceWithManualMetadataDeps(
            new StubCoverService(),
            s => s.Trim(),
            assignmentStub,
            _ => "Steam",
            _ => false);

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

    [Fact]
    public void BuildUnresolvedManualMetadataMasterRecordLabels_Returns_Only_Unresolved_And_Sorted()
    {
        var stub = new StubGameIndexEditorAssignmentService
        {
            NeedsPlaceholder = (_, name, _, _) =>
                string.Equals(name, "NewGame", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "Alpha", StringComparison.OrdinalIgnoreCase)
        };
        var svc = CreateServiceWithManualMetadataDeps(
            new StubCoverService(),
            s => s.Trim(),
            stub,
            _ => "Steam",
            _ => false);
        var rows = new List<GameIndexEditorRow>();
        var items = new[]
        {
            new ManualMetadataItem { GameName = "Zeta", FilePath = @"x\z.png", DeleteBeforeProcessing = false },
            new ManualMetadataItem { GameName = "NewGame", FilePath = @"x\a.png", DeleteBeforeProcessing = false },
            new ManualMetadataItem { GameName = "Alpha", FilePath = @"x\al.png", DeleteBeforeProcessing = false },
            new ManualMetadataItem { GameName = "Existing", FilePath = @"x\b.png", DeleteBeforeProcessing = false }
        };
        var labels = svc.BuildUnresolvedManualMetadataMasterRecordLabels(rows, items);
        Assert.Equal(2, labels.Count);
        Assert.Equal("Alpha | Steam", labels[0]);
        Assert.Equal("NewGame | Steam", labels[1]);
    }

    [Fact]
    public void BuildUnresolvedManualMetadataMasterRecordLabels_Skips_DeleteBeforeProcessing()
    {
        var stub = new StubGameIndexEditorAssignmentService { NeedsPlaceholder = (_, _, _, _) => true };
        var svc = CreateServiceWithManualMetadataDeps(
            new StubCoverService(),
            s => s,
            stub,
            _ => "PC",
            _ => false);
        var items = new[]
        {
            new ManualMetadataItem { GameName = "SkipMe", FilePath = @"c\f.png", DeleteBeforeProcessing = true }
        };
        var labels = svc.BuildUnresolvedManualMetadataMasterRecordLabels(new List<GameIndexEditorRow>(), items);
        Assert.Empty(labels);
    }

    [Fact]
    public void EnsureNewManualMetadataMasterRecordsInGameIndex_Calls_Ensure_When_Placeholder_Needed()
    {
        var ensureCalls = 0;
        var stub = new StubGameIndexEditorAssignmentService
        {
            NeedsPlaceholder = (_, _, _, _) => true,
            EnsureMaster = (list, n, p, pref) =>
            {
                ensureCalls++;
                return null;
            }
        };
        var svc = CreateServiceWithManualMetadataDeps(
            new StubCoverService(),
            s => s,
            stub,
            _ => "PC",
            _ => false);
        var rows = new List<GameIndexEditorRow>();
        var items = new[] { new ManualMetadataItem { GameName = "G", FilePath = @"c\f.png" } };
        svc.EnsureNewManualMetadataMasterRecordsInGameIndex(rows, items);
        Assert.Equal(1, ensureCalls);
    }

    [Fact]
    public void ApplyManualMetadataTagTextToPlatformFlags_Sets_Flags_From_First_Platform_Token()
    {
        var svc = CreateServiceWithManualMetadataDeps(new StubCoverService(), s => s);
        var item = new ManualMetadataItem
        {
            TagText = "foo, Steam, PC",
            TagSteam = false,
            TagPc = true,
            TagOther = true,
            CustomPlatformTag = "x",
            ForceTagMetadataWrite = false
        };
        svc.ApplyManualMetadataTagTextToPlatformFlags(new[] { item });
        Assert.True(item.TagSteam);
        Assert.False(item.TagPc);
        Assert.False(item.TagOther);
        Assert.Equal(string.Empty, item.CustomPlatformTag);
        Assert.True(item.ForceTagMetadataWrite);
    }

    [Fact]
    public void ApplyManualMetadataTagTextToPlatformFlags_PlayStation_Maps_To_PS5()
    {
        var svc = CreateServiceWithManualMetadataDeps(new StubCoverService(), s => s);
        var item = new ManualMetadataItem { TagText = "PlayStation" };
        svc.ApplyManualMetadataTagTextToPlatformFlags(new[] { item });
        Assert.True(item.TagPs5);
        Assert.False(item.TagSteam);
    }

    [Fact]
    public void ManualMetadataItemsMissingOtherPlatformName_True_When_Other_And_Blank_Custom()
    {
        var svc = CreateServiceWithManualMetadataDeps(new StubCoverService(), s => s);
        var bad = new ManualMetadataItem { TagOther = true, CustomPlatformTag = "   " };
        var ok = new ManualMetadataItem { TagOther = true, CustomPlatformTag = "Switch" };
        Assert.True(svc.ManualMetadataItemsMissingOtherPlatformName(new[] { bad }));
        Assert.False(svc.ManualMetadataItemsMissingOtherPlatformName(new[] { ok }));
    }

    [Fact]
    public void GetManualMetadataFinishEmptySelectionMessage_Uses_Mode()
    {
        var svc = CreateServiceWithManualMetadataDeps(new StubCoverService(), s => s);
        Assert.Contains("library", svc.GetManualMetadataFinishEmptySelectionMessage(true, false), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("upload", svc.GetManualMetadataFinishEmptySelectionMessage(false, true), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("unmatched", svc.GetManualMetadataFinishEmptySelectionMessage(false, false), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetManualMetadataFinishConfirmBody_Includes_Count_And_Mode_Phrase()
    {
        var svc = CreateServiceWithManualMetadataDeps(new StubCoverService(), s => s);
        Assert.Contains("3 selected image", svc.GetManualMetadataFinishConfirmBody(3, true, false));
        Assert.Contains("Apply changes now", svc.GetManualMetadataFinishConfirmBody(3, true, false));
        Assert.Contains("Continue", svc.GetManualMetadataFinishConfirmBody(2, false, true));
        Assert.Contains("send them now", svc.GetManualMetadataFinishConfirmBody(1, false, false), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildManualMetadataAddNewGamePrompt_Singular_And_Preview()
    {
        var svc = CreateServiceWithManualMetadataDeps(new StubCoverService(), s => s);
        var msg = svc.BuildManualMetadataAddNewGamePrompt(new[] { "A | Steam" }, 8);
        Assert.Contains("record is not", msg, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("- A | Steam", msg);
        Assert.Contains("Add it as", msg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildManualMetadataAddNewGamePrompt_Plural_And_Truncates_Preview()
    {
        var svc = CreateServiceWithManualMetadataDeps(new StubCoverService(), s => s);
        var labels = Enumerable.Range(0, 10).Select(i => "G" + i).ToList();
        var msg = svc.BuildManualMetadataAddNewGamePrompt(labels, 2);
        Assert.Contains("records are not", msg, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("- G0", msg);
        Assert.Contains("- G1", msg);
        Assert.Contains("- ...", msg);
        Assert.DoesNotContain("G2", msg);
    }

    [Fact]
    public void BuildManualMetadataAddNewGamePrompt_Throws_When_Empty()
    {
        var svc = CreateServiceWithManualMetadataDeps(new StubCoverService(), s => s);
        Assert.Throws<ArgumentException>(() => svc.BuildManualMetadataAddNewGamePrompt(Array.Empty<string>()));
    }
}
