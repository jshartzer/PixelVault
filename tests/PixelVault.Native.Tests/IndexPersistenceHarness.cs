using System.Text.RegularExpressions;
using PixelVaultNative;
using SQLitePCL;

namespace PixelVaultNative.Tests;

/// <summary>Temp cache root + library root + real <see cref="IndexPersistenceService"/> for tests.</summary>
internal sealed class IndexPersistenceHarness : IDisposable
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
        Batteries_V2.Init();
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
