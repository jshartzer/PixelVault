using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PixelVault.Companion;

public sealed partial class ScreenshotLibrary
{
    static string RemovalFile(string root,string provider,string game)=>Path.Combine(root,"removals",Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(provider+"\n"+game))).ToLowerInvariant()+".json");
    static Dictionary<string,ScreenshotPhoto> Removed(string root,string provider,string game)
    {
        var file=RemovalFile(root,provider,game);
        return File.Exists(file)?JsonSerializer.Deserialize<Dictionary<string,ScreenshotPhoto>>(File.ReadAllText(file),Json)??throw new InvalidDataException("Invalid removal record."):new(StringComparer.Ordinal);
    }
    static ScreenshotCatalogue Visible(ScreenshotCatalogue c,string root)
    {
        var removed=Removed(root,c.Provider,c.GameId);if(removed.Count==0)return c;
        var photos=c.Photos.Where(p=>!removed.ContainsKey(p.Id)).ToArray();
        return c with{Photos=photos,Eligible=Math.Max(photos.Length,c.Eligible-(c.Photos.Count-photos.Length))};
    }
    static string RemovalVersion(ScreenshotCatalogue c,Dictionary<string,ScreenshotPhoto> removed)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Digest(c)+"\n"+JsonSerializer.Serialize(removed.OrderBy(p=>p.Key,StringComparer.Ordinal),Json)))).ToLowerInvariant();
    ScreenshotRemovalGame RemovalGame(string provider,string game)
    {
        Scope(provider,game);var file=ActiveFor(provider,game);if(!File.Exists(file))throw new KeyNotFoundException();
        var c=CachedRaw(file);Validate(c,false);var removed=Removed(Root,provider,game);
        return new(provider,game,RemovalVersion(c,removed),c.Photos.Concat(removed.Values).DistinctBy(p=>p.Id).Select(p=>new ScreenshotRemovalIdentity(p.Id,p.EditionId,removed.ContainsKey(p.Id))).ToArray());
    }
    public IReadOnlyList<ScreenshotRemovalGame> RemovalInventory()
    {
        lock(gate){Enabled();return Catalogues().Select(c=>RemovalGame(c.Provider,c.GameId)).ToArray();}
    }
    public ScreenshotRemovalGame SetRemovals(string provider,string game,ScreenshotRemovalRequest request)
    {
        lock(gate)
        {
            Enabled();var current=RemovalGame(provider,game);
            if(request.WorkspaceId!=workspace.Identity||request.RemoveIds is null||request.RestoreIds is null||request.RemoveIds.Count+request.RestoreIds.Count>5000
                ||request.RemoveIds.Concat(request.RestoreIds).Any(id=>!Hash(id))||request.RemoveIds.Intersect(request.RestoreIds).Any())throw new InvalidDataException("Invalid photo removals.");
            if(request.Version!=current.Version)throw new InvalidOperationException("The photo library changed. Retry removal reconciliation.");
            var active=ActiveFor(provider,game);var c=CachedRaw(active);var removed=Removed(Root,provider,game);var known=c.Photos.Concat(removed.Values).DistinctBy(p=>p.Id).ToDictionary(p=>p.Id,StringComparer.Ordinal);
            if(request.RemoveIds.Concat(request.RestoreIds).Any(id=>!known.ContainsKey(id)))throw new InvalidDataException("Photo removal is outside the published game.");
            // Restore archived metadata before clearing its overlay; a crash here still leaves it hidden.
            var restored=request.RestoreIds.Where(id=>c.Photos.All(p=>p.Id!=id)).Select(id=>known[id]).ToArray();
            if(restored.Length>0)Write(active,c with{Photos=c.Photos.Concat(restored).ToArray(),Eligible=Math.Max(c.Eligible,c.Photos.Count+restored.Length)});
            foreach(var id in request.RemoveIds)removed[id]=known[id];
            foreach(var id in request.RestoreIds)removed.Remove(id);
            var file=RemovalFile(Root,provider,game);Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            if(File.Exists(file))
            {
                var history=Path.Combine(Root,"removal-history");Directory.CreateDirectory(history);
                var saved=Path.Combine(history,Path.GetFileNameWithoutExtension(file)+"-"+Guid.NewGuid().ToString("N")+".json");File.Copy(file,saved);Protect(saved);
            }
            var temp=file+".tmp";File.WriteAllText(temp,JsonSerializer.Serialize(removed,Json));Protect(temp);File.Move(temp,file,true);
            manifests.Remove(active);InvalidateAvailable(options,workspace);
            return RemovalGame(provider,game);
        }
    }
}
