using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PixelVaultNative;
using Xunit;
namespace PixelVault.Companion.Tests;

public sealed partial class CompanionTests
{
    [Fact]
    public void RuntimeLogRecordsFailureLocationWithoutPrivateExceptionText()
    {
        var root=Path.Combine(Path.GetTempPath(),"PixelVaultLog-"+Guid.NewGuid().ToString("N"));
        try
        {
            using var provider=new BoundedRuntimeLog(root);
            try{ThrowPrivateMapFailure();}
            catch(IOException e){provider.CreateLogger("test").LogError(new EventId(1),e,"Do not expose private request data");}
            var log=File.ReadAllText(Path.Combine(root,"runtime.log"));
            Assert.Contains("IOException Code=80070020",log);Assert.Contains(nameof(ThrowPrivateMapFailure),log);
            Assert.DoesNotContain("PRIVATE_SOURCE_PATH",log);Assert.DoesNotContain("PRIVATE_CREDENTIAL",log);Assert.DoesNotContain("private request data",log);
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    static void ThrowPrivateMapFailure()=>throw new IOException("PRIVATE_SOURCE_PATH PRIVATE_CREDENTIAL",unchecked((int)0x80070020));
    sealed class FakeLocalReader:ILocalReaderClient
    {
        public int Reads;public string Signature="source-v1";public int Current=1;public long Observed=DateTime.UtcNow.AddSeconds(1).Ticks;public string? WrongAccount;public bool Fail;public bool IncludeUnchanged;
        public TaskCompletionSource? Entered,Release;
        public async Task<LocalReaderResult> Run(LocalReaderRequest request,CancellationToken token)
        {
            if(request.Operation=="probe")return new(){Success=true,Signature=Signature};
            Interlocked.Increment(ref Reads);Entered?.TrySetResult();if(Release is not null)await Release.Task.WaitAsync(token);
            if(Fail)throw new IOException("PRIVATE SOURCE PATH must not reach the browser");
            var items=new List<AchievementTrackerProgress>{new(){ItemId="first",Current=Current}};if(IncludeUnchanged)items.Add(new(){ItemId="count",Current=2});
            var json=JsonSerializer.Serialize(new AchievementTrackerSnapshot{TrackerId=request.Definition.Id,Provider=request.Definition.Provider,GameId=request.Definition.GameId,Account=WrongAccount??request.Binding.Account,Source="Local test observation",ObservedUtcTicks=Observed,Items=items},AchievementTrackingCatalog.Json);
            return new(){Success=true,Signature=Signature,Fingerprint=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))),SnapshotJson=json};
        }
    }
    sealed class RuntimeCase:IDisposable
    {
        internal readonly DirectCase Data=new();internal readonly FakeLocalReader Reader=new();
        internal TrackerRepository Repo=>Data.Case.Desktop;
        internal RuntimeCase()
        {
            Repo.SaveSource(new(){TrackerId=TrackerId,Account=Approved,AdapterId="pixelvault.json-file",AdapterVersion="1",Location=Path.Combine(Data.App,"approved.json")});
        }
        internal Host Host()=>new(false,root:Data.State,applicationRoot:Data.App,cacheRoot:Data.Cache,testReader:Reader);
        internal async Task<GameRefreshView> Request(HttpClient client)
        {
            var game=await client.GetFromJsonAsync<GameCard>("/api/v1/games/steam/42");return (await (await client.PostAsJsonAsync("/api/v1/refresh",new RequestGameRefresh(game!.GroupId,Guid.NewGuid().ToString("N")))).Content.ReadFromJsonAsync<GameRefreshView>())!;
        }
        public void Dispose()=>Data.Dispose();
    }
    [Fact]
    public async Task LocalIdenticalOlderSaveNeedsNoReviewAndPreservesObservationTimes()
    {
        using var f=new RuntimeCase();f.Reader.Current=0;f.Reader.Observed=DateTime.UtcNow.AddDays(-1).Ticks;
        var before=f.Repo.Load(TrackerId,Approved).Single(s=>s.Item.Id=="first");
        using var host=f.Host();using var client=host.Client();await Setup(host,client);var service=host.Services.GetRequiredService<LocalRefreshService>();
        var job=await f.Request(client);await service.Tick(default);var result=service.Get(job.Id);
        Assert.Equal("complete",result.Status);Assert.False(result.CanReview);Assert.Empty(result.Tasks.Single().Changes);
        var after=f.Repo.Load(TrackerId,Approved).Single(s=>s.Item.Id=="first");Assert.Equal(before.ObservedUtcTicks,after.ObservedUtcTicks);Assert.Equal(before.Note,after.Note);
    }
    [Fact]
    public async Task LocalOlderReviewShowsOnlyDifferencesAndSurvivesFailedNewerCheck()
    {
        using var f=new RuntimeCase();f.Reader.IncludeUnchanged=true;f.Reader.Observed=DateTime.UtcNow.AddDays(-1).Ticks;
        using var host=f.Host();using var client=host.Client();await Setup(host,client);var service=host.Services.GetRequiredService<LocalRefreshService>();
        var old=await f.Request(client);await service.Tick(default);var review=service.Get(old.Id);
        Assert.Single(review.Tasks.Single().Changes);Assert.Equal(1,review.Tasks.Single().Older);
        f.Reader.Fail=true;await f.Request(client);await service.Tick(default);Assert.True(service.Get(old.Id).CanReview);
    }
    [Fact]
    public async Task LocalSuccessfulNewerCheckRetiresPendingReviewsWithoutApplyingThem()
    {
        using var f=new RuntimeCase();f.Reader.Observed=DateTime.UtcNow.AddDays(-1).Ticks;
        using var host=f.Host();using var client=host.Client();await Setup(host,client);var service=host.Services.GetRequiredService<LocalRefreshService>();
        var old=await f.Request(client);await service.Tick(default);Assert.True(service.Get(old.Id).CanReview);
        f.Reader.Observed=DateTime.UtcNow.Ticks;f.Reader.Current=0;var fresh=await f.Request(client);await service.Tick(default);
        Assert.Equal("complete",service.Get(fresh.Id).Status);Assert.False(service.Get(old.Id).CanReview);
        Assert.Empty(service.Get(old.Id).Tasks.Single().Changes);
        var connection=await client.GetFromJsonAsync<JsonElement>("/api/v1/refresh/connection");Assert.Empty(connection.GetProperty("reviewJobs").EnumerateArray());
        Assert.Equal(0,f.Repo.Load(TrackerId,Approved).Single(s=>s.Item.Id=="first").Current);
        Assert.Equal("Original note",f.Repo.Load(TrackerId,Approved).Single(s=>s.Item.Id=="first").Note);
    }
    [Fact]
    public async Task LocalRefreshUsesCanonicalProgressRetainsNotesAndIsQueuedAndAuthenticated()
    {
        using var f=new RuntimeCase();using var host=f.Host();using var client=host.Client();await Setup(host,client);var service=host.Services.GetRequiredService<LocalRefreshService>();
        var queued=await f.Request(client);Assert.Equal("reading",queued.Status);Assert.Equal(0,f.Reader.Reads);
        await service.Tick(default);var complete=await client.GetFromJsonAsync<GameRefreshView>("/api/v1/refresh/"+queued.Id);Assert.Equal("complete",complete!.Status);Assert.Equal(1,f.Reader.Reads);
        Assert.True((await client.GetFromJsonAsync<JsonElement>("/api/v1/refresh/connection")).GetProperty("online").GetBoolean());
        var row=f.Repo.Load(TrackerId,Approved).Single(s=>s.Item.Id=="first");Assert.Equal(1,row.Current);Assert.Equal("Original note",row.Note);
        Assert.Equal("Private other account note",f.Repo.Load(TrackerId,Other).Single(s=>s.Item.Id=="first").Note);
        using var anonymous=host.Client();Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.GetAsync("/api/v1/refresh/"+queued.Id)).StatusCode);
        Assert.DoesNotContain(f.Data.App,await client.GetStringAsync("/api/v1/refresh/"+queued.Id));
    }
    [Fact]
    public async Task LocalRefreshReviewsManualConflictsAndRejectsAnOutdatedReview()
    {
        using var f=new RuntimeCase();f.Repo.SetManualProgress(TrackerId,Approved,"first",0);
        using var host=f.Host();using var client=host.Client();await Setup(host,client);var service=host.Services.GetRequiredService<LocalRefreshService>();var job=await f.Request(client);await service.Tick(default);var review=service.Get(job.Id);Assert.Equal("review",review.Status);
        f.Repo.SaveNote(TrackerId,Approved,"first","Native changed this during review");
        Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync("/api/v1/refresh/"+job.Id+"/review",new ReviewGameRefresh(review.Version,false))).StatusCode);
        review=service.Get(job.Id);Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync("/api/v1/refresh/"+job.Id+"/review",new ReviewGameRefresh(review.Version,false))).StatusCode);
        var row=f.Repo.Load(TrackerId,Approved).Single(s=>s.Item.Id=="first");Assert.Equal(0,row.Current);Assert.Equal(1,row.ReportedCurrent);Assert.Equal("Native changed this during review",row.Note);
        Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync("/api/v1/refresh/"+job.Id+"/review",new ReviewGameRefresh(review.Version,false))).StatusCode);
    }
    [Fact]
    public async Task LocalRefreshPreservesOlderObservationsUntilExplicitReviewAndSurvivesRestart()
    {
        using var f=new RuntimeCase();f.Reader.Observed=DateTime.UtcNow.AddDays(-1).Ticks;string id,cookie;
        using(var host=f.Host())using(var client=host.Client())
        {cookie=await Setup(host,client);var job=await f.Request(client);id=job.Id;await host.Services.GetRequiredService<LocalRefreshService>().Tick(default);Assert.True(host.Services.GetRequiredService<LocalRefreshService>().Get(id).HasOlder);Assert.Equal(0,f.Repo.Load(TrackerId,Approved).Single(s=>s.Item.Id=="first").Current);}
        using var restart=f.Host();using var reader=restart.Client();reader.DefaultRequestHeaders.Add("Cookie",cookie);await Token(reader);var review=await reader.GetFromJsonAsync<GameRefreshView>("/api/v1/refresh/"+id);Assert.Equal("review",review!.Status);
        Assert.Equal(HttpStatusCode.OK,(await reader.PostAsJsonAsync("/api/v1/refresh/"+id+"/review",new ReviewGameRefresh(review.Version,true))).StatusCode);Assert.Equal(1,f.Repo.Load(TrackerId,Approved).Single(s=>s.Item.Id=="first").Current);
    }
    [Fact]
    public async Task LocalRefreshRechecksPermissionsAndPauseAfterTheReaderAndDoesNotBlockWebEdits()
    {
        using var f=new RuntimeCase();f.Reader.Entered=new();f.Reader.Release=new();using var host=f.Host();using var client=host.Client();await Setup(host,client);
        var service=host.Services.GetRequiredService<LocalRefreshService>();var job=await f.Request(client);var tick=service.Tick(default);await f.Reader.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var edit=Edit(await Editor(client),"first","note",note:"Saved while reader is busy");Assert.Equal(HttpStatusCode.OK,(await Save(client,edit)).StatusCode);
        var binding=f.Repo.Source(TrackerId,Approved);binding.Location=Path.Combine(f.Data.App,"different.json");f.Repo.SaveSource(binding);f.Reader.Release.SetResult();await tick;
        Assert.Equal("failed",service.Get(job.Id).Status);Assert.Equal(0,f.Repo.Load(TrackerId,Approved).Single(s=>s.Item.Id=="first").ReportedCurrent);Assert.Equal("Saved while reader is busy",f.Repo.Load(TrackerId,Approved).Single(s=>s.Item.Id=="first").Note);
    }
    [Fact]
    public async Task LocalAutomationWaitsForStableSourceAndRespectsPauseAndSharedReceipts()
    {
        using var f=new RuntimeCase();f.Repo.SaveAutomation(TrackerId,Approved,f.Repo.Source(TrackerId,Approved).Id,TrackerAutomationMode.SaveChanges,1,false);
        using var host=f.Host();using var client=host.Client();await Setup(host,client);var service=host.Services.GetRequiredService<LocalRefreshService>();var sync=host.Services.GetRequiredService<ConnectorSyncState>();sync.SetAutomatic(true);
        await service.Tick(default);Assert.Equal(0,f.Reader.Reads);await Task.Delay(3100);await service.Tick(default);sync.SetAutomatic(false);await service.Tick(default);Assert.Equal(0,f.Reader.Reads);
        // A paused queued automatic task was cancelled; re-enable and allow the same stable source to be queued again.
        sync.SetAutomatic(true);await service.Tick(default);await service.Tick(default);Assert.Equal(1,f.Reader.Reads);Assert.Equal(f.Reader.Signature,f.Repo.Automation(TrackerId,Approved).State.HandledSignature);
        await service.Tick(default);Assert.Equal(1,f.Reader.Reads);
    }
    [Fact]
    public async Task LocalRefreshRejectsWrongAccountAndRecoversAnInterruptedQueuedRead()
    {
        using var f=new RuntimeCase();f.Reader.WrongAccount=Other;using var host=f.Host();using var client=host.Client();await Setup(host,client);var service=host.Services.GetRequiredService<LocalRefreshService>();var job=await f.Request(client);await service.Tick(default);Assert.Equal("failed",service.Get(job.Id).Status);Assert.Equal(0,f.Repo.Load(TrackerId,Approved).Single(s=>s.Item.Id=="first").Current);
        f.Reader.WrongAccount=null;f.Reader.Entered=new();f.Reader.Release=new();job=await f.Request(client);using var cancellation=new CancellationTokenSource();var reading=service.Tick(cancellation.Token);await f.Reader.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));cancellation.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>reading);
        f.Reader.Entered=null;f.Reader.Release=null;await service.Tick(default);Assert.Equal("complete",service.Get(job.Id).Status);
    }
    [Fact]
    public async Task LocalRefreshFailurePreservesDataAndKeepsPrivateErrorsOutOfResponses()
    {
        using var f=new RuntimeCase();f.Reader.Fail=true;using var host=f.Host();using var client=host.Client();await Setup(host,client);var job=await f.Request(client);await host.Services.GetRequiredService<LocalRefreshService>().Tick(default);
        Assert.Equal("failed",host.Services.GetRequiredService<LocalRefreshService>().Get(job.Id).Status);Assert.Equal(0,f.Repo.Load(TrackerId,Approved).Single(s=>s.Item.Id=="first").ReportedCurrent);Assert.DoesNotContain("PRIVATE SOURCE PATH",await client.GetStringAsync("/api/v1/refresh/"+job.Id));
    }
    [Fact]
    public void LocalServicePlanContainsExplicitConfigurationAndOnlyStagedWriteRoots()
    {
        using var f=new DirectCase();var package=Path.Combine(f.Case.Base,"package");Directory.CreateDirectory(Path.Combine(package,"readers"));Directory.CreateDirectory(Path.Combine(package,"media-worker"));
        foreach(var file in new[]{"PixelVault.Companion.exe",Path.Combine("readers","PixelVault.exe"),Path.Combine("media-worker","PixelVault.Companion.Media.dll")})File.WriteAllText(Path.Combine(package,file),"test package");
        var input=Path.Combine(f.Case.Base,"source-options.json");File.WriteAllText(input,JsonSerializer.Serialize(new{Companion=new CompanionOptions{StorageMode="direct-local",ApplicationDataRoot=f.App,DataRoot=f.State,CacheRoot=f.Cache}}));var output=Path.Combine(f.Case.Base,"prepared-service");LocalServicePreparation.Prepare(input,package,output);
        using var settings=JsonDocument.Parse(File.ReadAllText(Path.Combine(output,"service.json")));var config=settings.RootElement.GetProperty("Companion");Assert.Equal("direct-local",config.GetProperty("StorageMode").GetString());Assert.Equal(f.App,config.GetProperty("ApplicationDataRoot").GetString());Assert.Equal(Path.Combine(output,"state"),config.GetProperty("DataRoot").GetString());Assert.False(config.GetProperty("LocalProviderRefreshEnabled").GetBoolean());Assert.DoesNotContain("0.0.0.0",settings.RootElement.GetProperty("Urls").GetString());
        Assert.False(File.Exists(Path.Combine(f.State,"local-runtime.lock")));
    }
    [Fact]
    public async Task LocalOnOpenReceivesNativeSignalsAndAcknowledgesOnlyTheHandledOpen()
    {
        using var f=new RuntimeCase();f.Repo.SaveAutomation(TrackerId,Approved,f.Repo.Source(TrackerId,Approved).Id,TrackerAutomationMode.OnOpen,1,false);
        using var host=f.Host();using var client=host.Client();await Setup(host,client);host.Services.GetRequiredService<ConnectorSyncState>().SetAutomatic(true);var service=host.Services.GetRequiredService<LocalRefreshService>();
        await service.Tick(default);Assert.Equal(0,f.Reader.Reads);TrackerSourceLease.SignalOpen(f.Repo.DatabasePath,TrackerId,Approved);await service.Tick(default);await Task.Delay(3100);await service.Tick(default);await service.Tick(default);Assert.Equal(1,f.Reader.Reads);
        Assert.Equal(TrackerSourceLease.OpenSignal(f.Repo.DatabasePath,TrackerId,Approved),f.Repo.Automation(TrackerId,Approved).State.HandledOpenSignal);await service.Tick(default);Assert.Equal(1,f.Reader.Reads);
    }
}
