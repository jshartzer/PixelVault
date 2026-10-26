using System.Net;
using System.Net.Http.Json;
using PixelVault.Companion.Client;
using Xunit;

namespace PixelVault.Companion.Tests;

public sealed partial class CompanionTests
{
    [Fact]
    public async Task DesktopDraftsSurviveIndependentSavesReloadsAndOtherWindowsUntilExplicitConflictChoice()
    {
        using var fixture=new WorkspaceCase();fixture.Prepare();using var host=new Host(false,root:fixture.Root);
        using var browser=host.Client();await Setup(host,browser);
        using var desktopHttp=host.Client();var client=new CompanionClient(desktopHttp);await client.SignInAsync("Owner","test-only-password-123");
        var workspace=await client.WorkspaceAsync();Assert.Equal("isolated-copy",workspace.Mode);Assert.Equal(Approved,workspace.Accounts["steam"]);
        Assert.Contains(await client.TrackersAsync(),t=>t.Id==TrackerId);
        var first=new CompanionEditor(client,TrackerId);var second=new CompanionEditor(client,TrackerId);await first.RefreshAsync();await second.RefreshAsync();
        first.Note("first","An unsaved desktop note");var original=first.Draft("first","note")!;
        first.Progress("count",4);await first.SaveAsync("count","progress");
        Assert.Equal(original,first.Draft("first","note"));Assert.Equal(4,first.Document.States.Single(s=>s.Item.Id=="count").Current);
        second.Note("first","The second window saved");await second.SaveAsync("first","note");await first.RefreshAsync();
        Assert.Equal(original,first.Draft("first","note"));
        var conflict=await Assert.ThrowsAsync<CompanionRequestException>(()=>first.SaveAsync("first","note"));Assert.Equal(HttpStatusCode.Conflict,conflict.Status);
        Assert.True(first.ConflictReady);Assert.Equal(("first","note"),first.Conflict);Assert.False(first.HasUncertainSave);
        Assert.Equal("The second window saved",first.Document.States.Single(s=>s.Item.Id=="first").Note);
        first.RebaseDraft("first","note");Assert.NotEqual(original.RequestId,first.Draft("first","note")!.RequestId);
        var saved=await first.SaveAsync("first","note");Assert.False(first.HasDrafts);Assert.Null(first.Conflict);
        Assert.Equal("An unsaved desktop note",(await Editor(browser)).GetProperty("states").EnumerateArray().Single(s=>s.GetProperty("item").GetProperty("id").GetString()=="first").GetProperty("note").GetString());
        await first.UndoAsync(saved.Result.HistoryId!.Value);
        Assert.Equal("The second window saved",first.Document.States.Single(s=>s.Item.Id=="first").Note);
        Assert.Contains(await client.HistoryAsync(TrackerId),h=>h.Id==saved.Result.HistoryId&&h.UndoneUtc>0);
        Assert.Equal("Original note",fixture.Desktop.Load(TrackerId,Approved).Single(s=>s.Item.Id=="first").Note);
    }

    [Fact]
    public async Task ChoosingSavedValueDiscardsOnlyTheConflictingDraftAndKeepsOtherEdits()
    {
        using var fixture=new WorkspaceCase();fixture.Prepare();using var host=new Host(false,root:fixture.Root);using var browser=host.Client();await Setup(host,browser);
        using var desktopHttp=host.Client();var client=new CompanionClient(desktopHttp);await client.SignInAsync("Owner","test-only-password-123");await client.WorkspaceAsync();
        var desktop=new CompanionEditor(client,TrackerId);await desktop.RefreshAsync();desktop.Note("first","Desktop draft");desktop.Progress("count",4);
        await Save(browser,Edit(await Editor(browser),"first","note",note:"Browser saved"));
        await Assert.ThrowsAsync<CompanionRequestException>(()=>desktop.SaveAsync("first","note"));desktop.UseSaved("first","note");
        Assert.Null(desktop.Draft("first","note"));Assert.NotNull(desktop.Draft("count","progress"));Assert.Null(desktop.Conflict);
        Assert.Equal("Browser saved",desktop.Document.States.Single(s=>s.Item.Id=="first").Note);
    }

