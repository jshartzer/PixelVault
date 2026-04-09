using System.Collections.Generic;
using System.Linq;
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class GameIndexEditorAssignmentServiceTests
{
    static string CreateSequentialGameId(IEnumerable<string> existing)
    {
        var max = 0;
        foreach (var g in existing ?? Enumerable.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(g)) continue;
            var m = System.Text.RegularExpressions.Regex.Match(g, @"^G(?<n>\d+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (m.Success && int.TryParse(m.Groups["n"].Value, out var n)) max = System.Math.Max(max, n);
        }
        return "G" + (max + 1).ToString("00000");
    }

    static GameIndexEditorAssignmentService CreateService()
    {
        return new GameIndexEditorAssignmentService(
            new StubIndexPersistence(),
            new StubFilenameParser(),
            (name, _) => (name ?? string.Empty).Trim(),
            p => (p ?? string.Empty).Trim(),
            g => (g ?? string.Empty).Trim(),
            s => s ?? string.Empty,
            CreateSequentialGameId,
            t => (t ?? string.Empty).ToLowerInvariant());
    }

    [Fact]
    public void EnsureManualMetadataMasterRow_EmptyNameAndNoGameId_ReturnsNull_DoesNotAddRow()
    {
        var svc = CreateService();
        var rows = new List<GameIndexEditorRow>();
        var result = svc.EnsureManualMetadataMasterRow(rows, "  ", "Steam", null);
        Assert.Null(result);
        Assert.Empty(rows);
    }

    [Fact]
    public void EnsureManualMetadataMasterRow_PreferredGameId_FindsRow_WithoutMatchingNameHint()
    {
        var svc = CreateService();
        var rows = new List<GameIndexEditorRow>
        {
            new()
            {
                GameId = "G00001",
                Name = "Hollow Knight",
                PlatformLabel = "Steam",
                FolderPath = string.Empty
            }
        };
        var result = svc.EnsureManualMetadataMasterRow(rows, string.Empty, "Steam", "G00001");
        Assert.Same(rows[0], result);
        Assert.Single(rows);
    }

    [Fact]
    public void EnsureManualMetadataMasterRow_WithTitle_CreatesRow_WhenMissing()
    {
        var svc = CreateService();
        var rows = new List<GameIndexEditorRow>();
        var result = svc.EnsureManualMetadataMasterRow(rows, " Celeste ", "Steam", null);
        Assert.NotNull(result);
        Assert.Single(rows);
        Assert.Equal("Celeste", result.Name);
        Assert.Equal("Steam", result.PlatformLabel);
        Assert.False(string.IsNullOrWhiteSpace(result.GameId));
    }

    sealed class StubIndexPersistence : IIndexPersistenceService
    {
        public void ApplyGameIdAliases(string root, Dictionary<string, string> aliasMap) { }

        public Dictionary<string, string> BuildSavedGameIdAliasMap(string root) => new();

        public List<FilenameConventionRule> LoadFilenameConventions(string root) => new();

        public List<FilenameConventionSample> LoadFilenameConventionSamples(string root, int maxCount) => new();

        public List<GameIndexEditorRow> LoadSavedGameIndexRows(string root) => new();

        public void RecordFilenameConventionSample(string root, string fileName, FilenameParseResult parseResult) { }

        public void DeleteFilenameConventionSamples(string root, IEnumerable<long> sampleIds) { }

        public void SaveFilenameConventions(string root, IEnumerable<FilenameConventionRule> rules) { }

        public void SaveSavedGameIndexRows(string root, IEnumerable<GameIndexEditorRow> rows) { }

        public Dictionary<string, LibraryMetadataIndexEntry> LoadLibraryMetadataIndexEntries(string root) => new();

        public Dictionary<string, LibraryMetadataIndexEntry> LoadLibraryMetadataIndexEntriesForFilePaths(string root, IEnumerable<string> filePaths) => new();

        public void SaveLibraryMetadataIndexEntries(string root, Dictionary<string, LibraryMetadataIndexEntry> index) { }

        public void UpsertLibraryMetadataIndexEntries(string root, IEnumerable<LibraryMetadataIndexEntry> entries) { }

        public Dictionary<string, string> LoadStarredExportFingerprints(string root, string exportDestinationNormalized) => new();

        public void UpsertStarredExportFingerprint(string root, string exportDestinationNormalized, string sourcePathNormalized, string fingerprint) { }

        public void PruneStarredExportFingerprints(string root, string exportDestinationNormalized, IReadOnlyCollection<string> activeSourcePathsNormalized) { }
    }

    sealed class StubFilenameParser : IFilenameParserService
    {
        public List<FilenameConventionRule> GetConventionRules(string root) => new();

        public void InvalidateRules(string root) { }

        public FilenameParseResult Parse(string file, string root) => new();

        public string GetGameTitleHint(string baseName, string root) => string.Empty;
    }
}
