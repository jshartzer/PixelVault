using PixelVaultNative;

namespace PixelVault.Companion;

public sealed partial class TrackerWorkspace
{
    readonly object readGate=new();
    DatabaseRevision? trackerRevision,photoRevision,mapRevision;
    readonly Dictionary<string,TrackerEditor> editors=new();
    string editorRevision="";
    public string TrackerRevision
    {
        get{lock(readGate)return Sample||Manifest is null?"sample":(trackerRevision??=new(Repository.DatabasePath)).Read();}
    }
    TrackerEditor Editor(string tracker,string account)
    {
        lock(readGate)
        {
            var revision=TrackerRevision;
            if(editorRevision!=revision){editors.Clear();editorRevision=revision;}
            var key=tracker+"\n"+account;
            if(editors.TryGetValue(key,out var cached))return cached;
            var editor=Repository.ReadEditor(tracker,account);
            if(TrackerRevision==revision){if(editors.Count>=32)editors.Clear();editors[key]=editor;}
            return editor;
        }
    }
    public object? BrowseVersions()
    {
        if(!DirectLocal)return null;
        lock(readGate)
        {
            return new{library=LocalData!.Revision,trackers=TrackerRevision,
                photos=(photoRevision??=new(Path.Combine(options.ApplicationDataRoot,"local-photos","identities.sqlite"))).Read(),
                maps=(mapRevision??=new(Path.Combine(options.ApplicationDataRoot,"game-maps","maps.sqlite"))).Read()};
        }
    }
    void DisposeReads(){lock(readGate){trackerRevision?.Dispose();photoRevision?.Dispose();mapRevision?.Dispose();editors.Clear();}}
}