    private sealed class LostAcknowledgement(HttpClient inner,bool serverError):HttpMessageHandler
    {
        public bool LoseNext {get;set;}
        public bool Offline {get;set;}
        public bool GoOfflineAfterConflict {get;set;}
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation)
        {
            if(Offline)throw new HttpRequestException("The server is unavailable.");
            using var copy=new HttpRequestMessage(request.Method,request.RequestUri);
            foreach(var header in request.Headers)copy.Headers.TryAddWithoutValidation(header.Key,header.Value);
            if(request.Content is not null)
            {
                copy.Content=new ByteArrayContent(await request.Content.ReadAsByteArrayAsync(cancellation));
                foreach(var header in request.Content.Headers)copy.Content.Headers.TryAddWithoutValidation(header.Key,header.Value);
            }
            var response=await inner.SendAsync(copy,cancellation);
            if(GoOfflineAfterConflict&&response.StatusCode==HttpStatusCode.Conflict)Offline=true;
            if(LoseNext&&request.Method==HttpMethod.Post&&request.RequestUri!.AbsolutePath.EndsWith("/items")&&response.IsSuccessStatusCode)
            {
                LoseNext=false;response.Dispose();
                if(serverError)return new(HttpStatusCode.InternalServerError){Content=JsonContent.Create(new{error="Acknowledgement failed."})};
                throw new HttpRequestException("The acknowledgement was lost.");
            }
            return response;
        }
    }

    [Fact]
    public async Task OfflineSaveRetainsTheDraftAndConflictCannotBeRebasedUntilLatestValueIsLoaded()
    {
        using var fixture=new WorkspaceCase();fixture.Prepare();using var host=new Host(false,root:fixture.Root);using var browser=host.Client();await Setup(host,browser);
        using var transport=host.Client();using var gate=new LostAcknowledgement(transport,false);using var http=new HttpClient(gate){BaseAddress=transport.BaseAddress};
        var client=new CompanionClient(http);await client.SignInAsync("Owner","test-only-password-123");await client.WorkspaceAsync();
        var editor=new CompanionEditor(client,TrackerId);await editor.RefreshAsync();editor.Note("first","Offline draft");gate.Offline=true;
        await Assert.ThrowsAsync<HttpRequestException>(()=>editor.SaveAsync("first","note"));Assert.True(editor.HasUncertainSave);
        Assert.Equal("Original note",fixture.Repo().Load(TrackerId,Approved).Single(s=>s.Item.Id=="first").Note);
        gate.Offline=false;await editor.RetryAsync();Assert.False(editor.HasUncertainSave);
        editor.Note("first","A later desktop draft");await Save(browser,Edit(await Editor(browser),"first","note",note:"A later browser save"));gate.GoOfflineAfterConflict=true;
        await Assert.ThrowsAsync<CompanionRequestException>(()=>editor.SaveAsync("first","note"));Assert.False(editor.ConflictReady);
        Assert.Throws<InvalidOperationException>(()=>editor.RebaseDraft("first","note"));gate.Offline=false;await editor.RefreshAsync();Assert.True(editor.ConflictReady);
        Assert.Equal("A later browser save",editor.Document.States.Single(s=>s.Item.Id=="first").Note);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnconfirmedDesktopSaveRetriesTheSameReceiptAndNeverWritesTheLocalStore(bool serverError)
    {
        using var fixture=new WorkspaceCase();fixture.Prepare();using var host=new Host(false,root:fixture.Root);using var browser=host.Client();await Setup(host,browser);
        using var transport=host.Client();using var lossy=new LostAcknowledgement(transport,serverError);using var http=new HttpClient(lossy){BaseAddress=transport.BaseAddress};
        var client=new CompanionClient(http);await client.SignInAsync("Owner","test-only-password-123");await client.WorkspaceAsync();
        var desktop=new CompanionEditor(client,TrackerId);await desktop.RefreshAsync();desktop.Note("first","A save without acknowledgement");var command=desktop.Draft("first","note");
        int before=fixture.Repo().History(TrackerId,Approved).Count;lossy.LoseNext=true;
        await Assert.ThrowsAnyAsync<Exception>(()=>desktop.SaveAsync("first","note"));Assert.True(desktop.HasUncertainSave);
        Assert.Throws<InvalidOperationException>(()=>desktop.Note("first","A different value"));
        await desktop.RefreshAsync();Assert.Equal(command,desktop.Draft("first","note"));
        var retry=await desktop.RetryAsync();Assert.True(retry.Result.Replayed);Assert.False(desktop.HasUncertainSave);Assert.False(desktop.HasDrafts);
        Assert.Equal(before+1,fixture.Repo().History(TrackerId,Approved).Count);
        Assert.Equal("Original note",fixture.Desktop.Load(TrackerId,Approved).Single(s=>s.Item.Id=="first").Note);
    }

    [Fact]
    public async Task WorkspaceIdentityIsPrivateAndPinsDesktopRequestsToTheirPreparedCopy()
    {
        using var fixture=new WorkspaceCase();fixture.Prepare();using var host=new Host(false,root:fixture.Root);using var anonymous=host.Client();
        Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.GetAsync("/api/v1/workspace")).StatusCode);
        using var browser=host.Client();await Setup(host,browser);var baseline=await Editor(browser);
        browser.DefaultRequestHeaders.Add("X-Companion-Workspace",new string('0',64));
        Assert.Equal(HttpStatusCode.Conflict,(await Save(browser,Edit(baseline,"first","note",note:"Wrong workspace"))).StatusCode);
        Assert.Equal("Original note",fixture.Repo().Load(TrackerId,Approved).Single(s=>s.Item.Id=="first").Note);
        using var desktopHttp=host.Client();var client=new CompanionClient(desktopHttp);await client.SignInAsync("Owner","test-only-password-123");var identity=await client.WorkspaceAsync();
        Assert.Equal(identity.Id,(await client.WorkspaceAsync()).Id);
        await client.SignOutAsync();Assert.Equal(HttpStatusCode.Unauthorized,(await Assert.ThrowsAsync<CompanionRequestException>(()=>client.TrackerAsync(TrackerId))).Status);
    }

    [Theory]
    [InlineData("http://127.0.0.1:8787",false,true)]
    [InlineData("https://companion.home",false,true)]
    [InlineData("http://192.168.1.99:8787",false,false)]
    [InlineData("http://192.168.1.99:8787",true,true)]
    [InlineData("https://name:password@companion.home",true,false)]
    [InlineData("https://companion.home/path",false,false)]
    [InlineData("https://companion.home?secret=bad",false,false)]
    [InlineData("file:///C:/Codex",true,false)]
    public void DesktopDestinationsAreExplicitAndDoNotEmbedSignInDetails(string address,bool allowHttp,bool accepted)
    {
        if(accepted)Assert.NotNull(CompanionConnection.Server(address,allowHttp));else Assert.Throws<ArgumentException>(()=>CompanionConnection.Server(address,allowHttp));
    }
}
