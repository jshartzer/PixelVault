using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PixelVaultNative;
using Xunit;

namespace PixelVault.Companion.Tests;

public sealed partial class CompanionTests
{
    private const string TrackerId="custom.companion",Approved="approved-account",Other="other-account";
    private sealed class WorkspaceCase : IDisposable
    {
        public string Base {get;}=Path.Combine(Path.GetTempPath(),"CompanionEditing-"+Guid.NewGuid().ToString("N"));
        public string Source=>Path.Combine(Base,"desktop");
        public string Root=>Path.Combine(Base,"workspace");
        public TrackerRepository Desktop {get;}
        public WorkspaceCase(bool native=false)
        {
            Desktop=new TrackerRepository(Source,pooling:false);
            Desktop.AddDefinition(new(){Id=TrackerId,Provider="steam",GameId="42",GameTitle="Test journey",Title="A custom tracker",Items=new()
            {new(){Id="first",Name="First item",Category="Places"},new(){Id="count",Name="Collect five",Category="Places",Target=5},new(){Id="unknown",Name="Unchecked item"},new(){Id="archived",Name="Old item",Archived=true}}});
            Desktop.SaveNote(TrackerId,Approved,"first","Original note");Desktop.SetManualProgress(TrackerId,Other,"count",4);
            Desktop.SaveNote(TrackerId,Other,"first","Private other account note");
            var snapshot=JsonSerializer.Serialize(new AchievementTrackerSnapshot{TrackerId=TrackerId,Provider="steam",GameId="42",Account=Approved,
                Source="Game-looking source label",ObservedUtcTicks=DateTime.UtcNow.Ticks,Items=new(){new(){ItemId="first",Current=native?1:0},new(){ItemId="count",Current=2}}});
            var plan=Desktop.Preview(snapshot,TrackerId,Approved,native?"death-stranding-dc.fabrication":"json-import","1","fixture-observation");
            Desktop.ApplyRefresh(plan,false,[],default);
        }
        public void Prepare()=>PrepareWorkspace.Copy(Source,Root,new(){["steam"]=Approved});
        public TrackerRepository Repo()=>new(Root,pooling:false);
        public void Dispose(){SqliteConnection.ClearAllPools();if(Directory.Exists(Base))Directory.Delete(Base,true);}
    }
    private static async Task<JsonElement> Editor(HttpClient client)=>await client.GetFromJsonAsync<JsonElement>("/api/v1/trackers/"+TrackerId);
    private static TrackerEdit Edit(JsonElement view,string item,string field,int? current=null,string? note=null,string? request=null)=>
        new(item,field,current,note!,view.GetProperty("versions").GetProperty(item).GetProperty(field=="note"?"note":"progress").GetString()!,request??Guid.NewGuid().ToString());
    private static Task<HttpResponseMessage> Save(HttpClient client,TrackerEdit command)=>client.PostAsJsonAsync("/api/v1/trackers/"+TrackerId+"/items",command);

    [Fact]
    public void PreparationUsesConsistentBackupAndPreservesEveryAccountHistoryAndConfiguration()
    {
        using var fixture=new WorkspaceCase();
        var sourceFile=fixture.Desktop.DatabasePath;var before=SHA256.HashData(File.ReadAllBytes(sourceFile));
        var result=PrepareWorkspace.Copy(fixture.Source,fixture.Root,new(){["steam"]=Approved});
        Assert.Equal(4,result.States);Assert.True(result.History>=4);
        Assert.Equal(before,SHA256.HashData(File.ReadAllBytes(sourceFile)));
        Assert.Equal(fixture.Desktop.Load(TrackerId,Approved).Select(s=>(s.Current,s.Note)),fixture.Repo().Load(TrackerId,Approved).Select(s=>(s.Current,s.Note)));
        Assert.Equal(fixture.Desktop.Load(TrackerId,Other).Select(s=>(s.Current,s.Note)),fixture.Repo().Load(TrackerId,Other).Select(s=>(s.Current,s.Note)));
        Assert.Throws<InvalidDataException>(()=>fixture.Prepare());
        Assert.Throws<InvalidDataException>(()=>PrepareWorkspace.Copy(fixture.Source,fixture.Source));
        Assert.Throws<InvalidDataException>(()=>PrepareWorkspace.Copy(fixture.Source+Path.DirectorySeparatorChar,Path.Combine(fixture.Source,"nested")));
        Assert.Throws<InvalidDataException>(()=>PrepareWorkspace.Copy(fixture.Source,Path.Combine(fixture.Base,"ambiguous")));
        Assert.False(File.Exists(Path.Combine(fixture.Base,"ambiguous","workspace.json")));
    }

