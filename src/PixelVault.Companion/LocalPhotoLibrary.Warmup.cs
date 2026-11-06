namespace PixelVault.Companion;

public sealed partial class LocalPhotoLibrary
{
    readonly HashSet<string> warmed=new();
    // Background work is deliberately sequential and limited to four recent captures.
    // It uses the same validated handles, cache quota, cancellation and conversion limit as requests.
    public async Task WarmRecent(CancellationToken token)
    {
        var recent=Catalogues().SelectMany(c=>c.Photos.Select(p=>(c.Provider,c.GameId,Photo:p)))
            .OrderByDescending(p=>p.Photo.CaptureUtcTicks).Take(4).ToArray();
        var current=recent.SelectMany(p=>new[]{"thumbnail","tile960"}.Select(kind=>p.Provider+"\n"+p.GameId+"\n"+p.Photo.Id+"\n"+p.Photo.Preview+"\n"+kind)).ToHashSet();
        warmed.IntersectWith(current);
        foreach(var p in recent)foreach(var kind in new[]{"thumbnail","tile960"})
        {
            token.ThrowIfCancellationRequested();var key=p.Provider+"\n"+p.GameId+"\n"+p.Photo.Id+"\n"+p.Photo.Preview+"\n"+kind;
            if(warmed.Contains(key))continue;
            await using var image=await Image(p.Provider,p.GameId,p.Photo.Id,kind,token);warmed.Add(key);
            await Task.Delay(150,token);
        }
    }
}
