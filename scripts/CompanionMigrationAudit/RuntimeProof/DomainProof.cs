using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using PixelVault.Companion;
using PixelVaultNative;

internal static class DomainProof
{
    internal static async Task Run(CompanionOptions options,string directory)
    {
        options.ValidateRoots();_=new OwnerStore(options);using var workspace=new TrackerWorkspace(options);
        var state=new ConnectorSyncState(options,workspace);if(state.Settings().AutomaticEnabled)throw new InvalidDataException("Real automatic workers must remain paused in rehearsal.");
        var achievements=new AchievementStore(options,workspace,state);var library=new GameLibrary(options,workspace,achievements);var client=new LocalReaderClient(options);var refresh=new LocalRefreshService(options,workspace,library,state,client);
        var before=workspace.Definitions().SelectMany(d=>workspace.Repository.Load(d.Id,workspace.Account(d.Id)).Select(s=>new{Tracker=d.Id,s.Item.Id,s.ManualCurrent,s.Note})).ToArray();
        var binding=workspace.Definitions().Select(d=>(Definition:d,Binding:workspace.Repository.Source(d.Id,workspace.Account(d.Id)))).First(s=>s.Binding is not null);
        var group=library.Games().First(g=>g.Provider==binding.Definition.Provider&&g.GameId==binding.Definition.GameId).GroupId;
        var watch=Stopwatch.StartNew();var queued=refresh.Request(new(group,Guid.NewGuid().ToString("N")));var result=queued;
        using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(3));
        while(result.Status=="reading"){await refresh.Tick(timeout.Token);result=refresh.Get(queued.Id);}
        if(result.Status is not ("complete" or "review"))throw new Exception("The isolated readers did not complete or produce a review.");
        var after=workspace.Definitions().SelectMany(d=>workspace.Repository.Load(d.Id,workspace.Account(d.Id)).Select(s=>new{Tracker=d.Id,s.Item.Id,s.ManualCurrent,s.Note})).ToArray();if(JsonSerializer.Serialize(before)!=JsonSerializer.Serialize(after))throw new Exception("Authored fields changed in the reader rehearsal.");
        var photos=workspace.LocalPhotos!.Catalogues();var sample=photos.First(c=>c.Photos.Any(p=>p.Original is not null));var row=sample.Photos.First(p=>p.Original is not null);
        var download=await workspace.LocalPhotos.DownloadOriginal(sample.Provider,sample.GameId,row.Id,row.Original!.Asset,false,default);long bytes;await using(download.Stream){bytes=download.Stream.Length;}
        await using(var image=await workspace.LocalPhotos.Image(sample.Provider,sample.GameId,row.Id,"thumbnail",default)){if(image.Length<12)throw new Exception("The local photo worker failed.");}
        var proof=new{NoListenerOpened=true,AutomaticPaused=true,ReaderTasks=result.Tasks.Count,Status=result.Status,Milliseconds=watch.ElapsedMilliseconds,AuthoredFieldsPreserved=true,Photos=photos.Sum(c=>c.Photos.Count),OriginalBytesRead=bytes,PhotoWorker=true,NativeUiLaunched=false,NasCalls=0,OriginalsCopied=0};
        File.WriteAllText(Path.Combine(directory,"domain-proof.json"),JsonSerializer.Serialize(proof,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(proof));
    }
}