    [Fact]
    public async Task RealAccountReadsEditsCountsNotesAndReturnsToSourceWithoutTouchingDesktop()
    {
        using var fixture=new WorkspaceCase();fixture.Prepare();using var host=new Host(demo:false,root:fixture.Root);using var client=host.Client();await Setup(host,client);
        var view=await Editor(client);Assert.False(view.GetProperty("sample").GetBoolean());Assert.False(view.GetProperty("readOnly").GetBoolean());
        Assert.Equal(HttpStatusCode.OK,(await Save(client,Edit(view,"count","progress",4))).StatusCode);
        view=await Editor(client);Assert.Equal(4,view.GetProperty("states").EnumerateArray().Single(s=>s.GetProperty("item").GetProperty("id").GetString()=="count").GetProperty("current").GetInt32());
        Assert.Equal(HttpStatusCode.OK,(await Save(client,Edit(view,"first","note",note:"Saved on the phone"))).StatusCode);
        view=await Editor(client);Assert.Equal(HttpStatusCode.OK,(await Save(client,Edit(view,"count","source"))).StatusCode);
        Assert.Equal(2,fixture.Repo().Load(TrackerId,Approved).Single(s=>s.Item.Id=="count").Current);
        Assert.Equal("Saved on the phone",fixture.Repo().Load(TrackerId,Approved).Single(s=>s.Item.Id=="first").Note);
        Assert.Equal("Original note",fixture.Desktop.Load(TrackerId,Approved).Single(s=>s.Item.Id=="first").Note);
        Assert.Equal(2,fixture.Desktop.Load(TrackerId,Approved).Single(s=>s.Item.Id=="count").Current);
        Assert.Equal("Private other account note",fixture.Repo().Load(TrackerId,Other).Single(s=>s.Item.Id=="first").Note);
        var spoof=await client.GetStringAsync("/api/v1/trackers/"+TrackerId+"?account="+Other);
        Assert.DoesNotContain("Private other account note",spoof);
        Assert.DoesNotContain(Other,spoof);
    }

