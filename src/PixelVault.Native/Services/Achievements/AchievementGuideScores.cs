using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace PixelVaultNative
{
    interface IAchievementScoreStore
    {
        void SyncAchievementScores(string account, IEnumerable<GameAchievementsFetchService.AchievementRow> rows);
        IReadOnlyDictionary<string, AchievementScoreTotals> LoadAchievementScoreTotals(string account);
    }

    sealed partial class AchievementGuideService : IAchievementScoreStore
    {
        public IReadOnlyDictionary<string, AchievementScoreTotals> LoadAchievementScoreTotals(string account) =>
            LoadAchievementScoreTotals(account, DateTime.UtcNow, TimeZoneInfo.Local);

        internal IReadOnlyDictionary<string, AchievementScoreTotals> LoadAchievementScoreTotals(string account, DateTime nowUtc, TimeZoneInfo zone)
        {
            var totals = new Dictionary<string, AchievementScoreTotals>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(account)) return totals;
            using var connection = OpenDatabase();
            using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT d.provider, d.provider_game_id, SUM(a.awarded_points), SUM(a.is_provisional),
    SUM(CASE WHEN a.unlock_utc_ticks >= $month_start AND a.unlock_utc_ticks <= $now THEN a.awarded_points ELSE 0 END)
FROM achievement_score_award AS a
JOIN achievement_definition AS d ON d.achievement_id = a.achievement_id
WHERE a.account_key = $account
GROUP BY d.provider, d.provider_game_id;";
            command.Parameters.AddWithValue("$account", AchievementHubStore.AccountKey(account));
            command.Parameters.AddWithValue("$month_start", AchievementScoring.MonthStartUtc(nowUtc, zone).Ticks);
            command.Parameters.AddWithValue("$now", nowUtc.Ticks);
            using var reader = command.ExecuteReader();
            while (reader.Read()) totals[reader.GetString(0) + ":" + reader.GetString(1)] = new AchievementScoreTotals
                { Earned = reader.GetInt64(2), ProvisionalAwards = reader.GetInt32(3), EarnedThisMonth = reader.GetInt64(4) };
            return totals;
        }

        static void InitializeScoreTables(SqliteConnection connection)
        {
            EnsureDefinitionColumn(connection, "score_points", "INTEGER NOT NULL DEFAULT 5 CHECK(score_points BETWEEN 5 AND 50 AND score_points % 5 = 0)");
            EnsureDefinitionColumn(connection, "score_rarity_percent", "REAL");
            EnsureDefinitionColumn(connection, "score_rule_version", "INTEGER NOT NULL DEFAULT 1");
            EnsureDefinitionColumn(connection, "score_updated_utc_ticks", "INTEGER NOT NULL DEFAULT 0");
            EnsureDefinitionColumn(connection, "score_provisional", "INTEGER NOT NULL DEFAULT 1");
            using var command = connection.CreateCommand();
            command.CommandText = @"
CREATE TABLE IF NOT EXISTS achievement_score_award (
    account_key TEXT NOT NULL,
    achievement_id INTEGER NOT NULL,
    awarded_points INTEGER NOT NULL CHECK(awarded_points BETWEEN 5 AND 50 AND awarded_points % 5 = 0),
    rarity_percent REAL,
    rule_version INTEGER NOT NULL,
    is_provisional INTEGER NOT NULL,
    awarded_utc_ticks INTEGER NOT NULL,
    finalized_utc_ticks INTEGER NOT NULL,
    unlock_utc_ticks INTEGER NOT NULL,
    PRIMARY KEY(account_key, achievement_id),
    FOREIGN KEY(achievement_id) REFERENCES achievement_definition(achievement_id) ON DELETE RESTRICT
);";
            command.ExecuteNonQuery();
        }

        /// <summary>
        /// Database values are authoritative, including when loading old JSON snapshots. Final awards are immutable.
        /// The caller supplies account-scoped progress; no account or unknown progress can create an award.
        /// </summary>
        public void SyncAchievementScores(string account, IEnumerable<GameAchievementsFetchService.AchievementRow> rows)
        {
            var stable = (rows ?? Enumerable.Empty<GameAchievementsFetchService.AchievementRow>())
                .Where(r => r?.HasStableProviderIdentity == true).ToList();
            if (stable.Count == 0) return;
            var accountKey = AchievementHubStore.AccountKey(account);
            var now = DateTime.UtcNow.Ticks;
            var values = new List<(GameAchievementsFetchService.AchievementRow Row, int Points, bool Provisional, int? Award, bool AwardProvisional)>();
            using var connection = OpenDatabase();
            using var transaction = connection.BeginTransaction(deferred: false);
            foreach (var row in stable)
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = @"
INSERT INTO achievement_definition
    (provider, provider_game_id, provider_achievement_id, title, description, icon_url,
     first_synced_utc_ticks, last_synced_utc_ticks, score_updated_utc_ticks)
VALUES ($provider, $game, $achievement, $title, $description, $icon, $now, $now, $now)
ON CONFLICT(provider, provider_game_id, provider_achievement_id) DO NOTHING;

UPDATE achievement_definition
SET score_points = $points, score_rarity_percent = $percent, score_rule_version = $version,
    score_updated_utc_ticks = $now, score_provisional = 0
WHERE provider = $provider AND provider_game_id = $game AND provider_achievement_id = $achievement
    AND $percent IS NOT NULL
    AND (score_provisional = 1 OR score_rarity_percent IS NOT $percent OR score_rule_version != $version);

INSERT INTO achievement_score_award
    (account_key, achievement_id, awarded_points, rarity_percent, rule_version, is_provisional,
     awarded_utc_ticks, finalized_utc_ticks, unlock_utc_ticks)
SELECT $account, achievement_id, score_points, score_rarity_percent, score_rule_version, score_provisional,
    $now, CASE WHEN score_provisional = 0 THEN $now ELSE 0 END, $unlock
FROM achievement_definition
WHERE provider = $provider AND provider_game_id = $game AND provider_achievement_id = $achievement
    AND $earned = 1 AND $account != ''
ON CONFLICT(account_key, achievement_id) DO NOTHING;

UPDATE achievement_score_award
SET unlock_utc_ticks = $unlock
WHERE account_key = $account
    AND achievement_id = (SELECT achievement_id FROM achievement_definition
        WHERE provider = $provider AND provider_game_id = $game AND provider_achievement_id = $achievement)
    AND $earned = 1 AND $unlock BETWEEN $epoch AND $now
    AND (unlock_utc_ticks < $epoch OR unlock_utc_ticks > $now);

UPDATE achievement_score_award
SET awarded_points = d.score_points, rarity_percent = d.score_rarity_percent,
    rule_version = d.score_rule_version, is_provisional = 0, finalized_utc_ticks = $now
FROM achievement_definition AS d
WHERE achievement_score_award.achievement_id = d.achievement_id AND account_key = $account
    AND d.provider = $provider AND d.provider_game_id = $game AND d.provider_achievement_id = $achievement
    AND achievement_score_award.is_provisional = 1 AND d.score_provisional = 0;

SELECT d.score_points, d.score_provisional, a.awarded_points, a.is_provisional
FROM achievement_definition AS d
LEFT JOIN achievement_score_award AS a ON a.achievement_id = d.achievement_id AND a.account_key = $account
WHERE d.provider = $provider AND d.provider_game_id = $game AND d.provider_achievement_id = $achievement;";
                command.Parameters.AddWithValue("$provider", NormalizeProvider(row.Provider));
                command.Parameters.AddWithValue("$game", row.ProviderGameId.Trim());
                command.Parameters.AddWithValue("$achievement", row.ProviderAchievementId.Trim());
                command.Parameters.AddWithValue("$title", row.Title ?? "");
                command.Parameters.AddWithValue("$description", row.Description ?? "");
                command.Parameters.AddWithValue("$icon", row.IconUrlColor ?? "");
                command.Parameters.AddWithValue("$now", now);
                command.Parameters.AddWithValue("$epoch", DateTime.UnixEpoch.Ticks);
                command.Parameters.AddWithValue("$points", AchievementScoring.Points(row.GlobalUnlockPercent));
                command.Parameters.AddWithValue("$percent", AchievementCollectionStats.ValidPercent(row.GlobalUnlockPercent)
                    ? (object)row.GlobalUnlockPercent.Value : DBNull.Value);
                command.Parameters.AddWithValue("$version", AchievementScoring.RuleVersion);
                command.Parameters.AddWithValue("$account", accountKey);
                command.Parameters.AddWithValue("$earned", row.ProgressKnown && row.Unlocked ? 1 : 0);
                command.Parameters.AddWithValue("$unlock", row.UnlockUtcTicks);
                using var reader = command.ExecuteReader();
                if (reader.Read()) values.Add((row, reader.GetInt32(0), reader.GetBoolean(1),
                    reader.IsDBNull(2) ? null : reader.GetInt32(2), !reader.IsDBNull(3) && reader.GetBoolean(3)));
            }
            transaction.Commit();
            foreach (var value in values)
            {
                value.Row.Points = value.Points;
                value.Row.PointsProvisional = value.Provisional;
                value.Row.AwardedPoints = value.Award;
                value.Row.AwardProvisional = value.AwardProvisional;
            }
        }
    }
}
