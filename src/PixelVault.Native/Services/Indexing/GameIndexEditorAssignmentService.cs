using System;
using System.Collections.Generic;
using System.Linq;

namespace PixelVaultNative
{
    /// <summary>
    /// Game index row identity resolution and persistence for import / manual metadata without <see cref="MainWindow"/> lambdas.
    /// </summary>
    internal interface IGameIndexEditorAssignmentService
    {
        GameIndexEditorRow ResolveExistingGameIndexRowForAssignment(IEnumerable<GameIndexEditorRow> rows, string name, string platformLabel, string preferredGameId);

        void SaveSavedGameIndexRows(string root, IEnumerable<GameIndexEditorRow> rows);

        /// <summary>True when the manual-metadata finish flow should treat this name/platform/id triple as not yet represented in <paramref name="rows"/> (prompt to add master records).</summary>
        bool ManualMetadataMasterRecordNeedsNewPlaceholder(IEnumerable<GameIndexEditorRow> rows, string normalizedName, string platformLabel, string preferredGameId);

        /// <summary>Create or return an existing row for manual metadata / Steam intake (mutates <paramref name="rows"/> when creating). Returns null when there is no title hint and no <paramref name="preferredGameId"/> — per <c>PV-PLN-LIBST-001</c> avoid inventing identity.</summary>
        GameIndexEditorRow EnsureManualMetadataMasterRow(List<GameIndexEditorRow> rows, string name, string platformLabel, string preferredGameId);
    }

    internal sealed class GameIndexEditorAssignmentService : IGameIndexEditorAssignmentService
    {
        readonly IIndexPersistenceService persistence;
        readonly IFilenameParserService filenameParser;
        readonly Func<string, string, string> normalizeGameIndexName;
        readonly Func<string, string> normalizeConsoleLabel;
        readonly Func<string, string> normalizeGameId;
        readonly Func<string, string> cleanTag;
        readonly Func<IEnumerable<string>, string> createGameId;
        readonly Func<string, string> foldNormalizedGameTitleForIdentity;

        public GameIndexEditorAssignmentService(
            IIndexPersistenceService persistence,
            IFilenameParserService filenameParser,
            Func<string, string, string> normalizeGameIndexName,
            Func<string, string> normalizeConsoleLabel,
            Func<string, string> normalizeGameId,
            Func<string, string> cleanTag,
            Func<IEnumerable<string>, string> createGameId,
            Func<string, string> foldNormalizedGameTitleForIdentity)
        {
            this.persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
            this.filenameParser = filenameParser;
            this.normalizeGameIndexName = normalizeGameIndexName ?? throw new ArgumentNullException(nameof(normalizeGameIndexName));
            this.normalizeConsoleLabel = normalizeConsoleLabel ?? throw new ArgumentNullException(nameof(normalizeConsoleLabel));
            this.normalizeGameId = normalizeGameId ?? throw new ArgumentNullException(nameof(normalizeGameId));
            this.cleanTag = cleanTag ?? throw new ArgumentNullException(nameof(cleanTag));
            this.createGameId = createGameId ?? throw new ArgumentNullException(nameof(createGameId));
            this.foldNormalizedGameTitleForIdentity = foldNormalizedGameTitleForIdentity ?? throw new ArgumentNullException(nameof(foldNormalizedGameTitleForIdentity));
        }

        public GameIndexEditorRow ResolveExistingGameIndexRowForAssignment(IEnumerable<GameIndexEditorRow> rows, string name, string platformLabel, string preferredGameId)
        {
            var normalizedRows = rows ?? Enumerable.Empty<GameIndexEditorRow>();
            var normalizedGameId = normalizeGameId(preferredGameId ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(normalizedGameId))
            {
                var byId = FindSavedGameIndexRowById(normalizedRows, normalizedGameId);
                if (byId != null) return byId;
            }
            return FindSavedGameIndexRowByIdentity(
                normalizedRows,
                normalizeGameIndexName(name ?? string.Empty, null),
                normalizeConsoleLabel(platformLabel ?? string.Empty));
        }

        public void SaveSavedGameIndexRows(string root, IEnumerable<GameIndexEditorRow> rows)
        {
            var sourceRows = (rows ?? Enumerable.Empty<GameIndexEditorRow>()).Where(row => row != null).Select(CloneGameIndexEditorRow).ToList();
            persistence.SaveSavedGameIndexRows(root, sourceRows);
            filenameParser.InvalidateRules(root);
        }

        public bool ManualMetadataMasterRecordNeedsNewPlaceholder(IEnumerable<GameIndexEditorRow> rows, string normalizedName, string platformLabel, string preferredGameId)
        {
            var rowSet = rows ?? Enumerable.Empty<GameIndexEditorRow>();
            if (FindSavedGameIndexRowByIdentity(rowSet, normalizedName ?? string.Empty, platformLabel ?? string.Empty) != null) return false;
            var normId = normalizeGameId(preferredGameId ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(normId) && FindSavedGameIndexRowById(rowSet, normId) != null) return false;
            return true;
        }

