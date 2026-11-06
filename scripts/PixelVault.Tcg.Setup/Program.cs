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
if(definitions.Length!=12||definitions.Any(d=>d.Provider!="steam"||d.GameId!="3070070"||!d.Id.StartsWith("tcg.")))throw new InvalidDataException();
var repo=new TrackerRepository(app,pooling:false,initializeSchema:false);repo.ValidateSharedSchema();
foreach(var definition in definitions)AchievementTrackingCatalog.Validate(definition);
if(repo.Definitions().Any(d=>definitions.Any(x=>x.Id==d.Id)))throw new InvalidOperationException("A requested TCG tracker already exists; preserve it and review before retrying.");
foreach(var d in definitions)
{
    var s=JsonSerializer.Deserialize<AchievementTrackerSnapshot>(File.ReadAllText(Path.Combine(bridge,"progress",d.Id+".json")),AchievementTrackingCatalog.Json)!;
    if(s.TrackerId!=d.Id||s.Account!=account||s.GameId!=d.GameId||s.Items.Count!=d.Items.Count||s.Evidence.Length>4000)
        throw new InvalidDataException("Snapshot scope or coverage mismatch.");
}
var backup=Path.Combine(app,"achievement-tracking","installation-backups","before-tcg-"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff")+".sqlite");
Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
using(var source=new SqliteConnection("Data Source="+repo.DatabasePath+";Pooling=False"))
using(var target=new SqliteConnection("Data Source="+backup+";Pooling=False")) {source.Open();target.Open();source.BackupDatabase(target);}
var results=new List<object>();
foreach(var definition in definitions)
{
    var progressPath=Path.Combine(bridge,"progress",definition.Id+".json");
    repo.AddDefinition(definition);
    var count=repo.Import(File.ReadAllText(progressPath),account,definition.Id);
    var binding=new TrackerSourceBinding{TrackerId=definition.Id,Account=account,AdapterId="pixelvault.json-file",AdapterVersion="1",
        Location=progressPath,Profile="TCG — Ben & Frank's autosave",KeepSnapshots=true,ReplaceManualProgress=false};
    repo.SaveSource(binding);
    repo.SaveAutomation(definition.Id,account,binding.Id,TrackerAutomationMode.SaveChanges,1,false);
    var editor=repo.ReadEditor(definition.Id,account);
    if(editor.States.Count!=definition.Items.Count||editor.States.Any(s=>!s.IsKnown))throw new InvalidDataException("Installed observations are incomplete.");
    results.Add(new{definition.Id,definition.Title,definition.AchievementId,Items=definition.Items.Count,Imported=count,
        Totals=editor.Totals,Automatic=repo.Automation(definition.Id,account).Settings});
}
File.WriteAllText(args[2],JsonSerializer.Serialize(new{Backup=backup,Trackers=results},new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine($"Created {results.Count} TCG trackers with save-change refresh and preserved manual overrides.");
