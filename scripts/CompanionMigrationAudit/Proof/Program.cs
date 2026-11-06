using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PixelVault.Companion;
using PixelVaultNative;

if(args.Length<1)throw new ArgumentException("Use Proof <staged-direct-local.json> [--child <command-file>].");
var config=Path.GetFullPath(args[0]);var stage=Path.GetDirectoryName(config)!;
using var document=JsonDocument.Parse(File.ReadAllText(config));
var options=JsonSerializer.Deserialize<CompanionOptions>(document.RootElement.GetProperty("Companion").GetRawText())!;
if(!File.Exists(Path.Combine(stage,"stage-manifest.json"))||!File.Exists(Path.Combine(options.ApplicationDataRoot,"local-store-preparation.json")))throw new InvalidDataException("Only an explicitly staged rehearsal is supported.");
var workspace=new TrackerWorkspace(options);
var definition=workspace.Definitions().First(d=>d.Items.Any(i=>!i.Archived));
var account=workspace.Account(definition.Id);
long HistoryCount()
{
    using var c=LocalApplicationData.Read(workspace.Repository.DatabasePath);using var q=c.CreateCommand();q.CommandText="SELECT COUNT(*) FROM tracker_history";return Convert.ToInt64(q.ExecuteScalar());
}
if(args.Length==3&&args[1]=="--child")
{
    using var payload=JsonDocument.Parse(File.ReadAllText(args[2]));
    var command=JsonSerializer.Deserialize<TrackerEdit>(payload.RootElement.GetProperty("command").GetRawText())!;
    var tracker=payload.RootElement.GetProperty("tracker").GetString()!;
    // Same shared domain repository used by Native; this writer runs in a separate OS process.
    var result=new TrackerRepository(options.ApplicationDataRoot,pooling:false,initializeSchema:false).Edit(tracker,workspace.Account(tracker),command);
    Console.WriteLine(JsonSerializer.Serialize(result));return;
}
var watch=Stopwatch.StartNew();var state=new ConnectorSyncState(options,workspace);var achievements=new AchievementStore(options,workspace,state);var library=new GameLibrary(options,workspace,achievements);
var games=library.Games();var coldMs=watch.ElapsedMilliseconds;watch.Restart();_=library.Games();var warmMs=watch.ElapsedMilliseconds;
var rows=workspace.LocalData!.Achievements().Sum(g=>g.Rows.Count);
var editor=workspace.Repository.ReadEditor(definition.Id,account);var selected=editor.States.First(s=>!s.Item.Archived);
var commandPath=Path.Combine(stage,"proof-command.private.json");
var edit=new TrackerEdit(selected.Item.Id,"note",null,"Isolated shared-store verification",editor.Versions[selected.Item.Id].Note,Guid.NewGuid().ToString("N"));
File.WriteAllText(commandPath,JsonSerializer.Serialize(new{tracker=definition.Id,command=edit}));
var before=HistoryCount();
async Task<TrackerMutation> Child()
{
    var start=new ProcessStartInfo("dotnet"){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardError=true};
    foreach(var value in new[]{Assembly.GetExecutingAssembly().Location,config,"--child",commandPath})start.ArgumentList.Add(value);
    using var process=Process.Start(start)!;var output=await process.StandardOutput.ReadToEndAsync();var error=await process.StandardError.ReadToEndAsync();await process.WaitForExitAsync();if(process.ExitCode!=0)throw new InvalidOperationException(error);
    return JsonSerializer.Deserialize<TrackerMutation>(output)!;
}
var written=await Child();
if(workspace.Repository.Load(definition.Id,account).Single(s=>s.Item.Id==selected.Item.Id).Note!=edit.Note)throw new Exception("The child write is not visible to the web repository.");
bool staleRejected=false;
try{workspace.Repository.Edit(definition.Id,account,edit with{Note="Stale web draft",RequestId=Guid.NewGuid().ToString("N")});}
catch(InvalidOperationException){staleRejected=true;}
if(!staleRejected)throw new Exception("A stale cross-process edit was accepted.");
var replay=await Child();if(!replay.Replayed||HistoryCount()!=before+1)throw new Exception("A cross-process retry duplicated history.");
workspace.Repository.UndoCommand(definition.Id,account,written.HistoryId!.Value,Guid.NewGuid().ToString("N"));
if(workspace.Repository.Load(definition.Id,account).Single(s=>s.Item.Id==selected.Item.Id).Note!=selected.Note)throw new Exception("Undo did not restore the original note.");
var protectedChecked=0;
using var comparison=JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(stage)!,"comparison.json")));
var recovered=comparison.RootElement.GetProperty("differences").EnumerateArray()
    .Where(d=>d.GetProperty("area").GetString()=="source-observation"&&d.GetProperty("pc").GetProperty("reported_current").GetRawText()!=d.GetProperty("nas").GetProperty("reported_current").GetRawText())
    .Select(d=>string.Join("\n",d.GetProperty("key").EnumerateArray().Select(k=>k.GetString()))).ToHashSet();
var recoveredChecked=0;
foreach(var tracker in workspace.Definitions())
{
    var view=workspace.Repository.ReadEditor(tracker.Id,workspace.Account(tracker.Id));
    foreach(var item in view.States.Where(s=>view.ProtectedItems.Contains(s.Item.Id)))
    {
        bool rejected=false;
        try{workspace.Repository.Edit(tracker.Id,workspace.Account(tracker.Id),new(item.Item.Id,"progress",0,null,view.Versions[item.Item.Id].Progress,Guid.NewGuid().ToString("N")));}
        catch(InvalidOperationException){rejected=true;}
        if(!rejected)throw new Exception("A game-confirmed completion could be cleared.");
        protectedChecked++;
        if(recovered.Contains(tracker.Id+"\n"+workspace.Account(tracker.Id)+"\n"+item.Item.Id))recoveredChecked++;
    }
}
if(recoveredChecked!=recovered.Count)throw new Exception("A recovered completion is missing source protection.");
var resultProof=new{Games=games.Count,Achievements=rows,ColdMetadataMs=coldMs,WarmMetadataMs=warmMs,
    CrossProcessWriteVisible=true,StaleEditRejected=staleRejected,RetryReplayed=replay.Replayed,UndoRestored=true,
    ProtectedCompletionsChecked=protectedChecked,RecoveredCompletionsProtected=recoveredChecked,PhotosCopied=0,NasCalls=0,NativeUiRequired=false};
var json=JsonSerializer.Serialize(resultProof,new JsonSerializerOptions{WriteIndented=true});File.WriteAllText(Path.Combine(stage,"proof-result.json"),json);Console.WriteLine(json);
