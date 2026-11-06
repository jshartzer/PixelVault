using System.Reflection;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PixelVault.Tracking;
using PixelVaultNative;

// Explicit installation tool. Does not run on application startup or modify an existing tracker.
SQLitePCL.Batteries_V2.Init();
if(args.Length!=4)throw new ArgumentException("Arguments: application-data-root workspace-manifest worker-exe receipt-path");
var root=Path.GetFullPath(args[0]);var worker=Path.GetFullPath(args[2]);var receipt=Path.GetFullPath(args[3]);
if(!File.Exists(worker))throw new FileNotFoundException("The reviewed headless reader is missing.");
using var manifest=JsonDocument.Parse(File.ReadAllText(args[1]));
var accounts=manifest.RootElement.GetProperty("Accounts").EnumerateObject().ToDictionary(p=>p.Name,p=>p.Value.GetString()!);
var repo=new TrackerRepository(root,pooling:false,initializeSchema:false);repo.ValidateSharedSchema();
using var backgroundOwner=TrackerSourceLease.OwnBackground(repo.DatabasePath);
var priorSource=repo.SourcesForAccounts(accounts).Single(s=>s.Definition.Id==AchievementTrackingCatalog.DeathStrandingOrdersId).Binding;
if(priorSource.AdapterId!="death-stranding-dc.fabrication"||priorSource.AdapterVersion!="1")throw new InvalidDataException("The existing validated standard-order source is required.");
var tracker=CenturyCargoGoal.TrackerId;var achievement=CenturyAchievementCatalog.DeathStrandingCargoId;
if(!CenturyCargoGoal.Matches(repo.Definitions().Single(d=>d.Id==tracker)))throw new InvalidDataException("Unexpected cargo-weight catalogue.");
using var connection=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=repo.DatabasePath,Pooling=false}.ToString());connection.Open();
Dictionary<string,string> Audit()
{
    var result=new Dictionary<string,string>();using var list=connection.CreateCommand();list.CommandText="SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
    var tables=new List<string>();using(var rows=list.ExecuteReader())while(rows.Read())tables.Add(rows.GetString(0));
    foreach(var table in tables)
    {
        var quoted="\""+table.Replace("\"","\"\"")+"\"";
        using var fields=connection.CreateCommand();fields.CommandText="PRAGMA table_info("+quoted+")";var names=new HashSet<string>();using(var r=fields.ExecuteReader())while(r.Read())names.Add(r.GetString(1));
        var filter=names.Contains("tracker_id")?" WHERE tracker_id<>$id":table=="century_progress"?" WHERE id<>$id":"";
        using var command=connection.CreateCommand();command.CommandText="SELECT * FROM "+quoted+filter;
        if(filter.Length>0)command.Parameters.AddWithValue("$id",table=="century_progress"?achievement:tracker);
        var records=new List<string>();using(var rows=command.ExecuteReader())while(rows.Read()){var values=new object[rows.FieldCount];rows.GetValues(values);records.Add(JsonSerializer.Serialize(values));}
        result[table]=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n",records.Order(StringComparer.Ordinal)))));
    }
    return result;
}
var before=Audit();var backupFolder=Path.Combine(root,"achievement-tracking","installation-backups");Directory.CreateDirectory(backupFolder);
var backup=Path.Combine(backupFolder,"before-century-cargo-"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff")+".sqlite");
using(var target=new SqliteConnection("Data Source="+backup+";Pooling=False")){target.Open();connection.BackupDatabase(target);}
var binding=repo.Source(tracker,priorSource.Account);
if(binding==null)
{
    binding=JsonSerializer.Deserialize<TrackerSourceBinding>(JsonSerializer.Serialize(priorSource,AchievementTrackingCatalog.Json),AchievementTrackingCatalog.Json)!;
    binding.TrackerId=tracker;binding.Id=null;binding.ReplaceManualProgress=false;repo.SaveSource(binding);
}
else if(binding.AdapterId!=priorSource.AdapterId||binding.AdapterVersion!=priorSource.AdapterVersion||binding.Location!=priorSource.Location||binding.GameDirectory!=priorSource.GameDirectory||binding.SpecificSave!=priorSource.SpecificSave)
    throw new InvalidOperationException("The cargo tracker already has a different source; its settings were preserved.");
var assembly=typeof(MainWindow).Assembly;const BindingFlags flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
var store=Activator.CreateInstance(assembly.GetType("PixelVaultNative.AchievementTrackingStore")!,flags,null,new object[]{root},null)!;
var source=Activator.CreateInstance(assembly.GetType("PixelVaultNative.DeathStrandingTrackerSource")!,flags,null,new object?[]{Path.GetDirectoryName(repo.DatabasePath),worker,null,null},null)!;
var adapters=Array.CreateInstance(assembly.GetType("PixelVaultNative.ITrackerDataSource")!,1);adapters.SetValue(source,0);
var service=Activator.CreateInstance(assembly.GetType("PixelVaultNative.TrackerRefreshService")!,flags,null,new object[]{store,adapters},null)!;
var prepare=(Task)service.GetType().GetMethod("PrepareAsync",flags)!.Invoke(service,new object[]{tracker,binding.Account,CancellationToken.None,false})!;
await prepare;var plan=(TrackerRefreshPlan)prepare.GetType().GetProperty("Result")!.GetValue(prepare)!;
if(plan.Rows.Count!=1||plan.HasRollback)throw new InvalidDataException("The source observation needs review; older progress was not accepted.");
repo.ApplyRefresh(plan,false,[],CancellationToken.None);
var automatic=repo.Automation(tracker,binding.Account).Settings;
if(string.IsNullOrEmpty(automatic.Id))
{
    var prior=repo.Automation(priorSource.TrackerId,priorSource.Account).Settings;
    // New tracker only: reuse the order tracker's active schedule, retaining a paused policy.
    repo.SaveAutomation(tracker,binding.Account,binding.Id,prior.Mode==TrackerAutomationMode.Manual?TrackerAutomationMode.SaveChanges:prior.Mode,
        prior.IntervalMinutes>0?prior.IntervalMinutes:5,prior.Paused);
}
var progress=repo.Century("steam","1850570",achievement,binding.Account)??throw new InvalidDataException("No verified delivered-weight observation was recorded.");
automatic=repo.Automation(tracker,binding.Account).Settings;
var after=Audit();foreach(var pair in before)if(!after.TryGetValue(pair.Key,out var hash)||hash!=pair.Value)throw new InvalidDataException("Unrelated saved rows changed: "+pair.Key);
Directory.CreateDirectory(Path.GetDirectoryName(receipt)!);
var totals=TrackerTotals.Calculate(repo.Definitions().Single(d=>d.Id==tracker),repo.Load(tracker,binding.Account));
var result=new{Passed=true,Tracker=tracker,Achievement=achievement,progress.Current,progress.Target,progress.Unlocked,progress.EarnedPoints,
    totals.AggregateCompleted,totals.AggregateTotal,
    AllOtherTrackerRowsUnchanged=true,SourcePathsReused=true,SourceAuthorityInherited=false,AutomaticMode=automatic.Mode.ToString(),automatic.Paused,Backup=backup,ObservedUtc=progress.ObservedUtc};
File.WriteAllText(receipt,JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine(JsonSerializer.Serialize(result));
