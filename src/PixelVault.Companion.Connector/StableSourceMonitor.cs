namespace PixelVault.Companion.Connector;

public sealed class StableSourceMonitor
{
    sealed class Entry {public string Signature="";public DateTime Changed;public DateTime Next;}
    readonly Dictionary<string,Entry> entries=new(StringComparer.Ordinal);
    public Dictionary<string,string> Handled {get;}
    public StableSourceMonitor(Dictionary<string,string> handled=null){Handled=handled??new(StringComparer.Ordinal);}
    public bool Ready(string key,string signature,DateTime now)
    {
        if(Handled.GetValueOrDefault(key)==signature)return false;
        if(!entries.TryGetValue(key,out var entry)||entry.Signature!=signature){entries[key]=new(){Signature=signature,Changed=now};return false;}
        return now-entry.Changed>=TimeSpan.FromSeconds(3)&&now>=entry.Next;
    }
    public void Delay(string key,DateTime now,TimeSpan delay){if(entries.TryGetValue(key,out var entry))entry.Next=now+delay;}
    public void Acknowledge(string key,string signature){Handled[key]=signature;}
}
