#nullable enable
using System.Text.Json;

namespace PixelVaultNative;

public sealed record LocalProviderSummary(string Provider,string GameId,string Account,int? PlaytimeMinutes,long? LastPlayedUtc,long CapturedUtc=0);

/// <summary>Small account-scoped provider summaries shared by Native and the local web reader.</summary>
public sealed class LocalProviderSummaryStore(string dataRoot)
{
    readonly string path=Path.Combine(Path.GetFullPath(dataRoot),"local-provider-summaries.json");
    static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    public IReadOnlyList<LocalProviderSummary> Read()
    {
        if(!File.Exists(path))return [];
        using var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
        if(input.Length>10*1024*1024)throw new InvalidDataException("The provider summary store is too large.");
        var records=JsonSerializer.Deserialize<List<LocalProviderSummary>>(input,Json)??throw new InvalidDataException("Invalid provider summaries.");
        if(records.Count>10000||records.Any(s=>s.Provider is not ("steam" or "retroachievements")||string.IsNullOrWhiteSpace(s.Account)||string.IsNullOrWhiteSpace(s.GameId)||s.PlaytimeMinutes<0||s.LastPlayedUtc<0||s.LastPlayedUtc>DateTime.MaxValue.Ticks))throw new InvalidDataException("Invalid provider summaries.");
        return records;
    }
    public void Save(LocalProviderSummary value)
    {
        if(value.Provider is not ("steam" or "retroachievements")||string.IsNullOrWhiteSpace(value.Account)||string.IsNullOrWhiteSpace(value.GameId)||value.PlaytimeMinutes<0||value.LastPlayedUtc<0||value.LastPlayedUtc>DateTime.MaxValue.Ticks)throw new InvalidDataException("Invalid provider summary.");
        var deadline=Environment.TickCount64+5000;FileStream? lease=null;
        while(lease is null)
        {
            try{lease=new FileStream(path+".lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}
            catch(IOException)when(Environment.TickCount64<deadline){Thread.Sleep(20);}
        }
        using(lease)
        {
            var records=Read().ToList();var old=records.SingleOrDefault(s=>s.Provider==value.Provider&&s.GameId==value.GameId&&s.Account==value.Account);
            if(old is not null&&old.CapturedUtc>value.CapturedUtc)return;
            if(old is not null){records.Remove(old);value=value with{PlaytimeMinutes=value.PlaytimeMinutes??old.PlaytimeMinutes,LastPlayedUtc=value.LastPlayedUtc??old.LastPlayedUtc};}
            records.Add(value);if(records.Count>10000)throw new InvalidDataException("The provider summary store is full.");var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try{File.WriteAllText(temp,JsonSerializer.Serialize(records,Json));File.Move(temp,path,true);}
            finally{if(File.Exists(temp))File.Delete(temp);}
        }
    }
}
