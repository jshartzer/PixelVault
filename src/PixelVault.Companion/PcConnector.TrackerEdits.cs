using PixelVaultNative;

namespace PixelVault.Companion;

public sealed partial class PcConnector
{
    public TrackerWebPage TrackerEdits(string tracker,int offset)
    {
        var settings=sync.Settings();
        if(!settings.AutomaticEnabled||!settings.LibraryEnabled||!settings.WebTrackerEditsEnabled)throw new InvalidOperationException("Web tracker return sync is paused or not enabled.");
        using var c=Open();_=Pairing(c)??throw new InvalidOperationException("The PC pairing changed.");
        if(offset<0||offset>20000)throw new InvalidDataException("Invalid edit page.");
        var d=workspace.Definitions().SingleOrDefault(d=>d.Id==tracker)??throw new KeyNotFoundException();
        var fields=workspace.Repository.WebAuthoredFields(d.Id,workspace.Account(d.Id),workspace.Manifest!.CopiedUtc.ToUniversalTime().Ticks);
        return new(workspace.Identity,d.Id,d.Provider,d.GameId,fields.Skip(offset).Take(100).ToArray(),offset+100<fields.Count?offset+100:null);
    }
}
