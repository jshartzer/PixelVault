using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace PixelVaultNative
{
    sealed class AchievementGuideEntry
    {
        public long AchievementId;
        public string Provider;
        public string ProviderGameId;
        public string ProviderAchievementId;
        public string PixelVaultGameId;
        public string Title;
        public string Description;
        public string IconUrl;
        public int DisplayOrder;
        public bool IsHidden;
        public bool IsActive;
        public string GuideText;
        public string SourceUrl;
        public string SourceTitle;
        public string Tags;
        public bool IsMissable;
        public long GuideUpdatedUtcTicks;
    }

    sealed class AchievementGuideEdit
    {
        public long AchievementId;
        public string GuideText;
        public string SourceUrl;
        public string SourceTitle;
        public string Tags;
        public bool IsMissable;
    }

    interface IAchievementGuideService
    {
        IReadOnlyList<AchievementGuideEntry> SyncDefinitionsAndLoadGuides(
            string pixelVaultGameId,
            IEnumerable<GameAchievementsFetchService.AchievementRow> rows);

        AchievementGuideEntry SaveGuide(AchievementGuideEdit edit);

        AchievementGuideImportPreview PreviewGuideImport(
            string json,
            string expectedProvider,
            string expectedProviderGameId);

        AchievementGuideImportResult ImportGuideBundle(
            string json,
            string expectedProvider,
            string expectedProviderGameId);
    }

    /// <summary>
    /// Durable achievement catalog and authored-guide store for PV-PLN-ACHG-001.
    /// This database lives beneath PixelVaultData/guides, never the rebuildable per-library cache store.
    /// </summary>
    sealed partial class AchievementGuideService : IAchievementGuideService
    {
        readonly string _databasePath;
        const int BackupRetention = 12;

        internal AchievementGuideService(string dataRoot)
        {
            if (string.IsNullOrWhiteSpace(dataRoot))
                throw new ArgumentException("A persistent PixelVault data root is required.", nameof(dataRoot));

            var guideRoot = Path.Combine(Path.GetFullPath(dataRoot.Trim()), "guides");
            Directory.CreateDirectory(guideRoot);
            _databasePath = Path.Combine(guideRoot, "pixelvault-guides.sqlite");
        }

        internal string DatabasePath => _databasePath;

        SqliteConnection OpenDatabase()
        {
            var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = _databasePath,
                Mode = SqliteOpenMode.ReadWriteCreate
            }.ToString());
            connection.Open();
            InitializeDatabase(connection);
            return connection;
        }

        static void InitializeDatabase(SqliteConnection connection)
        {
            using (var pragma = connection.CreateCommand())
            {
                pragma.CommandText = @"
PRAGMA journal_mode=WAL;
PRAGMA synchronous=FULL;
PRAGMA foreign_keys=ON;
PRAGMA busy_timeout=5000;";
                pragma.ExecuteNonQuery();
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
CREATE TABLE IF NOT EXISTS achievement_definition (
    achievement_id INTEGER PRIMARY KEY AUTOINCREMENT,
    provider TEXT NOT NULL,
    provider_game_id TEXT NOT NULL,
    provider_achievement_id TEXT NOT NULL,
    pixelvault_game_id TEXT NOT NULL DEFAULT '',
    title TEXT NOT NULL DEFAULT '',
    description TEXT NOT NULL DEFAULT '',
    icon_url TEXT NOT NULL DEFAULT '',
    display_order INTEGER NOT NULL DEFAULT 0,
    is_hidden INTEGER NOT NULL DEFAULT 0,
    is_active INTEGER NOT NULL DEFAULT 1,
    first_synced_utc_ticks INTEGER NOT NULL,
    last_synced_utc_ticks INTEGER NOT NULL,
    UNIQUE(provider, provider_game_id, provider_achievement_id)
);
CREATE INDEX IF NOT EXISTS idx_achievement_definition_pixelvault_game
    ON achievement_definition(pixelvault_game_id, provider, provider_game_id);
CREATE TABLE IF NOT EXISTS achievement_guide (
    achievement_id INTEGER PRIMARY KEY,
    guide_text TEXT NOT NULL DEFAULT '',
    source_url TEXT NOT NULL DEFAULT '',
    source_title TEXT NOT NULL DEFAULT '',
    tags TEXT NOT NULL DEFAULT '',
    is_missable INTEGER NOT NULL DEFAULT 0,
    created_utc_ticks INTEGER NOT NULL,
    updated_utc_ticks INTEGER NOT NULL,
    FOREIGN KEY (achievement_id) REFERENCES achievement_definition(achievement_id) ON DELETE RESTRICT
);";
                command.ExecuteNonQuery();
            }
        }

        public IReadOnlyList<AchievementGuideEntry> SyncDefinitionsAndLoadGuides(
            string pixelVaultGameId,
            IEnumerable<GameAchievementsFetchService.AchievementRow> rows)
        {
            var stableRows = (rows ?? Enumerable.Empty<GameAchievementsFetchService.AchievementRow>())
                .Where(row => row != null && row.HasStableProviderIdentity)
                .GroupBy(ProviderKey, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToList();
            if (stableRows.Count == 0) return Array.Empty<AchievementGuideEntry>();

            var gameId = (pixelVaultGameId ?? string.Empty).Trim();
            var now = DateTime.UtcNow.Ticks;
            using (var connection = OpenDatabase())
            using (var transaction = connection.BeginTransaction())
            {
                foreach (var providerGame in stableRows.GroupBy(
                    row => (Provider: NormalizeProvider(row.Provider), GameId: row.ProviderGameId.Trim())))
                {
                    using (var inactive = connection.CreateCommand())
                    {
                        inactive.Transaction = transaction;
                        inactive.CommandText = @"
UPDATE achievement_definition
SET is_active = 0
WHERE provider = $provider AND provider_game_id = $provider_game_id;";
                        inactive.Parameters.AddWithValue("$provider", providerGame.Key.Provider);
                        inactive.Parameters.AddWithValue("$provider_game_id", providerGame.Key.GameId);
                        inactive.ExecuteNonQuery();
                    }
                }

                foreach (var row in stableRows)
                {
                    using (var upsert = connection.CreateCommand())
                    {
                        upsert.Transaction = transaction;
                        upsert.CommandText = @"
INSERT INTO achievement_definition
    (provider, provider_game_id, provider_achievement_id, pixelvault_game_id, title, description,
     icon_url, display_order, is_hidden, is_active, first_synced_utc_ticks, last_synced_utc_ticks)
VALUES
    ($provider, $provider_game_id, $provider_achievement_id, $pixelvault_game_id, $title, $description,
     $icon_url, $display_order, $is_hidden, 1, $now, $now)
ON CONFLICT(provider, provider_game_id, provider_achievement_id) DO UPDATE SET
    pixelvault_game_id = CASE
        WHEN excluded.pixelvault_game_id = '' THEN achievement_definition.pixelvault_game_id
        ELSE excluded.pixelvault_game_id
    END,
    title = excluded.title,
    description = excluded.description,
    icon_url = excluded.icon_url,
    display_order = excluded.display_order,
    is_hidden = excluded.is_hidden,
    is_active = 1,
    last_synced_utc_ticks = excluded.last_synced_utc_ticks;";
                        upsert.Parameters.AddWithValue("$provider", NormalizeProvider(row.Provider));
                        upsert.Parameters.AddWithValue("$provider_game_id", row.ProviderGameId.Trim());
                        upsert.Parameters.AddWithValue("$provider_achievement_id", row.ProviderAchievementId.Trim());
                        upsert.Parameters.AddWithValue("$pixelvault_game_id", gameId);
                        upsert.Parameters.AddWithValue("$title", (row.Title ?? string.Empty).Trim());
                        upsert.Parameters.AddWithValue("$description", (row.Description ?? string.Empty).Trim());
                        upsert.Parameters.AddWithValue("$icon_url", (row.IconUrlColor ?? string.Empty).Trim());
                        upsert.Parameters.AddWithValue("$display_order", row.SortKey);
                        upsert.Parameters.AddWithValue("$is_hidden", row.Hidden ? 1 : 0);
                        upsert.Parameters.AddWithValue("$now", now);
                        upsert.ExecuteNonQuery();
                    }
                }

                transaction.Commit();
            }

            TryCreateRollingBackup();

            using (var connection = OpenDatabase())
            {
                var result = new List<AchievementGuideEntry>(stableRows.Count);
                foreach (var row in stableRows)
                {
                    var entry = LoadEntry(
                        connection,
                        NormalizeProvider(row.Provider),
                        row.ProviderGameId.Trim(),
                        row.ProviderAchievementId.Trim());
                    if (entry != null) result.Add(entry);
                }
                return result;
            }
        }

        public AchievementGuideEntry SaveGuide(AchievementGuideEdit edit)
        {
            if (edit == null) throw new ArgumentNullException(nameof(edit));
            if (edit.AchievementId <= 0) throw new ArgumentOutOfRangeException(nameof(edit), "A valid achievement ID is required.");
            if (!IsOptionalHttpUrl(edit.SourceUrl))
                throw new ArgumentException("Source URL must be a complete http:// or https:// address.", nameof(edit));

            var now = DateTime.UtcNow.Ticks;
            using (var connection = OpenDatabase())
            using (var transaction = connection.BeginTransaction())
            {
                using (var exists = connection.CreateCommand())
                {
                    exists.Transaction = transaction;
                    exists.CommandText = "SELECT COUNT(1) FROM achievement_definition WHERE achievement_id = $id;";
                    exists.Parameters.AddWithValue("$id", edit.AchievementId);
                    if (Convert.ToInt64(exists.ExecuteScalar(), CultureInfo.InvariantCulture) != 1L)
                        throw new InvalidOperationException("The achievement no longer exists in the local guide catalog.");
                }

                using (var save = connection.CreateCommand())
                {
                    save.Transaction = transaction;
                    save.CommandText = @"
INSERT INTO achievement_guide
    (achievement_id, guide_text, source_url, source_title, tags, is_missable, created_utc_ticks, updated_utc_ticks)
VALUES
    ($achievement_id, $guide_text, $source_url, $source_title, $tags, $is_missable, $now, $now)
ON CONFLICT(achievement_id) DO UPDATE SET
    guide_text = excluded.guide_text,
    source_url = excluded.source_url,
    source_title = excluded.source_title,
    tags = excluded.tags,
    is_missable = excluded.is_missable,
    updated_utc_ticks = excluded.updated_utc_ticks;";
                    save.Parameters.AddWithValue("$achievement_id", edit.AchievementId);
                    save.Parameters.AddWithValue("$guide_text", (edit.GuideText ?? string.Empty).Trim());
                    save.Parameters.AddWithValue("$source_url", (edit.SourceUrl ?? string.Empty).Trim());
                    save.Parameters.AddWithValue("$source_title", (edit.SourceTitle ?? string.Empty).Trim());
                    save.Parameters.AddWithValue("$tags", NormalizeTags(edit.Tags));
                    save.Parameters.AddWithValue("$is_missable", edit.IsMissable ? 1 : 0);
                    save.Parameters.AddWithValue("$now", now);
                    save.ExecuteNonQuery();
                }

                transaction.Commit();
            }

            TryCreateRollingBackup();

            using (var connection = OpenDatabase())
            {
                return LoadEntry(connection, edit.AchievementId)
                    ?? throw new InvalidOperationException("The saved guide could not be reloaded.");
            }
        }

        static string ProviderKey(GameAchievementsFetchService.AchievementRow row)
        {
            return NormalizeProvider(row.Provider) + "\u001f"
                + row.ProviderGameId.Trim() + "\u001f"
                + row.ProviderAchievementId.Trim();
        }

        static string NormalizeProvider(string provider)
        {
            return (provider ?? string.Empty).Trim().ToLowerInvariant();
        }

        internal static string NormalizeTags(string tags)
        {
            return string.Join(", ", (tags ?? string.Empty)
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(tag => tag.Trim())
                .Where(tag => tag.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase));
        }

        void TryCreateRollingBackup()
        {
            if (!File.Exists(_databasePath)) return;
            try
            {
                var backupRoot = Path.Combine(Path.GetDirectoryName(_databasePath) ?? string.Empty, "backups");
                Directory.CreateDirectory(backupRoot);
                var backupPath = Path.Combine(
                    backupRoot,
                    "pixelvault-guides-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff", CultureInfo.InvariantCulture) + ".sqlite");

                using (var source = new SqliteConnection(new SqliteConnectionStringBuilder
                {
                    DataSource = _databasePath,
                    Mode = SqliteOpenMode.ReadOnly
                }.ToString()))
                using (var destination = new SqliteConnection(new SqliteConnectionStringBuilder
                {
                    DataSource = backupPath,
                    Mode = SqliteOpenMode.ReadWriteCreate
                }.ToString()))
                {
                    source.Open();
                    destination.Open();
                    source.BackupDatabase(destination);
                }

                foreach (var stale in new DirectoryInfo(backupRoot)
                    .EnumerateFiles("pixelvault-guides-*.sqlite", SearchOption.TopDirectoryOnly)
                    .OrderByDescending(file => file.Name, StringComparer.OrdinalIgnoreCase)
                    .Skip(BackupRetention))
                {
                    try { stale.Delete(); }
                    catch { }
                }
            }
            catch
            {
                // Backups are best-effort; a failed snapshot must not discard an otherwise valid guide edit.
            }
        }

        static AchievementGuideEntry LoadEntry(
            SqliteConnection connection,
            string provider,
            string providerGameId,
            string providerAchievementId)
        {
            using (var command = CreateEntrySelectCommand(connection))
            {
                command.CommandText += @"
WHERE d.provider = $provider
  AND d.provider_game_id = $provider_game_id
  AND d.provider_achievement_id = $provider_achievement_id;";
                command.Parameters.AddWithValue("$provider", provider);
                command.Parameters.AddWithValue("$provider_game_id", providerGameId);
                command.Parameters.AddWithValue("$provider_achievement_id", providerAchievementId);
                using (var reader = command.ExecuteReader())
                    return reader.Read() ? ReadEntry(reader) : null;
            }
        }

        static AchievementGuideEntry LoadEntry(SqliteConnection connection, long achievementId)
        {
            using (var command = CreateEntrySelectCommand(connection))
            {
                command.CommandText += "\nWHERE d.achievement_id = $achievement_id;";
                command.Parameters.AddWithValue("$achievement_id", achievementId);
                using (var reader = command.ExecuteReader())
                    return reader.Read() ? ReadEntry(reader) : null;
            }
        }

        static SqliteCommand CreateEntrySelectCommand(SqliteConnection connection)
        {
            var command = connection.CreateCommand();
            command.CommandText = @"
SELECT d.achievement_id, d.provider, d.provider_game_id, d.provider_achievement_id,
       d.pixelvault_game_id, d.title, d.description, d.icon_url, d.display_order,
       d.is_hidden, d.is_active,
       COALESCE(g.guide_text, ''), COALESCE(g.source_url, ''), COALESCE(g.source_title, ''),
       COALESCE(g.tags, ''), COALESCE(g.is_missable, 0), COALESCE(g.updated_utc_ticks, 0)
FROM achievement_definition d
LEFT JOIN achievement_guide g ON g.achievement_id = d.achievement_id";
            return command;
        }

        static AchievementGuideEntry ReadEntry(SqliteDataReader reader)
        {
            return new AchievementGuideEntry
            {
                AchievementId = reader.GetInt64(0),
                Provider = reader.GetString(1),
                ProviderGameId = reader.GetString(2),
                ProviderAchievementId = reader.GetString(3),
                PixelVaultGameId = reader.GetString(4),
                Title = reader.GetString(5),
                Description = reader.GetString(6),
                IconUrl = reader.GetString(7),
                DisplayOrder = reader.GetInt32(8),
                IsHidden = reader.GetInt32(9) != 0,
                IsActive = reader.GetInt32(10) != 0,
                GuideText = reader.GetString(11),
                SourceUrl = reader.GetString(12),
                SourceTitle = reader.GetString(13),
                Tags = reader.GetString(14),
                IsMissable = reader.GetInt32(15) != 0,
                GuideUpdatedUtcTicks = reader.GetInt64(16)
            };
        }
    }
}
