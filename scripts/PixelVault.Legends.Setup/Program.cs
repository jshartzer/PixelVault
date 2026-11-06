using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PixelVault.Tracking;
using PixelVaultNative;

SQLitePCL.Batteries_V2.Init();
if (args.Length != 4) throw new ArgumentException("Arguments: application-data-root workspace-manifest worker-exe receipt-path");
var root = Path.GetFullPath(args[0]);
var worker = Path.GetFullPath(args[2]);
var receipt = Path.GetFullPath(args[3]);
if (!File.Exists(worker)) throw new FileNotFoundException("The reviewed headless reader is missing.");
using var manifest = JsonDocument.Parse(File.ReadAllText(args[1]));
var accounts = manifest.RootElement.GetProperty("Accounts").EnumerateObject()
    .ToDictionary(p => p.Name, p => p.Value.GetString()!);
var repo = new TrackerRepository(root, pooling: false, initializeSchema: false);
repo.ValidateSharedSchema();
using var backgroundOwner = TrackerSourceLease.OwnBackground(repo.DatabasePath);

var trackerId = CenturyLegendsCatalogue.TrackerId;
var achievementId = CenturyAchievementCatalog.DeathStrandingLegendsId;
var definition = repo.Definitions().Single(d => d.Id == trackerId);
if (!CenturyLegendsCatalogue.Matches(definition)) throw new InvalidDataException("Unexpected Legend tracker catalogue.");
var account = accounts.GetValueOrDefault("steam") ?? throw new InvalidDataException("The approved Steam account is missing.");
var binding = repo.Source(trackerId, account) ?? throw new InvalidDataException("The existing verified Legend source binding is required.");
var orderBinding = repo.Source(AchievementTrackingCatalog.DeathStrandingOrdersId, account)
    ?? throw new InvalidDataException("The official order tracker source binding is required.");
if (binding.AdapterId != "death-stranding-dc.fabrication" || binding.AdapterVersion != "1"
    || binding.Account != orderBinding.Account || binding.Location != orderBinding.Location
    || binding.GameDirectory != orderBinding.GameDirectory || binding.SpecificSave != orderBinding.SpecificSave)
    throw new InvalidDataException("The existing approved save paths must match the official order tracker.");

using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = repo.DatabasePath, Pooling = false }.ToString());
connection.Open();
Dictionary<string, string> Audit()
{
    var result = new Dictionary<string, string>();
    using var list = connection.CreateCommand();
    list.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
    var tables = new List<string>();
    using (var rows = list.ExecuteReader()) while (rows.Read()) tables.Add(rows.GetString(0));
    foreach (var table in tables)
    {
        var quoted = "\"" + table.Replace("\"", "\"\"") + "\"";
        using var fields = connection.CreateCommand(); fields.CommandText = "PRAGMA table_info(" + quoted + ")";
        var names = new HashSet<string>();
        using (var rows = fields.ExecuteReader()) while (rows.Read()) names.Add(rows.GetString(1));
        var filter = names.Contains("tracker_id") ? " WHERE tracker_id<>$id" : table == "century_progress" ? " WHERE id<>$id" : "";
        using var command = connection.CreateCommand(); command.CommandText = "SELECT * FROM " + quoted + filter;
        if (filter.Length > 0) command.Parameters.AddWithValue("$id", table == "century_progress" ? achievementId : trackerId);
        var records = new List<string>();
        using (var rows = command.ExecuteReader())
            while (rows.Read()) { var values = new object[rows.FieldCount]; rows.GetValues(values); records.Add(JsonSerializer.Serialize(values)); }
        result[table] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", records.Order(StringComparer.Ordinal)))));
    }
    return result;
}

