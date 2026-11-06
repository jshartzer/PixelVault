using System.Text.Json;
using Microsoft.Data.Sqlite;
using PixelVaultNative;

SQLitePCL.Batteries_V2.Init();
if(args.Length!=3) throw new ArgumentException("application-root bridge-root receipt-path");
var app=Path.GetFullPath(args[0]);var bridge=Path.GetFullPath(args[1]);
using var config=JsonDocument.Parse(File.ReadAllText(Path.Combine(bridge,"config.json")));
var account=config.RootElement.GetProperty("account").GetString()!;
var definitions=config.RootElement.GetProperty("trackers").EnumerateArray()
    .Select(t=>JsonSerializer.Deserialize<AchievementTrackerDefinition>(t.GetProperty("definition").GetRawText(),AchievementTrackingCatalog.Json)!).ToArray();
if(definitions.Length!=20||definitions.Any(d=>d.Provider!="steam"||d.GameId!="3812600"||!d.Id.StartsWith("restory.")||string.IsNullOrWhiteSpace(d.AchievementId)))throw new InvalidDataException();
var repo=new TrackerRepository(app,pooling:false,initializeSchema:false);repo.ValidateSharedSchema();
foreach(var definition in definitions)AchievementTrackingCatalog.Validate(definition);
if(repo.Definitions().Any(d=>definitions.Any(x=>x.Id==d.Id)))throw new InvalidOperationException("A requested Restory tracker already exists; preserve it and review before retrying.");
var backup=Path.Combine(app,"achievement-tracking","installation-backups","before-restory-"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff")+".sqlite");
Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
using(var source=new SqliteConnection("Data Source="+repo.DatabasePath+";Pooling=False"))
using(var target=new SqliteConnection("Data Source="+backup+";Pooling=False")) {source.Open();target.Open();source.BackupDatabase(target);}
var results=new List<object>();
foreach(var definition in definitions)
{
    var progressPath=Path.Combine(bridge,"progress",definition.Id+".json");
    var snapshot=File.ReadAllText(progressPath);
    repo.AddDefinition(definition);
    var count=repo.Import(snapshot,account,definition.Id);
    var binding=new TrackerSourceBinding{TrackerId=definition.Id,Account=account,AdapterId="pixelvault.json-file",AdapterVersion="1",
        Location=progressPath,Profile="Restory — local profile 1",KeepSnapshots=true,ReplaceManualProgress=false};
    repo.SaveSource(binding);
    repo.SaveAutomation(definition.Id,account,binding.Id,TrackerAutomationMode.SaveChanges,1,false);
    results.Add(new{definition.Id,definition.Title,definition.AchievementId,Items=definition.Items.Count,Imported=count,Automatic=repo.Automation(definition.Id,account).Settings});
}
File.WriteAllText(args[2],JsonSerializer.Serialize(new{Backup=backup,Trackers=results},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"Created {results.Count} linked Restory trackers with Save Changes automatic refresh.");
