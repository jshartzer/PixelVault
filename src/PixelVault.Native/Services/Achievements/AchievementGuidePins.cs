using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace PixelVaultNative
{
    enum AchievementPinChange { Changed, Unchanged, LimitReached, ProgressUnavailable, AlreadyEarned, DefinitionUnavailable }

    sealed partial class AchievementGuideService
    {
        internal const int MaximumAchievementPins = 3;

        static void InitializePinTable(SqliteConnection connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText = @"
CREATE TABLE IF NOT EXISTS achievement_pin (
    achievement_id INTEGER PRIMARY KEY,
    display_order INTEGER NOT NULL,
    pinned_utc_ticks INTEGER NOT NULL,
    FOREIGN KEY (achievement_id) REFERENCES achievement_definition(achievement_id) ON DELETE RESTRICT
);";
            command.ExecuteNonQuery();
        }

        public IReadOnlyList<AchievementGuideEntry> LoadPinnedAchievements(string provider, string providerGameId)
        {
            using var connection = OpenDatabase();
            using var command = CreateEntrySelectCommand(connection);
            command.CommandText += @"
INNER JOIN achievement_pin p ON p.achievement_id = d.achievement_id
WHERE d.provider = $provider AND d.provider_game_id = $game
ORDER BY p.display_order, p.pinned_utc_ticks, p.achievement_id;";
            command.Parameters.AddWithValue("$provider", NormalizeProvider(provider));
            command.Parameters.AddWithValue("$game", (providerGameId ?? string.Empty).Trim());
            using var reader = command.ExecuteReader();
            var entries = new List<AchievementGuideEntry>();
            while (reader.Read()) entries.Add(ReadEntry(reader));
            return entries;
        }

        public AchievementPinChange SetAchievementPinned(GameAchievementsFetchService.AchievementRow row, bool pinned)
        {
            if (row == null || !row.HasStableProviderIdentity) return AchievementPinChange.DefinitionUnavailable;
            var changed = false;
            using (var connection = OpenDatabase())
            // Reserve the write lock before counting so simultaneous windows cannot exceed the game limit.
            using (var transaction = connection.BeginTransaction(deferred: false))
            {
                using var lookup = connection.CreateCommand();
                lookup.Transaction = transaction;
                lookup.CommandText = @"
SELECT d.achievement_id, d.is_active, p.achievement_id IS NOT NULL
FROM achievement_definition d LEFT JOIN achievement_pin p ON p.achievement_id = d.achievement_id
WHERE d.provider = $provider AND d.provider_game_id = $game AND d.provider_achievement_id = $achievement;";
                lookup.Parameters.AddWithValue("$provider", NormalizeProvider(row.Provider));
                lookup.Parameters.AddWithValue("$game", row.ProviderGameId.Trim());
                lookup.Parameters.AddWithValue("$achievement", row.ProviderAchievementId.Trim());
                long id;
                bool active, alreadyPinned;
                using (var reader = lookup.ExecuteReader())
                {
                    if (!reader.Read()) return AchievementPinChange.DefinitionUnavailable;
                    id = reader.GetInt64(0);
                    active = reader.GetBoolean(1);
                    alreadyPinned = reader.GetBoolean(2);
                }
                if (pinned == alreadyPinned) return AchievementPinChange.Unchanged;
                if (pinned)
                {
                    if (!active) return AchievementPinChange.DefinitionUnavailable;
                    if (!row.ProgressKnown) return AchievementPinChange.ProgressUnavailable;
                    if (row.Unlocked) return AchievementPinChange.AlreadyEarned;
                    using var count = connection.CreateCommand();
                    count.Transaction = transaction;
                    count.CommandText = @"
SELECT COUNT(*), COALESCE(MAX(p.display_order), 0)
FROM achievement_pin p INNER JOIN achievement_definition d ON d.achievement_id = p.achievement_id
WHERE d.provider = $provider AND d.provider_game_id = $game;";
                    count.Parameters.AddWithValue("$provider", NormalizeProvider(row.Provider));
                    count.Parameters.AddWithValue("$game", row.ProviderGameId.Trim());
                    long nextOrder;
                    using (var reader = count.ExecuteReader())
                    {
                        reader.Read();
                        if (reader.GetInt32(0) >= MaximumAchievementPins) return AchievementPinChange.LimitReached;
                        nextOrder = reader.GetInt64(1) + 1;
                    }
                    using var insert = connection.CreateCommand();
                    insert.Transaction = transaction;
                    insert.CommandText = "INSERT INTO achievement_pin VALUES ($id, $order, $now);";
                    insert.Parameters.AddWithValue("$id", id);
                    insert.Parameters.AddWithValue("$order", nextOrder);
                    insert.Parameters.AddWithValue("$now", DateTime.UtcNow.Ticks);
                    insert.ExecuteNonQuery();
                }
                else
                {
                    using var remove = connection.CreateCommand();
                    remove.Transaction = transaction;
                    remove.CommandText = "DELETE FROM achievement_pin WHERE achievement_id = $id;";
                    remove.Parameters.AddWithValue("$id", id);
                    remove.ExecuteNonQuery();
                }
                transaction.Commit();
                changed = true;
            }
            if (changed) TryCreateRollingBackup();
            return AchievementPinChange.Changed;
        }

        public int ReconcileAchievementPins(IEnumerable<GameAchievementsFetchService.AchievementRow> rows)
        {
            // Conflicting duplicate status is not confirmation. Missing/unknown rows cannot erase planning data.
            var earned = (rows ?? Enumerable.Empty<GameAchievementsFetchService.AchievementRow>())
                .Where(row => row != null && row.HasStableProviderIdentity)
                .GroupBy(ProviderKey, StringComparer.Ordinal)
                .Where(group => group.All(row => row.ProgressKnown && row.Unlocked))
                .Select(group => group.First()).ToList();
            if (earned.Count == 0) return 0;
            var removed = 0;
            using (var connection = OpenDatabase())
            using (var transaction = connection.BeginTransaction())
            {
                foreach (var row in earned)
                {
                    using var command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = @"
DELETE FROM achievement_pin WHERE achievement_id IN (
    SELECT achievement_id FROM achievement_definition
    WHERE provider = $provider AND provider_game_id = $game AND provider_achievement_id = $achievement
);";
                    command.Parameters.AddWithValue("$provider", NormalizeProvider(row.Provider));
                    command.Parameters.AddWithValue("$game", row.ProviderGameId.Trim());
                    command.Parameters.AddWithValue("$achievement", row.ProviderAchievementId.Trim());
                    removed += command.ExecuteNonQuery();
                }
                transaction.Commit();
            }
            if (removed > 0) TryCreateRollingBackup();
            return removed;
        }
    }
}