var before = Audit();
string previousFingerprint;
long previousObservedUtc;
using (var command = connection.CreateCommand())
{
    command.CommandText = "SELECT status FROM tracker_source WHERE tracker_id=$tracker AND account=$account";
    command.Parameters.AddWithValue("$tracker", trackerId); command.Parameters.AddWithValue("$account", account);
    using var status = JsonDocument.Parse((string?)command.ExecuteScalar() ?? throw new InvalidDataException("The prior save status is missing."));
    previousFingerprint = status.RootElement.GetProperty("Fingerprint").GetString()!;
    previousObservedUtc = status.RootElement.GetProperty("SourceObservedUtc").GetInt64();
}
var backupFolder = Path.Combine(root, "achievement-tracking", "installation-backups"); Directory.CreateDirectory(backupFolder);
var backup = Path.Combine(backupFolder, "before-century-legends-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff") + ".sqlite");
using (var target = new SqliteConnection("Data Source=" + backup + ";Pooling=False")) { target.Open(); connection.BackupDatabase(target); }

var assembly = typeof(MainWindow).Assembly;
const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
var store = Activator.CreateInstance(assembly.GetType("PixelVaultNative.AchievementTrackingStore")!, flags, null, new object[] { root }, null)!;
var source = Activator.CreateInstance(assembly.GetType("PixelVaultNative.DeathStrandingTrackerSource")!, flags, null,
    new object?[] { Path.GetDirectoryName(repo.DatabasePath), worker, null, null }, null)!;
var adapters = Array.CreateInstance(assembly.GetType("PixelVaultNative.ITrackerDataSource")!, 1); adapters.SetValue(source, 0);
var service = Activator.CreateInstance(assembly.GetType("PixelVaultNative.TrackerRefreshService")!, flags, null, new object[] { store, adapters }, null)!;
var refresh = (Task)service.GetType().GetMethod("PrepareAsync", flags)!.Invoke(service,
    new object[] { trackerId, account, CancellationToken.None, false })!;
await refresh;
var plan = (TrackerRefreshPlan)refresh.GetType().GetProperty("Result")!.GetValue(refresh)!;
if (plan.Rows.Count != CenturyLegendsCatalogue.Total) throw new InvalidDataException("The complete order catalogue is required.");
var sameSavedSnapshot = plan.Fingerprint == previousFingerprint && plan.ObservedUtc == previousObservedUtc;
if (plan.HasRollback && !sameSavedSnapshot)
    throw new InvalidDataException("The order save is older than the last accepted source snapshot.");
repo.ApplyRefresh(plan, sameSavedSnapshot, [], CancellationToken.None);

var progress = repo.Century("steam", "1850570", achievementId, account)
    ?? throw new InvalidDataException("The verified Legend progress was not recorded.");
var states = repo.Load(trackerId, account);
var effectiveTotals = TrackerTotals.Calculate(definition, states);
var sourceSnapshot = JsonSerializer.Deserialize<AchievementTrackerSnapshot>(plan.Json, AchievementTrackingCatalog.Json)!;
var sourceTotals = TrackerTotals.FromVerifiedSnapshot(definition, sourceSnapshot);
var order430 = states.Single(s => s.Item.Identifiers.GetValueOrDefault("OrderNumber") == "430");
if (order430.ReportedCurrent != 1 || progress.Current != sourceTotals.Completed || progress.Target != 540)
    throw new InvalidDataException("Order 430 or the aggregate Legend progress did not match the current save.");
var after = Audit();
foreach (var pair in before)
    if (!after.TryGetValue(pair.Key, out var hash) || hash != pair.Value)
        throw new InvalidDataException("Unrelated saved rows changed: " + pair.Key);

Directory.CreateDirectory(Path.GetDirectoryName(receipt)!);
var result = new
{
    Passed = true, Tracker = trackerId, Achievement = achievementId, progress.Current, progress.Target,
    progress.Unlocked, progress.EarnedPoints, SourceCompleted = sourceTotals.Completed, SourceTotal = sourceTotals.Total,
    EffectiveCompleted = effectiveTotals.Completed, EffectiveTotal = effectiveTotals.Total, Order430 = order430.ReportedCurrent,
    Order430Source = order430.Source, Order430ObservedUtc = order430.ObservedUtcTicks,
    AllOtherTrackerRowsUnchanged = true, SourcePathsReused = true,
    SameSavedSnapshotReinterpreted = sameSavedSnapshot, Backup = backup,
    progress.ObservedUtc, progress.VerifiedUtc
};
File.WriteAllText(receipt, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(result));
