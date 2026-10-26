using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace PixelVaultNative
{
    internal static class AchievementOrganization
    {
        internal const string DlcPrefix = "DLC: ";
        internal static readonly string[] DefaultCategories = { "Story", "Actions", "Difficulty", "Collectibles", "Multiplayer", "Miscellaneous", "Uncategorized" };
        internal static int CategoryPriority(string name) => string.Equals(name, "Story", StringComparison.OrdinalIgnoreCase) ? 0
            : string.Equals(name, "Actions", StringComparison.OrdinalIgnoreCase) ? 1 : 2;
        internal static int CategoryOrderIndex(string name, IReadOnlyList<string> order)
        {
            if (order == null) return CategoryPriority(name);
            name = CategoryName(name);
            for (var index = 0; index < order.Count; index++)
                if (string.Equals(name, order[index], StringComparison.OrdinalIgnoreCase)) return index;
            return int.MaxValue;
        }
        internal static string DlcName(string dlc, string category)
        {
            if (!string.IsNullOrWhiteSpace(dlc)) return dlc.Trim();
            category = (category ?? "").Trim();
            return category.StartsWith(DlcPrefix, StringComparison.OrdinalIgnoreCase) ? category.Substring(DlcPrefix.Length).Trim()
                : string.Equals(category, "DLC", StringComparison.OrdinalIgnoreCase) ? "DLC" : "";
        }
        internal static string CategoryName(string category)
        {
            category = (category ?? "").Trim();
            return category.Length == 0 || string.Equals(category, "Base Game", StringComparison.OrdinalIgnoreCase)
                || category.StartsWith(DlcPrefix, StringComparison.OrdinalIgnoreCase) || string.Equals(category, "DLC", StringComparison.OrdinalIgnoreCase)
                ? "Uncategorized" : category;
        }
        internal static string NormalizeDlcName(string name)
        {
            name = (name ?? "").Trim();
            if (name.Length > 120 || name.Any(char.IsControl)) throw new ArgumentException("DLC names must be at most 120 characters without line breaks.");
            if (string.Equals(name, "Base Game", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Base Game is reserved for achievements without DLC.");
            return name;
        }
    }

    sealed partial class AchievementGuideService
    {
        static void InitializeDlcOrganization(SqliteConnection connection)
        {
            EnsureDefinitionColumn(connection, "dlc_name", "TEXT NOT NULL DEFAULT ''");
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"CREATE TABLE IF NOT EXISTS achievement_dlc (
provider TEXT NOT NULL, provider_game_id TEXT NOT NULL, name TEXT COLLATE NOCASE NOT NULL,
PRIMARY KEY(provider, provider_game_id, name));
CREATE TABLE IF NOT EXISTS achievement_organization_migration (name TEXT PRIMARY KEY);";
            command.ExecuteNonQuery();
            command.CommandText = @"CREATE TABLE IF NOT EXISTS achievement_category (name TEXT COLLATE NOCASE PRIMARY KEY);
CREATE TABLE IF NOT EXISTS achievement_category_order (name TEXT COLLATE NOCASE PRIMARY KEY, position INTEGER NOT NULL);";
            command.ExecuteNonQuery();
            MigrateKnownLegacyDlcGroups(connection, transaction);
            // Idempotent so an interrupted column migration or a legacy writer cannot strand DLC assignments.
            command.CommandText = @"UPDATE achievement_definition SET
dlc_name = CASE WHEN lower(trim(group_name)) = 'dlc' THEN 'DLC' ELSE trim(substr(trim(group_name), 6)) END,
group_name = ''
WHERE dlc_name = '' AND (lower(trim(group_name)) = 'dlc' OR lower(trim(group_name)) LIKE 'dlc: %');
INSERT OR IGNORE INTO achievement_dlc SELECT DISTINCT provider, provider_game_id, dlc_name FROM achievement_definition WHERE dlc_name <> '';";
            command.ExecuteNonQuery();
            foreach (var category in AchievementOrganization.DefaultCategories)
            {
                command.CommandText = "INSERT OR IGNORE INTO achievement_category VALUES($category);";
                command.Parameters.Clear();
                command.Parameters.AddWithValue("$category", category);
                command.ExecuteNonQuery();
            }
            command.Parameters.Clear();
            command.CommandText = @"INSERT OR IGNORE INTO achievement_category
SELECT DISTINCT trim(group_name) FROM achievement_definition
WHERE trim(group_name)<>'' AND lower(trim(group_name)) NOT IN ('base game','dlc') AND lower(trim(group_name)) NOT LIKE 'dlc: %';";
            command.ExecuteNonQuery();
            transaction.Commit();
        }

        static void MigrateKnownLegacyDlcGroups(SqliteConnection connection, SqliteTransaction transaction)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT COUNT(*) FROM achievement_organization_migration WHERE name='legacy-dlc-sections-v1';";
            if (Convert.ToInt32(command.ExecuteScalar()) != 0) return;
            // Exact legacy import metadata, keyed by provider/game, never inferred from arbitrary category labels.
            using var stream = typeof(AchievementGuideService).Assembly.GetManifestResourceStream("PixelVault.LegacyAchievementDlcGroups.json")
                ?? throw new InvalidOperationException("The legacy DLC organization resource is missing.");
            using var document = JsonDocument.Parse(stream);
            foreach (var item in document.RootElement.EnumerateArray())
            {
                command.Parameters.Clear();
                command.CommandText = @"UPDATE achievement_definition SET dlc_name=$dlc, group_name=''
WHERE provider=$provider AND provider_game_id=$game AND group_name=$group COLLATE NOCASE AND dlc_name='';";
                command.Parameters.AddWithValue("$dlc", item.GetProperty("dlc").GetString());
                command.Parameters.AddWithValue("$provider", item.GetProperty("provider").GetString());
                command.Parameters.AddWithValue("$game", item.GetProperty("game").GetString());
                command.Parameters.AddWithValue("$group", item.GetProperty("group").GetString());
                command.ExecuteNonQuery();
            }
            command.Parameters.Clear();
            command.CommandText = "INSERT INTO achievement_organization_migration VALUES('legacy-dlc-sections-v1');";
            command.ExecuteNonQuery();
        }

        static void RegisterAchievementOrganization(SqliteConnection connection, SqliteTransaction transaction, long id)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"INSERT OR IGNORE INTO achievement_dlc
SELECT provider, provider_game_id, dlc_name FROM achievement_definition WHERE achievement_id=$id AND dlc_name<>'';
INSERT OR IGNORE INTO achievement_category
SELECT trim(group_name) FROM achievement_definition WHERE achievement_id=$id AND trim(group_name)<>''
AND lower(trim(group_name)) NOT IN ('base game','dlc') AND lower(trim(group_name)) NOT LIKE 'dlc: %';";
            command.Parameters.AddWithValue("$id", id);
            command.ExecuteNonQuery();
        }

        public IReadOnlyList<string> LoadCategoryNames()
        {
            using var connection = OpenDatabase();
            return LoadCategoryNames(connection, null);
        }

        static List<string> LoadCategoryNames(SqliteConnection connection, SqliteTransaction transaction)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"SELECT c.name, o.position FROM achievement_category c
LEFT JOIN achievement_category_order o ON o.name=c.name;";
            var names = new List<(string Name, int? Position)>();
            using var reader = command.ExecuteReader();
            while (reader.Read()) names.Add((reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetInt32(1)));
            int DefaultPosition(string name)
            {
                if (name.Equals("Uncategorized", StringComparison.OrdinalIgnoreCase)) return int.MaxValue;
                var index = Array.FindIndex(AchievementOrganization.DefaultCategories, value => string.Equals(name, value, StringComparison.OrdinalIgnoreCase));
                return index < 0 ? AchievementOrganization.DefaultCategories.Length : index;
            }
            return names.OrderBy(item => item.Position.HasValue ? 0 : 1).ThenBy(item => item.Position ?? DefaultPosition(item.Name))
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase).Select(item => item.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        public void SaveCategoryOrder(IEnumerable<string> names)
        {
            var requested = (names ?? throw new ArgumentNullException(nameof(names))).Select(NormalizeGroupName).ToList();
            if (requested.Count == 0 || requested.Any(string.IsNullOrWhiteSpace)
                || requested.Distinct(StringComparer.OrdinalIgnoreCase).Count() != requested.Count)
                throw new ArgumentException("The category order must contain unique, nonempty category names.", nameof(names));
            using var connection = OpenDatabase();
            using (var transaction = connection.BeginTransaction())
            {
                var current = LoadCategoryNames(connection, transaction);
                var canonicalNames = current.ToDictionary(name => name, StringComparer.OrdinalIgnoreCase);
                if (requested.Any(name => !canonicalNames.ContainsKey(name))) throw new ArgumentException("The category list changed. Reopen Achievements settings and try again.", nameof(names));
                var ordered = requested.Select(name => canonicalNames[name]).Concat(current.Except(requested, StringComparer.OrdinalIgnoreCase)).ToList();
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "DELETE FROM achievement_category_order;";
                command.ExecuteNonQuery();
                for (var index = 0; index < ordered.Count; index++)
                {
                    command.Parameters.Clear();
                    command.CommandText = "INSERT INTO achievement_category_order VALUES($name,$position);";
                    command.Parameters.AddWithValue("$name", ordered[index]);
                    command.Parameters.AddWithValue("$position", index);
                    command.ExecuteNonQuery();
                }
                transaction.Commit();
            }
            TryCreateRollingBackup();
        }

        public IReadOnlyList<string> LoadDlcNames(string provider, string providerGameId)
        {
            using var connection = OpenDatabase();
            using var command = connection.CreateCommand();
            command.CommandText = @"SELECT name FROM achievement_dlc WHERE provider=$provider AND provider_game_id=$game
UNION SELECT dlc_name FROM achievement_definition WHERE provider=$provider AND provider_game_id=$game AND dlc_name<>''
ORDER BY 1 COLLATE NOCASE;";
            command.Parameters.AddWithValue("$provider", NormalizeProvider(provider));
            command.Parameters.AddWithValue("$game", (providerGameId ?? "").Trim());
            var names = new List<string>();
            using var reader = command.ExecuteReader();
            while (reader.Read()) names.Add(reader.GetString(0));
            return names;
        }

        public AchievementGuideEntry AssignAchievementDlc(GameAchievementsFetchService.AchievementRow row, string name)
        {
            if (row?.HasStableProviderIdentity != true) throw new ArgumentException("A stable achievement identity is required.");
            name = AchievementOrganization.NormalizeDlcName(name);
            using var connection = OpenDatabase();
            using (var transaction = connection.BeginTransaction())
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = @"UPDATE achievement_definition SET dlc_name=$name
WHERE provider=$provider AND provider_game_id=$game AND provider_achievement_id=$achievement;";
                command.Parameters.AddWithValue("$name", name);
                command.Parameters.AddWithValue("$provider", NormalizeProvider(row.Provider));
                command.Parameters.AddWithValue("$game", row.ProviderGameId.Trim());
                command.Parameters.AddWithValue("$achievement", row.ProviderAchievementId.Trim());
                if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException("The achievement is not available in the local catalog. Refresh and try again.");
                command.CommandText = "SELECT achievement_id FROM achievement_definition WHERE provider=$provider AND provider_game_id=$game AND provider_achievement_id=$achievement;";
                RegisterAchievementOrganization(connection, transaction, Convert.ToInt64(command.ExecuteScalar()));
                transaction.Commit();
            }
            TryCreateRollingBackup();
            return LoadEntry(connection, NormalizeProvider(row.Provider), row.ProviderGameId.Trim(), row.ProviderAchievementId.Trim());
        }
    }
}