        public GameIndexEditorRow EnsureManualMetadataMasterRow(List<GameIndexEditorRow> rows, string name, string platformLabel, string preferredGameId)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            var normalizedName = normalizeGameIndexName(name ?? string.Empty, null);
            var normalizedPlatform = normalizeConsoleLabel(platformLabel ?? string.Empty);
            var normalizedGameId = normalizeGameId(preferredGameId ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(normalizedGameId))
            {
                var byId = FindSavedGameIndexRowById(rows, normalizedGameId);
                if (byId != null) return byId;
            }
            if (string.IsNullOrWhiteSpace(normalizedName) && string.IsNullOrWhiteSpace(normalizedGameId)) return null;
            var byIdentity = FindSavedGameIndexRowByIdentity(rows, normalizedName, normalizedPlatform);
            if (byIdentity != null) return byIdentity;
            var created = new GameIndexEditorRow
            {
                GameId = !string.IsNullOrWhiteSpace(normalizedGameId) ? normalizedGameId : createGameId(rows.Select(row => row.GameId)),
                Name = normalizedName,
                PlatformLabel = normalizedPlatform,
                SteamAppId = string.Empty,
                NonSteamId = string.Empty,
                SteamGridDbId = string.Empty,
                RetroAchievementsGameId = string.Empty,
                FileCount = 0,
                FolderPath = string.Empty,
                PreviewImagePath = string.Empty,
                FilePaths = new string[0],
                IndexAddedUtcTicks = DateTime.UtcNow.Ticks
            };
            rows.Add(created);
            return created;
        }

        string BuildGameIndexIdentity(string name, string platformLabel)
        {
            var normalized = normalizeGameIndexName(name ?? string.Empty, null);
            return foldNormalizedGameTitleForIdentity(normalized) + "|" + normalizeConsoleLabel(platformLabel ?? string.Empty);
        }

        GameIndexEditorRow FindSavedGameIndexRowById(IEnumerable<GameIndexEditorRow> rows, string gameId)
        {
            var wantedId = normalizeGameId(gameId ?? string.Empty);
            if (string.IsNullOrWhiteSpace(wantedId)) return null;
            return (rows ?? Enumerable.Empty<GameIndexEditorRow>()).FirstOrDefault(row =>
                row != null && string.Equals(normalizeGameId(row.GameId ?? string.Empty), wantedId, StringComparison.OrdinalIgnoreCase));
        }

        GameIndexEditorRow FindSavedGameIndexRowByIdentity(IEnumerable<GameIndexEditorRow> rows, string name, string platformLabel)
        {
            var wantedIdentity = BuildGameIndexIdentity(name, platformLabel);
            return (rows ?? Enumerable.Empty<GameIndexEditorRow>()).FirstOrDefault(row =>
                row != null && string.Equals(BuildGameIndexIdentity(row.Name, row.PlatformLabel), wantedIdentity, StringComparison.OrdinalIgnoreCase));
        }

        GameIndexEditorRow CloneGameIndexEditorRow(GameIndexEditorRow row)
        {
            if (row == null) return null;
            return new GameIndexEditorRow
            {
                GameId = normalizeGameId(row.GameId ?? string.Empty),
                Name = normalizeGameIndexName(row.Name ?? string.Empty, row.FolderPath),
                PlatformLabel = normalizeConsoleLabel(row.PlatformLabel ?? string.Empty),
                SteamAppId = cleanTag(row.SteamAppId ?? string.Empty),
                NonSteamId = cleanTag(row.NonSteamId ?? string.Empty),
                SteamGridDbId = cleanTag(row.SteamGridDbId ?? string.Empty),
                RetroAchievementsGameId = cleanTag(row.RetroAchievementsGameId ?? string.Empty),
                SuppressSteamAppIdAutoResolve = row.SuppressSteamAppIdAutoResolve,
                SuppressSteamGridDbIdAutoResolve = row.SuppressSteamGridDbIdAutoResolve,
                FileCount = Math.Max(0, row.FileCount),
                FolderPath = (row.FolderPath ?? string.Empty).Trim(),
                PreviewImagePath = (row.PreviewImagePath ?? string.Empty).Trim(),
                FilePaths = (row.FilePaths ?? new string[0]).Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                IsCompleted100Percent = row.IsCompleted100Percent,
                CompletedUtcTicks = row.CompletedUtcTicks > 0 ? row.CompletedUtcTicks : 0L,
                IsFavorite = row.IsFavorite,
                IsShowcase = row.IsShowcase,
                CollectionNotes = row.CollectionNotes ?? string.Empty,
                IndexAddedUtcTicks = row.IndexAddedUtcTicks,
                StorageGroupId = row.StorageGroupId ?? string.Empty
            };
        }
    }
}
