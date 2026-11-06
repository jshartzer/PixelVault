using System.Net.Http.Json;
using System.Text.Json;
using PixelVault.Companion;
using PixelVaultNative;

namespace PixelVault.Companion.Connector;

public static class AchievementArtworkPublisher
{
    static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    public static async Task<int> Sync(HttpClient http,string dataRoot,string workspace,IReadOnlyList<AchievementGameSync> games,CancellationToken token)
    {
        var manifest=Path.Combine(dataRoot,"achievement-artwork","manifest.json");
        if(!File.Exists(manifest))return 0;
        if(new FileInfo(manifest).Length>2*1024*1024)throw new InvalidDataException("Artwork manifest is too large.");
        var entries=JsonSerializer.Deserialize<AchievementArtworkAsset[]>(await File.ReadAllTextAsync(manifest,token),Json)??[];
        var permitted=games.Where(g=>g.Provider=="steam").SelectMany(g=>g.Rows.Select(r=>(g.GameId,Row:r))).ToDictionary(p=>(p.GameId,p.Row.Id));
        var selected=entries.Where(e=>e is not null&&e.Provider=="steam"&&permitted.TryGetValue((e.GameId,e.AchievementId),out var row)&&AchievementArtwork.Canonical(row.Row.Icon)==AchievementArtwork.Canonical(e.OriginalUrl))
            .Select(e=>e with{OriginalUrl=AchievementArtwork.Canonical(e.OriginalUrl)}).ToArray();
        if(selected.Length==0)return 0;
        var inventory=await http.GetFromJsonAsync<Dictionary<string,string>>("connector/v1/achievement-artwork",Json,token)??throw new InvalidDataException("Missing artwork inventory.");
        var sent=0;
        foreach(var entry in selected)
        {
            token.ThrowIfCancellationRequested();
            if(inventory.GetValueOrDefault(entry.OriginalUrl)==entry.Sha256)continue;
            var art=AchievementArtwork.Find(entry.OriginalUrl,dataRoot);
            if(art is null||art.Sha256!=entry.Sha256)throw new InvalidDataException("Imported artwork failed verification.");
            using var response=await http.PostAsJsonAsync("connector/v1/achievement-artwork",new AchievementArtworkTransfer(workspace,entry,art.Bytes),Json,token);
            response.EnsureSuccessStatusCode();var receipt=await response.Content.ReadFromJsonAsync<AchievementArtworkReceipt>(Json,token);
            if(receipt?.Sha256!=entry.Sha256)throw new InvalidDataException("Artwork receipt mismatch.");
            inventory[entry.OriginalUrl]=entry.Sha256;sent++;
        }
        return sent;
    }
}
