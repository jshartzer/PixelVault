using System.Text.Json;
using System.Text.RegularExpressions;
using PixelVaultNative;

namespace PixelVault.Companion;

public sealed class AchievementArtworkSync(CompanionOptions options,TrackerWorkspace workspace,ConnectorSyncState state,AchievementStore achievements)
{
    readonly object gate=new();
    static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    string Root=>Path.Combine(options.DataRoot,"achievement-artwork");
    void Enabled()
    {
        var settings=state.Settings();
        if(workspace.ReadOnly||!settings.AutomaticEnabled||!settings.LibraryEnabled||settings.WorkspaceId!=workspace.Identity)
            throw new InvalidOperationException("Automatic achievement artwork syncing is paused.");
    }
    Dictionary<string,JsonElement> Read()
    {
        var path=Path.Combine(Root,"manifest.json");
        if(!File.Exists(path))return new(StringComparer.Ordinal);
        if(new FileInfo(path).Length>2*1024*1024)throw new InvalidDataException("Artwork manifest is too large.");
        return (JsonSerializer.Deserialize<JsonElement[]>(File.ReadAllText(path))??[]).ToDictionary(e=>e.GetProperty("originalUrl").GetString()!,StringComparer.Ordinal);
    }
    static void Protect(string path){if(!OperatingSystem.IsWindows())File.SetUnixFileMode(path,UnixFileMode.UserRead|UnixFileMode.UserWrite);}
    public Dictionary<string,string> Inventory()
    {
        lock(gate)
        {
            Enabled();var inventory=new Dictionary<string,string>(StringComparer.Ordinal);
            foreach(var pair in Read())
            {
                var entry=pair.Value.Deserialize<AchievementArtworkAsset>(Json);
                if(entry is null||!Regex.IsMatch(entry.Sha256??"","^[a-f0-9]{64}$"))continue;
                var file=Path.Combine(Root,entry.File);
                if(File.Exists(file)&&new FileInfo(file).Length<=786432&&AchievementArtwork.Verify(File.ReadAllBytes(file),entry.Sha256) is not null)
                    inventory[pair.Key]=entry.Sha256!;
            }
            return inventory;
        }
    }
    public AchievementArtworkReceipt Receive(AchievementArtworkTransfer transfer)
    {
        lock(gate)
        {
            Enabled();var art=transfer.Artwork;
            if(transfer.WorkspaceId!=workspace.Identity||art is null||art.Provider!="steam"||!Regex.IsMatch(art.Sha256??"","^[a-f0-9]{64}$")
                ||transfer.Bytes is null||AchievementArtwork.Verify(transfer.Bytes,art.Sha256) is null||art.SourceUrl?.Length>1024||art.SourcePage?.Length>1024)
                throw new InvalidDataException("Invalid achievement artwork.");
            var original=AchievementIcons.Canonical(art.Provider,art.GameId,art.OriginalUrl);
            var expected=AchievementIcons.Canonical(art.Provider,art.GameId,achievements.Data(art.Provider,art.GameId,art.AchievementId).Icon);
            if(original is null||original!=expected)throw new InvalidDataException("Artwork does not match this achievement.");
            art=art with{OriginalUrl=original};
            var saved=Read();Directory.CreateDirectory(Root);var file=Path.Combine(Root,art.File);
            var valid=File.Exists(file)&&new FileInfo(file).Length<=786432&&AchievementArtwork.Verify(File.ReadAllBytes(file),art.Sha256) is not null;
            if(valid&&saved.TryGetValue(original,out var old)&&old.GetProperty("sha256").GetString()==art.Sha256)return new(art.Sha256!,false);
            if(!valid)
            {
                if(Directory.EnumerateFiles(Root,"*.png").Sum(p=>new FileInfo(p).Length)+transfer.Bytes.Length>128L*1024*1024)
                    throw new InvalidDataException("Artwork cache is full.");
                var temp=file+"."+Guid.NewGuid().ToString("N")+".tmp";
                try{File.WriteAllBytes(temp,transfer.Bytes);Protect(temp);File.Move(temp,file,true);}finally{if(File.Exists(temp))File.Delete(temp);}
            }
            saved[original]=JsonSerializer.SerializeToElement(art,Json);
            var manifest=Path.Combine(Root,"manifest.json");var pending=manifest+"."+Guid.NewGuid().ToString("N")+".tmp";
            var bytes=JsonSerializer.SerializeToUtf8Bytes(saved.Values.ToArray(),Json);if(bytes.Length>2*1024*1024)throw new InvalidDataException("Artwork manifest is full.");
            try{File.WriteAllBytes(pending,bytes);Protect(pending);File.Move(pending,manifest,true);}finally{if(File.Exists(pending))File.Delete(pending);}
            state.Put("artwork-update");return new(art.Sha256!,true);
        }
    }
}
