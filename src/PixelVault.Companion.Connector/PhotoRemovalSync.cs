using System.Net.Http.Json;
using System.Text.Json;
using PixelVault.Companion;
using PixelVault.Companion.Export;

namespace PixelVault.Companion.Connector;

public static class PhotoRemovalSync
{
    static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    public static (string[] Remove,string[] Restore) Plan(ScreenshotRemovalGame game,string workspace,string root,ScreenshotSnapshotExporter.PhotoDeletionEvidence evidence)
    {
            var candidates=game.Photos.Select(p=>p.EditionId).Distinct(StringComparer.Ordinal).SelectMany(edition=>evidence.Events.Select(e=>(Id:ScreenshotSnapshotExporter.DeletedPhotoId(workspace,edition,root,e.File),Evidence:e))).GroupBy(p=>p.Id,StringComparer.Ordinal).ToDictionary(g=>g.Key,g=>g.First().Evidence,StringComparer.Ordinal);
            var remove=game.Photos.Where(p=>!p.Removed&&candidates.TryGetValue(p.Id,out var e)&&e.Missing&&!e.Indexed&&!e.Listed).Select(p=>p.Id).ToArray();
            var restore=game.Photos.Where(p=>p.Removed&&candidates.TryGetValue(p.Id,out var e)&&!e.Missing&&e.Indexed&&e.Listed&&e.EditionId==p.EditionId).Select(p=>p.Id).ToArray();
        return (remove,restore);
    }
    public static async Task<int> Reconcile(HttpClient http,ConnectorPairing config,ScreenshotSnapshotExporter.PhotoDeletionEvidence evidence,CancellationToken token)
    {
        if(evidence.Events.Count==0)return 0;
        var root=Path.GetFullPath(File.ReadLines(Path.Combine(config.DesktopDataRoot,"PixelVault.settings.ini")).First(l=>l.StartsWith("library=",StringComparison.OrdinalIgnoreCase)).Split('=',2)[1].Trim());
        var inventory=await http.GetFromJsonAsync<ScreenshotRemovalGame[]>("connector/v1/screenshots/removals",Json,token)??throw new InvalidDataException("Missing published photo identities.");
        var changed=0;
        foreach(var game in inventory)
        {
            var (remove,restore)=Plan(game,config.WorkspaceId,root,evidence);
            if(remove.Length+restore.Length==0)continue;
            if(ScreenshotSnapshotExporter.ReadDeletionEvidence(config.DesktopDataRoot,config.WorkspaceId,token).Signature!=evidence.Signature)throw new IOException("Native photo deletion state changed during reconciliation.");
            using var response=await http.PostAsJsonAsync($"connector/v1/screenshots/removals/{Uri.EscapeDataString(game.Provider)}/{Uri.EscapeDataString(game.GameId)}",new ScreenshotRemovalRequest(config.WorkspaceId,game.Version,remove,restore),Json,token);response.EnsureSuccessStatusCode();
            var result=await response.Content.ReadFromJsonAsync<ScreenshotRemovalGame>(Json,token)??throw new InvalidDataException("No photo removal receipt.");
            var expected=game.Photos.Select(p=>p with{Removed=remove.Contains(p.Id)||!restore.Contains(p.Id)&&p.Removed}).OrderBy(p=>p.Id).ToArray();
            if(result.Provider!=game.Provider||result.GameId!=game.GameId||!result.Photos.OrderBy(p=>p.Id).SequenceEqual(expected))throw new InvalidDataException("Photo removal receipt did not match.");
            changed+=remove.Length+restore.Length;
        }
        return changed;
    }
}
