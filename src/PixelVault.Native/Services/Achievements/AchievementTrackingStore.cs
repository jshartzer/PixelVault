using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace PixelVaultNative
{
    /// <summary>Durable local checklist state, isolated by tracker and provider account, outside all rebuildable caches.</summary>
    sealed partial class AchievementTrackingStore
    {
        readonly string path;
        internal string ImportDirectory { get; }
        internal string DatabasePath => path;
        internal AchievementTrackingStore(string dataRoot)
        {
            if (string.IsNullOrWhiteSpace(dataRoot)) throw new ArgumentException("A persistent data root is required.");
            var root = Path.Combine(Path.GetFullPath(dataRoot), "achievement-tracking");
            path = Path.Combine(root, "pixelvault-tracking.sqlite");
            ImportDirectory = Path.Combine(root, "imports");
        }

        SqliteConnection Open()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = @"
PRAGMA journal_mode=WAL;
PRAGMA synchronous=FULL;
PRAGMA busy_timeout=5000;
CREATE TABLE IF NOT EXISTS tracker_definition (id TEXT PRIMARY KEY, definition TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS tracker_catalogue_history(id INTEGER PRIMARY KEY AUTOINCREMENT,tracker_id TEXT NOT NULL,before_definition TEXT NOT NULL,after_definition TEXT NOT NULL,created_utc INTEGER NOT NULL,undone_utc INTEGER NOT NULL DEFAULT 0,source TEXT NOT NULL,fingerprint TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS tracker_link (tracker_id TEXT PRIMARY KEY, achievement_id TEXT NULL);
CREATE TABLE IF NOT EXISTS tracker_source (tracker_id TEXT NOT NULL, account TEXT NOT NULL, binding TEXT NOT NULL, status TEXT NOT NULL,
 PRIMARY KEY(tracker_id,account));
CREATE TABLE IF NOT EXISTS tracker_automation (tracker_id TEXT NOT NULL, account TEXT NOT NULL, settings TEXT NOT NULL, state TEXT NOT NULL,
 PRIMARY KEY(tracker_id,account));
CREATE TABLE IF NOT EXISTS tracker_automation_result (id INTEGER PRIMARY KEY AUTOINCREMENT, tracker_id TEXT NOT NULL, account TEXT NOT NULL,
 created_utc INTEGER NOT NULL, message TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS tracker_history (id INTEGER PRIMARY KEY AUTOINCREMENT, tracker_id TEXT NOT NULL, account TEXT NOT NULL,
 kind TEXT NOT NULL,label TEXT NOT NULL,created_utc INTEGER NOT NULL,undone_utc INTEGER NOT NULL DEFAULT 0,
 adapter_id TEXT NOT NULL,adapter_version TEXT NOT NULL,fingerprint TEXT NOT NULL,observed_utc INTEGER NOT NULL,changes TEXT NOT NULL);
CREATE INDEX IF NOT EXISTS tracker_history_scope ON tracker_history(tracker_id,account,id);
CREATE TABLE IF NOT EXISTS tracker_item_state (
 tracker_id TEXT NOT NULL, account TEXT NOT NULL, item_id TEXT NOT NULL,
 reported_current INTEGER NULL, manual_current INTEGER NULL, availability INTEGER NOT NULL DEFAULT 0,
 note TEXT NOT NULL DEFAULT '', source TEXT NOT NULL DEFAULT '', evidence TEXT NOT NULL DEFAULT '',
 observed_utc INTEGER NOT NULL DEFAULT 0, manual_utc INTEGER NOT NULL DEFAULT 0,
 PRIMARY KEY(tracker_id,account,item_id));
CREATE TABLE IF NOT EXISTS tracker_import (tracker_id TEXT NOT NULL,account TEXT NOT NULL,digest TEXT NOT NULL,
 PRIMARY KEY(tracker_id,account,digest));";
            cmd.ExecuteNonQuery();
            return c;
        }

        internal IReadOnlyList<AchievementTrackerDefinition> Definitions()
        {
            var definitions = AchievementTrackingCatalog.Builtins().ToList();
            using var c = Open(); using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT definition FROM tracker_definition ORDER BY id";
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) definitions.Add(JsonSerializer.Deserialize<AchievementTrackerDefinition>(reader.GetString(0), AchievementTrackingCatalog.Json));
            reader.Close();
            cmd.CommandText = "SELECT tracker_id,achievement_id FROM tracker_link";
            using var links = cmd.ExecuteReader();
            while (links.Read())
            {
                var definition = definitions.FirstOrDefault(d => d.Id == links.GetString(0));
                if (definition != null) definition.AchievementId = links.IsDBNull(1) ? null : links.GetString(1);
            }
            return definitions;
        }

        internal void SetAchievementLink(string trackerId, GameAchievementsFetchService.AchievementRow achievement)
        {
            var definition = Definition(trackerId);
            if (achievement != null && (!achievement.HasStableProviderIdentity || achievement.Provider != definition.Provider || achievement.ProviderGameId != definition.GameId))
                throw new InvalidDataException("Choose an achievement from this checklist's game.");
            using var c = Open(); using var cmd = c.CreateCommand();
            cmd.CommandText = "INSERT INTO tracker_link(tracker_id,achievement_id) VALUES($t,$a) ON CONFLICT(tracker_id) DO UPDATE SET achievement_id=$a";
            cmd.Parameters.AddWithValue("$t", trackerId);
            cmd.Parameters.AddWithValue("$a", (object)achievement?.ProviderAchievementId ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }

        internal IReadOnlyList<GameAchievementsFetchService.AchievementRow> AchievementChoices(string steamAccount, string retroAccount)
        {
            var hub = new AchievementHubStore(Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(path)), "achievement-hub"));
            var result = new List<GameAchievementsFetchService.AchievementRow>();
            foreach (var identity in new[] { (Provider: "steam", Account: steamAccount), (Provider: "retroachievements", Account: retroAccount) })
            {
                if (string.IsNullOrWhiteSpace(identity.Account)) continue;
                var data = hub.LoadAsync(identity.Provider, identity.Account, System.Threading.CancellationToken.None).GetAwaiter().GetResult();
                result.AddRange(data.Games.Values.SelectMany(s => s.Rows).Where(r => r?.HasStableProviderIdentity == true));
            }
            return result;
        }

        internal void AddDefinition(AchievementTrackerDefinition definition)
        {
            AchievementTrackingCatalog.Validate(definition);
            if (Definitions().Any(d => d.Id == definition.Id)) throw new InvalidDataException("This checklist already exists.");
            using var c = Open(); using var cmd = c.CreateCommand();
            cmd.CommandText = "INSERT INTO tracker_definition(id,definition) VALUES($id,$json)";
            cmd.Parameters.AddWithValue("$id", definition.Id);
            cmd.Parameters.AddWithValue("$json", JsonSerializer.Serialize(definition, AchievementTrackingCatalog.Json));
            cmd.ExecuteNonQuery();
        }

        AchievementTrackerDefinition Definition(string id) => Definitions().FirstOrDefault(d => d.Id == id)
            ?? throw new InvalidDataException("The checklist definition was not found.");

        static string RequireAccount(AchievementTrackerDefinition definition, string account)
        {
            var key = AchievementTrackingCatalog.AccountKey(definition.Provider, account);
            if (key.Length == 0) throw new InvalidDataException("Connect an account before saving checklist progress.");
            return key;
        }

        internal IReadOnlyList<AchievementTrackerState> Load(string id, string account)
        {
            var definition = Definition(id);
            var key = AchievementTrackingCatalog.AccountKey(definition.Provider, account);
            var saved = new Dictionary<string, AchievementTrackerState>(StringComparer.Ordinal);
            using var c = Open(); using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT item_id,reported_current,manual_current,availability,note,source,evidence,observed_utc,manual_utc FROM tracker_item_state WHERE tracker_id=$t AND account=$a";
            cmd.Parameters.AddWithValue("$t", id); cmd.Parameters.AddWithValue("$a", key);
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) saved[reader.GetString(0)] = new AchievementTrackerState
            {
                ReportedCurrent = reader.IsDBNull(1) ? null : reader.GetInt32(1),
                ManualCurrent = reader.IsDBNull(2) ? null : reader.GetInt32(2), Availability = (TrackerAvailability)reader.GetInt32(3),
                Note = reader.GetString(4), Source = reader.GetString(5), Evidence = reader.GetString(6),
                ObservedUtcTicks = reader.GetInt64(7), ManualUtcTicks = reader.GetInt64(8)
            };
            return definition.Items.OrderBy(i => i.Order).ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase).Select(item =>
            {
                var state = saved.TryGetValue(item.Id, out var value) ? value : new AchievementTrackerState();
                state.Item = item; return state;
            }).ToList();
        }

        internal void SetManualProgress(string id, string account, string itemId, int? current)
        {
            var definition = Definition(id); var key = RequireAccount(definition, account);
            if (!definition.Items.Any(i => i.Id == itemId) || current < 0 || current > 1000000)
                throw new InvalidDataException("Invalid checklist item or count.");
            using var c = Open(); using var tx = c.BeginTransaction(); using var cmd = c.CreateCommand(); cmd.Transaction = tx;
            var before = ReadRecord(c, tx, id, key, itemId).Fields(TrackerChangeFields.Manual);
            cmd.CommandText = @"INSERT INTO tracker_item_state(tracker_id,account,item_id,manual_current,manual_utc)
VALUES($t,$a,$i,$n,$u) ON CONFLICT(tracker_id,account,item_id) DO UPDATE SET manual_current=$n,manual_utc=$u";
            cmd.Parameters.AddWithValue("$t", id); cmd.Parameters.AddWithValue("$a", key); cmd.Parameters.AddWithValue("$i", itemId);
            cmd.Parameters.AddWithValue("$n", (object)current ?? DBNull.Value); cmd.Parameters.AddWithValue("$u", DateTime.UtcNow.Ticks);
            cmd.ExecuteNonQuery();
            RecordBatch(c, tx, id, key, "manual", "Progress: " + definition.Items.First(i => i.Id == itemId).Name, "manual", "1", "", 0,
                new() { new() { ItemId = itemId, Fields = TrackerChangeFields.Manual, Before = before, After = ReadRecord(c, tx, id, key, itemId).Fields(TrackerChangeFields.Manual) } });
            tx.Commit();
        }

        internal void SaveNote(string id, string account, string itemId, string note)
        {
            var definition = Definition(id); var key = RequireAccount(definition, account);
            if (!definition.Items.Any(i => i.Id == itemId) || (note?.Length ?? 0) > 20000) throw new InvalidDataException("Invalid checklist note.");
            using var c = Open(); using var tx = c.BeginTransaction(); using var cmd = c.CreateCommand(); cmd.Transaction = tx;
            var before = ReadRecord(c, tx, id, key, itemId).Fields(TrackerChangeFields.Note);
            cmd.CommandText = @"INSERT INTO tracker_item_state(tracker_id,account,item_id,note) VALUES($t,$a,$i,$n)
ON CONFLICT(tracker_id,account,item_id) DO UPDATE SET note=$n";
            cmd.Parameters.AddWithValue("$t", id); cmd.Parameters.AddWithValue("$a", key); cmd.Parameters.AddWithValue("$i", itemId);
            cmd.Parameters.AddWithValue("$n", note ?? ""); cmd.ExecuteNonQuery();
            RecordBatch(c, tx, id, key, "note", "Note: " + definition.Items.First(i => i.Id == itemId).Name, "manual", "1", "", 0,
                new() { new() { ItemId = itemId, Fields = TrackerChangeFields.Note, Before = before, After = ReadRecord(c, tx, id, key, itemId).Fields(TrackerChangeFields.Note) } });
            tx.Commit();
        }

        internal int Import(string json, string expectedAccount, string expectedTrackerId = null, bool automatic = false)
        {
            if (json == null || json.Length > 4 * 1024 * 1024) throw new InvalidDataException("Progress import is empty or too large.");
            var snapshot = JsonSerializer.Deserialize<AchievementTrackerSnapshot>(json, AchievementTrackingCatalog.Json)
                ?? throw new InvalidDataException("Invalid checklist snapshot.");
            var definition = Definition(snapshot.TrackerId);
            if (expectedTrackerId != null && snapshot.TrackerId != expectedTrackerId)
                throw new InvalidDataException("The snapshot belongs to a different checklist.");
            var account = RequireAccount(definition, expectedAccount);
            if (snapshot.Version != 1 || snapshot.Provider != definition.Provider || snapshot.GameId != definition.GameId
                || AchievementTrackingCatalog.AccountKey(snapshot.Provider, snapshot.Account) != account)
                throw new InvalidDataException("The snapshot belongs to a different game or account.");
            if (snapshot.ObservedUtcTicks <= 0 || snapshot.ObservedUtcTicks > DateTime.UtcNow.AddMinutes(5).Ticks
                || string.IsNullOrWhiteSpace(snapshot.Source) || snapshot.Source.Length > 1000 || (snapshot.Evidence?.Length ?? 0) > 4000 || snapshot.Items == null || snapshot.Items.Count == 0
                || snapshot.Items.Count > definition.Items.Count)
                throw new InvalidDataException("The snapshot needs a valid source, observation time and item list.");
            var known = definition.Items.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in snapshot.Items)
                if (item == null || !known.Contains(item.ItemId ?? "") || !seen.Add(item.ItemId)
                    || item.Current < 0 || item.Current > 1000000 || !Enum.IsDefined(item.Availability) || (item.Note?.Length ?? 0) > 20000)
                    throw new InvalidDataException("The snapshot contains an invalid or duplicate item.");
            var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
            using var c = Open(); using var tx = c.BeginTransaction();
            using var check = c.CreateCommand(); check.Transaction = tx;
            check.CommandText = "SELECT COUNT(*) FROM tracker_import WHERE tracker_id=$t AND account=$a AND digest=$d";
            check.Parameters.AddWithValue("$t", snapshot.TrackerId); check.Parameters.AddWithValue("$a", account); check.Parameters.AddWithValue("$d", digest);
            if (Convert.ToInt32(check.ExecuteScalar()) != 0)
            {
                if (automatic) return 0;
                using var history = c.CreateCommand(); history.Transaction = tx;
                history.CommandText = "SELECT undone_utc FROM tracker_history WHERE tracker_id=$t AND account=$a AND fingerprint=$d AND kind='import' ORDER BY id DESC LIMIT 1";
                history.Parameters.AddWithValue("$t", snapshot.TrackerId); history.Parameters.AddWithValue("$a", account); history.Parameters.AddWithValue("$d", digest);
                var undone = history.ExecuteScalar();
                if (undone == null || Convert.ToInt64(undone) == 0) return 0;
            }
            int applied = 0;
            var changes = new List<TrackerHistoryChange>();
            foreach (var item in snapshot.Items)
            {
                var before = ReadRecord(c, tx, snapshot.TrackerId, account, item.ItemId);
                using var cmd = c.CreateCommand(); cmd.Transaction = tx;
                cmd.CommandText = @"INSERT INTO tracker_item_state(tracker_id,account,item_id,reported_current,availability,source,evidence,observed_utc)
VALUES($t,$a,$i,$n,$v,$s,$e,$u) ON CONFLICT(tracker_id,account,item_id) DO UPDATE SET
reported_current=$n,availability=$v,source=$s,evidence=$e,observed_utc=$u WHERE excluded.observed_utc>tracker_item_state.observed_utc";
                cmd.Parameters.AddWithValue("$t", snapshot.TrackerId); cmd.Parameters.AddWithValue("$a", account);
                cmd.Parameters.AddWithValue("$i", item.ItemId); cmd.Parameters.AddWithValue("$n", (object)item.Current ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$v", (int)item.Availability); cmd.Parameters.AddWithValue("$s", snapshot.Source.Trim());
                cmd.Parameters.AddWithValue("$e", snapshot.Evidence ?? ""); cmd.Parameters.AddWithValue("$u", snapshot.ObservedUtcTicks);
                var count = cmd.ExecuteNonQuery(); applied += count;
                if (count > 0)
                {
                    var fields = TrackerChangeFields.Reported;
                    if (CanAddSourceNote(c,tx,snapshot.TrackerId,account,item.ItemId,before,item.Note))
                    { WriteFields(c,tx,snapshot.TrackerId,account,item.ItemId,new TrackerStoredRecord { Note = item.Note },TrackerChangeFields.Note); fields |= TrackerChangeFields.Note; }
                    changes.Add(new TrackerHistoryChange { ItemId = item.ItemId, Fields = fields, Before = before.Fields(fields), After = ReadRecord(c,tx,snapshot.TrackerId,account,item.ItemId).Fields(fields) });
                }
            }
            using var mark = c.CreateCommand(); mark.Transaction = tx;
            mark.CommandText = "INSERT OR IGNORE INTO tracker_import(tracker_id,account,digest) VALUES($t,$a,$d)";
            mark.Parameters.AddWithValue("$t", snapshot.TrackerId); mark.Parameters.AddWithValue("$a", account); mark.Parameters.AddWithValue("$d", digest);
            mark.ExecuteNonQuery();
            RecordBatch(c, tx, snapshot.TrackerId, account, "import", "Import progress", "json-import", "1", digest, snapshot.ObservedUtcTicks, changes);
            tx.Commit(); return applied;
        }

        internal int ImportPending(string trackerId, string account, Action<string> reportFailure = null)
        {
            if (!Directory.Exists(ImportDirectory) || string.IsNullOrWhiteSpace(account)) return 0;
            int count = 0;
            foreach (var file in Directory.EnumerateFiles(ImportDirectory, "*.json").OrderBy(p => p, StringComparer.Ordinal))
            {
                try
                {
                    if (new FileInfo(file).Length > 4 * 1024 * 1024) throw new InvalidDataException("The saved import is too large.");
                    var text = File.ReadAllText(file);
                    var snapshot = JsonSerializer.Deserialize<AchievementTrackerSnapshot>(text, AchievementTrackingCatalog.Json);
                    if (snapshot?.TrackerId != trackerId || AchievementTrackingCatalog.AccountKey(snapshot.Provider, snapshot.Account)
                        != AchievementTrackingCatalog.AccountKey(snapshot.Provider, account)) continue;
                    count += Import(text, account, trackerId, automatic: true);
                }
                catch (Exception ex) when (ex is IOException || ex is JsonException || ex is UnauthorizedAccessException)
                {
                    reportFailure?.Invoke(Path.GetFileName(file) + ": " + ex.Message);
                }
            }
            return count;
        }
    }
}
