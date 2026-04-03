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
    }

    internal sealed class GameIndexEditorAssignmentService : IGameIndexEditorAssignmentService
    {
        readonly IIndexPersistenceService persistence;
        readonly IFilenameParserService filenameParser;
        readonly Func<string, string, string> normalizeGameIndexName;
        readonly Func<string, string> normalizeConsoleLabel;
        readonly Func<string, string> normalizeGameId;
        readonly Func<string, string> cleanTag;

        public GameIndexEditorAssignmentService(
            IIndexPersistenceService persistence,
            IFilenameParserService filenameParser,
            Func<string, string, string> normalizeGameIndexName,
            Func<string, string> normalizeConsoleLabel,
            Func<string, string> normalizeGameId,
            Func<string, string> cleanTag)
        {
            this.persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
            this.filenameParser = filenameParser;
            this.normalizeGameIndexName = normalizeGameIndexName ?? throw new ArgumentNullException(nameof(normalizeGameIndexName));
            this.normalizeConsoleLabel = normalizeConsoleLabel ?? throw new ArgumentNullException(nameof(normalizeConsoleLabel));
            this.normalizeGameId = normalizeGameId ?? throw new ArgumentNullException(nameof(normalizeGameId));
            this.cleanTag = cleanTag ?? throw new ArgumentNullException(nameof(cleanTag));
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

        string BuildGameIndexIdentity(string name, string platformLabel)
        {
            return normalizeGameIndexName(name ?? string.Empty, null) + "|" + normalizeConsoleLabel(platformLabel ?? string.Empty);
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
                SteamGridDbId = cleanTag(row.SteamGridDbId ?? string.Empty),
                SuppressSteamAppIdAutoResolve = row.SuppressSteamAppIdAutoResolve,
                SuppressSteamGridDbIdAutoResolve = row.SuppressSteamGridDbIdAutoResolve,
                FileCount = Math.Max(0, row.FileCount),
                FolderPath = (row.FolderPath ?? string.Empty).Trim(),
                PreviewImagePath = (row.PreviewImagePath ?? string.Empty).Trim(),
                FilePaths = (row.FilePaths ?? new string[0]).Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            };
        }
    }
}
