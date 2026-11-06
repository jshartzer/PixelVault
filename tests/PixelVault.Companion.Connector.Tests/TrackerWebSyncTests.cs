using System.Text.Json;
using Microsoft.Data.Sqlite;
using PixelVault.Companion;
using PixelVault.Companion.Connector;
using PixelVaultNative;
using Xunit;

public sealed class TrackerWebSyncTests:IDisposable
{
    readonly string root=Path.Combine(Path.GetTempPath(),"PV-WebReturn-"+Guid.NewGuid().ToString("N"));
    const string Id="custom.return",Account="approved";static readonly string Workspace=new('A',64);
    TrackerRepository Native=>new(Path.Combine(root,"pc"),false);TrackerRepository Web=>new(Path.Combine(root,"nas"),false);
    long copied;
    ConnectorPairing Pair=>new("http://127.0.0.1/",Workspace,new string('B',64),[],new(){["steam"]=Account},Path.Combine(root,"pc"),WebTrackerEditsEnabled:true);
    public TrackerWebSyncTests()
    {
        SQLitePCL.Batteries_V2.Init();Native.AddDefinition(new(){Id=Id,Provider="steam",GameId="42",GameTitle="Test",Title="Regions",Items=new(){new(){Id="one",Name="First"},new(){Id="count",Name="Counter",Target=10},new(){Id="archived",Name="Archived",Archived=true}}});
        Native.SetManualProgress(Id,Account,"count",2);Native.SaveNote(Id,Account,"count","Original note");Native.SaveNote(Id,"other","count","Other account");
        Directory.CreateDirectory(Path.Combine(root,"nas","achievement-tracking"));using var pc=new SqliteConnection("Data Source="+Native.DatabasePath);pc.Open();using var nas=new SqliteConnection("Data Source="+Web.DatabasePath);nas.Open();pc.BackupDatabase(nas);copied=DateTime.UtcNow.Ticks;
    }
    void Edit(string item,string field,int? current=null,string? note=null)
    {
        var e=Web.ReadEditor(Id,Account);Web.Edit(Id,Account,new(item,field,current,note!,field=="note"?e.Versions[item].Note:e.Versions[item].Progress,Guid.NewGuid().ToString()));
    }
    TrackerWebPage Page()=>new(Workspace,Id,"steam","42",Web.WebAuthoredFields(Id,Account,copied),null);
    TrackerWebResult Sync()=>TrackerWebSync.Apply(Pair,Path.Combine(root,"private"),Page());
    [Fact] public void WebCheckoffsCountsAndNotesReachPcWithBackupAndIdempotentRestartReceipts()
    {
        Assert.Empty(Page().Fields);Edit("one","progress",1);Edit("count","progress",7);Edit("count","note",note:"Saved on phone");
        Assert.Equal(3,Sync().Applied);Assert.True(Native.Load(Id,Account).Single(s=>s.Item.Id=="one").Complete);Assert.Equal(7,Native.Load(Id,Account).Single(s=>s.Item.Id=="count").ManualCurrent);Assert.Equal("Saved on phone",Native.Load(Id,Account).Single(s=>s.Item.Id=="count").Note);
        Assert.Equal("Other account",Native.Load(Id,"other").Single(s=>s.Item.Id=="count").Note);var history=Native.History(Id,Account).Count;
        Assert.Equal(0,Sync().Applied);Assert.Equal(history,Native.History(Id,Account).Count);Assert.Single(Directory.GetFiles(Path.Combine(root,"private","backups","tracker-web-edits")));
        using var backup=new SqliteConnection("Data Source="+Directory.GetFiles(Path.Combine(root,"private","backups","tracker-web-edits")).Single());backup.Open();using var cmd=backup.CreateCommand();cmd.CommandText="SELECT manual_current FROM tracker_item_state WHERE item_id='count' AND account='approved'";Assert.Equal(2L,cmd.ExecuteScalar());
    }
    [Fact] public void PreviewReportsRealConflictsAndRollsBackAllFieldAndReceiptChanges()
    {
        Edit("one","progress",1);Edit("count","progress",4);Native.SetManualProgress(Id,Account,"count",8);var history=Native.History(Id,Account).Count;
        var result=TrackerWebSync.Apply(Pair,Path.Combine(root,"private"),Page(),preview:true);Assert.Equal(1,result.Applied);Assert.Equal(1,result.Conflicts);Assert.Equal(history,Native.History(Id,Account).Count);Assert.False(Native.Load(Id,Account).Single(s=>s.Item.Id=="one").Complete);Assert.Empty(Native.WebSyncConflicts());
        Assert.Equal(1,Sync().Applied);Assert.Single(Native.WebSyncConflicts());
    }
    sealed class ResponseHandler(Func<HttpRequestMessage,HttpResponseMessage> respond):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)=>Task.FromResult(respond(request));
    }
    [Fact] public async Task NetworkReturnLoopContinuesPastUnavailableCatalogueItemsAndReplaysSafely()
    {
        Edit("one","progress",1);Edit("count","progress",4);var page=Page();page=page with{Fields=page.Fields.Select(f=>f.ItemId=="one"?f with{Name="Different PC catalogue"}:f).ToArray()};
        using var handler=new ResponseHandler(request=>request.RequestUri!.AbsolutePath.EndsWith("/"+Id,StringComparison.Ordinal)?new(System.Net.HttpStatusCode.OK){Content=System.Net.Http.Json.JsonContent.Create(page)}:new(System.Net.HttpStatusCode.NotFound));using var http=new HttpClient(handler){BaseAddress=new Uri("http://127.0.0.1/")};
        var result=await TrackerWebSync.OnceAsync(http,Pair,Path.Combine(root,"private"),default);Assert.Equal(new TrackerWebResult(1,0,1),result);Assert.Equal(4,Native.Load(Id,Account).Single(s=>s.Item.Id=="count").ManualCurrent);Assert.False(Native.Load(Id,Account).Single(s=>s.Item.Id=="one").Complete);
        Assert.Equal(new TrackerWebResult(0,0,1),await TrackerWebSync.OnceAsync(http,Pair,Path.Combine(root,"private"),default));
    }
    [Fact] public void ConcurrentPcEditAndAbaArePreservedUntilExplicitVersionGuardedReview()
    {
        Edit("count","progress",3);Sync();Native.SetManualProgress(Id,Account,"count",8);Native.SetManualProgress(Id,Account,"count",3);Edit("count","progress",4);
        Assert.Equal(1,Sync().Conflicts);Assert.Equal(3,Native.Load(Id,Account).Single(s=>s.Item.Id=="count").ManualCurrent);
        var conflict=Assert.Single(Native.WebSyncConflicts(Id,Account));Native.SetManualProgress(Id,Account,"count",9);Assert.Throws<InvalidOperationException>(()=>Native.ResolveWebSync(conflict,true));
        conflict=Assert.Single(Native.WebSyncConflicts(Id,Account));Native.ResolveWebSync(conflict,true);Assert.Empty(Native.WebSyncConflicts());Assert.Equal(4,Native.Load(Id,Account).Single(s=>s.Item.Id=="count").ManualCurrent);
        Edit("count","progress",5);Assert.Equal(1,Sync().Applied);
    }
    [Fact] public void BootstrapConflictKeepsBothValuesAndKeepingPcAcknowledgesOnlyThatWebRevision()
    {
        Native.SetManualProgress(Id,Account,"count",9);Edit("count","progress",4);Assert.Equal(1,Sync().Conflicts);Native.ResolveWebSync(Assert.Single(Native.WebSyncConflicts()),false);
        Assert.Equal(0,Sync().Applied);Assert.Equal(9,Native.Load(Id,Account).Single(s=>s.Item.Id=="count").ManualCurrent);Edit("count","progress",5);Assert.Equal(1,Sync().Applied);
    }
    [Fact] public void WebUndoAndClearedNotesPropagateWithoutChangingSourceObservations()
    {
        var snapshot=new AchievementTrackerSnapshot{TrackerId=Id,Provider="steam",GameId="42",Account=Account,Source="Source",ObservedUtcTicks=DateTime.UtcNow.Ticks,Items=new(){new(){ItemId="count",Current=6}}};Native.Import(JsonSerializer.Serialize(snapshot),Account);
        Edit("count","progress",4);Edit("count","note",note:"");Sync();var h=Web.History(Id,Account).First(h=>h.Kind=="manual");Web.UndoCommand(Id,Account,h.Id,Guid.NewGuid().ToString());Sync();
        var state=Native.Load(Id,Account).Single(s=>s.Item.Id=="count");Assert.Equal(2,state.ManualCurrent);Assert.Equal(6,state.ReportedCurrent);Assert.Equal("",state.Note);
    }
    [Fact] public void WrongWorkspaceAccountGameTargetAndDisabledPermissionCannotWrite()
    {
        Edit("count","progress",4);var page=Page();Assert.Throws<InvalidDataException>(()=>TrackerWebSync.Apply(Pair with{WebTrackerEditsEnabled=false},root,page));Assert.Throws<InvalidDataException>(()=>TrackerWebSync.Apply(Pair,root,page with{WorkspaceId=new string('C',64)}));Assert.Throws<InvalidDataException>(()=>TrackerWebSync.Apply(Pair,root,page with{GameId="43"}));
        Assert.Throws<InvalidDataException>(()=>TrackerWebSync.Apply(Pair,root,page with{Fields=page.Fields.Select(f=>f with{Target=99}).ToArray()}));Assert.Equal(2,Native.Load(Id,Account).Single(s=>s.Item.Id=="count").ManualCurrent);
    }
    [Fact] public void ProtectedGameCompletionIsNotUncheckedByWebManualProgress()
    {
        var snapshot=JsonSerializer.Serialize(new AchievementTrackerSnapshot{TrackerId=Id,Provider="steam",GameId="42",Account=Account,Source="Game",ObservedUtcTicks=DateTime.UtcNow.Ticks,Items=new(){new(){ItemId="one",Current=1}}});Native.ApplyRefresh(Native.Preview(snapshot,Id,Account,"death-stranding-dc.fabrication","1","fact"),false,[],default);
        Edit("one","progress",0);Assert.Equal(1,Sync().Conflicts);Assert.True(Native.Load(Id,Account).Single(s=>s.Item.Id=="one").Complete);Assert.Throws<InvalidOperationException>(()=>Native.ResolveWebSync(Assert.Single(Native.WebSyncConflicts()),true));
    }
    public void Dispose(){SqliteConnection.ClearAllPools();Directory.Delete(root,true);}
}