    [Fact]
    public async Task SameFieldConflictsButUnrelatedProgressAndNotesCanSaveIndependently()
    {
        using var fixture=new WorkspaceCase();fixture.Prepare();using var host=new Host(demo:false,root:fixture.Root);using var client=host.Client();await Setup(host,client);
        var baseline=await Editor(client);
        var writes=await Task.WhenAll(Save(client,Edit(baseline,"first","note",note:"Tab one")),Save(client,Edit(baseline,"first","note",note:"Tab two")));
        Assert.Single(writes,r=>r.StatusCode==HttpStatusCode.OK);Assert.Single(writes,r=>r.StatusCode==HttpStatusCode.Conflict);
        baseline=await Editor(client);
        Assert.Equal(HttpStatusCode.OK,(await Save(client,Edit(baseline,"count","progress",3))).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await Save(client,Edit(baseline,"count","note",note:"Independent note"))).StatusCode);
    }

    [Fact]
    public async Task RetryingAnAcknowledgementLossDoesNotDuplicateHistoryAndRequestIdsCannotBeReused()
    {
        using var fixture=new WorkspaceCase();fixture.Prepare();using var host=new Host(demo:false,root:fixture.Root);using var client=host.Client();await Setup(host,client);
        var before=fixture.Repo().History(TrackerId,Approved).Count;var command=Edit(await Editor(client),"first","note",note:"One save");
        Assert.Equal(HttpStatusCode.OK,(await Save(client,command)).StatusCode);
        var replay=await Save(client,command);Assert.Equal(HttpStatusCode.OK,replay.StatusCode);
        Assert.True((await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("result").GetProperty("replayed").GetBoolean());
        Assert.Equal(before+1,fixture.Repo().History(TrackerId,Approved).Count);
        Assert.Equal(HttpStatusCode.Conflict,(await Save(client,command with{Note="Different payload"})).StatusCode);
    }

    [Fact]
    public async Task UndoIsGuardedFieldScopedAndIdempotent()
    {
        using var fixture=new WorkspaceCase();fixture.Prepare();using var host=new Host(demo:false,root:fixture.Root);using var client=host.Client();await Setup(host,client);
        var changed=await Save(client,Edit(await Editor(client),"count","progress",4));var payload=await changed.Content.ReadFromJsonAsync<JsonElement>();
        var history=payload.GetProperty("result").GetProperty("historyId").GetInt64();
        await Save(client,Edit(await Editor(client),"count","note",note:"Keep later note"));
        var undo=new{requestId=Guid.NewGuid().ToString()};var url=$"/api/v1/trackers/{TrackerId}/history/{history}/undo";
        Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync(url,undo)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync(url,undo)).StatusCode);
        var state=fixture.Repo().Load(TrackerId,Approved).Single(s=>s.Item.Id=="count");Assert.Equal(2,state.Current);Assert.Equal("Keep later note",state.Note);
        changed=await Save(client,Edit(await Editor(client),"count","progress",3));history=(await changed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("result").GetProperty("historyId").GetInt64();
        await Save(client,Edit(await Editor(client),"count","progress",5));
        Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync($"/api/v1/trackers/{TrackerId}/history/{history}/undo",new{requestId=Guid.NewGuid().ToString()})).StatusCode);
    }

    [Fact]
    public async Task GameConfirmedValuesCannotBeUncheckedEvenAfterSourceDisconnection()
    {
        using var fixture=new WorkspaceCase(native:true);fixture.Prepare();using var host=new Host(demo:false,root:fixture.Root);using var client=host.Client();await Setup(host,client);
        var view=await Editor(client);Assert.Contains(view.GetProperty("protectedItems").EnumerateArray(),v=>v.GetString()=="first");
        Assert.Equal(HttpStatusCode.Conflict,(await Save(client,Edit(view,"first","progress",0))).StatusCode);
        Assert.Equal(1,fixture.Repo().Load(TrackerId,Approved).Single(s=>s.Item.Id=="first").Current);
        Assert.Equal(HttpStatusCode.OK,(await Save(client,Edit(view,"first","note",note:"Native note"))).StatusCode);
    }

    [Fact]
    public async Task TrackerWritesRequireAuthorizationCsrfActiveItemsAndValidValues()
    {
        using var fixture=new WorkspaceCase();fixture.Prepare();using var host=new Host(demo:false,root:fixture.Root);using var client=host.Client();
        Assert.Equal(HttpStatusCode.Unauthorized,(await Save(client,new("first","progress",1,null!,new string('0',64),Guid.NewGuid().ToString()))).StatusCode);
        await Setup(host,client);var view=await Editor(client);var command=Edit(view,"first","progress",1);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");Assert.Equal(HttpStatusCode.BadRequest,(await Save(client,command)).StatusCode);await Token(client);
        Assert.Equal(HttpStatusCode.BadRequest,(await Save(client,command with{Current=-1})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await Save(client,command with{ItemId="archived"})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await Save(client,command with{Field="reported"})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await Save(client,command with{RequestId="bad"})).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await Save(client,Edit(view,"first","note",note:new string('x',20000)))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await Save(client,Edit(view,"first","note",note:new string('x',20001)))).StatusCode);
    }

    [Fact]
    public void VersionsDetectChangeAndChangeBackAndDoNotCreateHistoryForNoOpRetries()
    {
        using var fixture=new WorkspaceCase();fixture.Prepare();var repo=fixture.Repo();var before=repo.ReadEditor(TrackerId,Approved);
        Assert.NotEqual(before.Versions["unknown"].Progress,before.Versions["unknown"].Note);
        Assert.Null(before.States.Single(s=>s.Item.Id=="unknown").Current);
        var stale=new TrackerEdit("first","note",null,"stale",before.Versions["first"].Note,Guid.NewGuid().ToString());
        repo.Edit(TrackerId,Approved,stale with{Note="Temporary"});
        var current=repo.ReadEditor(TrackerId,Approved);
        repo.Edit(TrackerId,Approved,stale with{Note="Original note",ExpectedVersion=current.Versions["first"].Note,RequestId=Guid.NewGuid().ToString()});
        Assert.Throws<InvalidOperationException>(()=>repo.Edit(TrackerId,Approved,stale with{RequestId=Guid.NewGuid().ToString()}));
        current=repo.ReadEditor(TrackerId,Approved);var count=repo.History(TrackerId,Approved).Count;
        var noOp=repo.Edit(TrackerId,Approved,new("first","note",null,"Original note",current.Versions["first"].Note,Guid.NewGuid().ToString()));
        Assert.Null(noOp.HistoryId);Assert.Equal(count,repo.History(TrackerId,Approved).Count);
    }

    [Fact]
    public void UndoVersionsRemainDistinctWhenTheClockDoesNotAdvance()
    {
        using var fixture=new WorkspaceCase();fixture.Prepare();var repo=fixture.Repo();
        TrackerMutation Note(string value){var view=repo.ReadEditor(TrackerId,Approved);return repo.Edit(TrackerId,Approved,new("first","note",null,value,view.Versions["first"].Note,Guid.NewGuid().ToString()));}
        var b=Note("B");var a=Note("Original note");var last=Note("B");
        void Undo(long id)
        {
            repo.UndoCommand(TrackerId,Approved,id,Guid.NewGuid().ToString());
            using var c=new SqliteConnection($"Data Source={repo.DatabasePath};Pooling=False");c.Open();using var command=c.CreateCommand();
            command.CommandText="UPDATE tracker_history SET undone_utc=1 WHERE undone_utc>0";command.ExecuteNonQuery();
        }
        Undo(last.HistoryId!.Value);var stale=repo.ReadEditor(TrackerId,Approved).Versions["first"].Note;
        Undo(a.HistoryId!.Value);Undo(b.HistoryId!.Value);
        Assert.Equal("Original note",repo.Load(TrackerId,Approved).Single(s=>s.Item.Id=="first").Note);
        Assert.Throws<InvalidOperationException>(()=>repo.Edit(TrackerId,Approved,new("first","note",null,"Stale",stale,Guid.NewGuid().ToString())));
    }

    [Fact]
    public void SourceLabelsDoNotManufactureGameProvenanceAndArchivedChangesAreRejected()
    {
        using var fixture=new WorkspaceCase();fixture.Prepare();var repo=fixture.Repo();
        var json=JsonSerializer.Serialize(new AchievementTrackerSnapshot{TrackerId=TrackerId,Provider="steam",GameId="42",Account=Approved,
            Source="Native game confirmed",ObservedUtcTicks=DateTime.UtcNow.AddSeconds(1).Ticks,Items=new(){new(){ItemId="first",Current=1}}});
        var plan=repo.Preview(json,TrackerId,Approved,"json-import","1","label-is-not-authority");repo.ApplyRefresh(plan,false,[],default);
        var view=repo.ReadEditor(TrackerId,Approved);Assert.DoesNotContain("first",view.ProtectedItems);
        repo.Edit(TrackerId,Approved,new("first","progress",0,null!,view.Versions["first"].Progress,Guid.NewGuid().ToString()));
        Assert.Equal(0,repo.Load(TrackerId,Approved).Single(s=>s.Item.Id=="first").Current);
        Assert.Throws<InvalidDataException>(()=>repo.Edit(TrackerId,Approved,new("archived","progress",1,null!,new string('0',64),Guid.NewGuid().ToString())));
    }

    [Fact]
    public async Task NetEightClientAndBrowserUseTheSameServerAndReturnConflictsWithoutOverwriting()
    {
        using var fixture=new WorkspaceCase();fixture.Prepare();using var host=new Host(demo:false,root:fixture.Root);
        using var browser=host.Client();await Setup(host,browser);
        using var desktopHttp=host.Client();var desktop=new PixelVault.Companion.Client.CompanionClient(desktopHttp);
        await desktop.SignInAsync("Owner","test-only-password-123");
        var view=await desktop.TrackerAsync(TrackerId);Assert.False(view.ReadOnly);
        var command=new TrackerEdit("first","note",null,"Desktop client note",view.Versions["first"].Note,Guid.NewGuid().ToString());
        await Save(browser,Edit(await Editor(browser),"first","note",note:"Browser note"));
        var error=await Assert.ThrowsAsync<PixelVault.Companion.Client.CompanionRequestException>(()=>desktop.EditAsync(TrackerId,command));
        Assert.Equal(HttpStatusCode.Conflict,error.Status);
        view=await desktop.TrackerAsync(TrackerId);
        var saved=await desktop.EditAsync(TrackerId,command with{ExpectedVersion=view.Versions["first"].Note,RequestId=Guid.NewGuid().ToString()});
        Assert.Equal("Desktop client note",(await Editor(browser)).GetProperty("states").EnumerateArray().Single(s=>s.GetProperty("item").GetProperty("id").GetString()=="first").GetProperty("note").GetString());
        await desktop.UndoAsync(TrackerId,saved.Result.HistoryId!.Value,Guid.NewGuid().ToString());
        Assert.Equal("Browser note",(await desktop.TrackerAsync(TrackerId)).States.Single(s=>s.Item.Id=="first").Note);
        await desktop.SignOutAsync();
        Assert.Equal(HttpStatusCode.Unauthorized,(await Assert.ThrowsAsync<PixelVault.Companion.Client.CompanionRequestException>(()=>desktop.TrackerAsync(TrackerId))).Status);
    }
}
