using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using PixelVaultNative;

namespace PixelVault.Companion;

public sealed partial class PcConnector
{
    static bool SamePermission(TrackerSourceBinding a,TrackerSourceBinding b)=>
        a.AdapterId==b.AdapterId&&a.AdapterVersion==b.AdapterVersion&&a.Account==b.Account
        &&string.Equals(a.Location,b.Location,StringComparison.OrdinalIgnoreCase)
        &&string.Equals(a.GameDirectory??"",b.GameDirectory??"",StringComparison.OrdinalIgnoreCase)
        &&string.Equals(a.SpecificSave??"",b.SpecificSave??"",StringComparison.OrdinalIgnoreCase)
        &&(a.Profile??"")==(b.Profile??"")&&a.Spreadsheet is null&&b.Spreadsheet is null;
    public TrackerPublicationResult Publish(TrackerPublication request)
    {
        lock(gate)
        {
            var settings=sync.Settings();
            if(!settings.AutomaticEnabled||!settings.LibraryEnabled)throw new InvalidOperationException("Automatic publication is paused.");
            if(request.WorkspaceId!=workspace.Identity||request.Trackers is null||request.Trackers.Count>20)throw new InvalidDataException("Invalid tracker publication.");
            foreach(var item in request.Trackers)
            {
                AchievementTrackingCatalog.Validate(item.Definition);var d=item.Definition;
                if(!Regex.IsMatch(d.Id,"^[A-Za-z0-9._-]{1,200}$")||!workspace.Manifest!.Accounts.ContainsKey(d.Provider)
                    ||!Regex.IsMatch(d.GameId,"^[A-Za-z0-9._-]{1,80}$")||d.GameTitle.Length>250||d.Title.Length>250)
                    throw new InvalidDataException("The tracker is outside the approved workspace.");
            }
            using var c=Open();var pair=Pairing(c)??throw new InvalidOperationException("The PC pairing changed.");
            var sources=pair.Sources.ToList();var existing=workspace.Definitions().ToDictionary(d=>d.Id,StringComparer.Ordinal);int added=0;
            foreach(var item in request.Trackers)
            {
                var definition=item.Definition;var digest=Hash(JsonSerializer.Serialize(definition,Json));
                using var claim=c.CreateCommand();claim.CommandText="SELECT digest FROM companion_published_tracker WHERE workspace=$w AND id=$id";
                claim.Parameters.AddWithValue("$w",workspace.Identity);claim.Parameters.AddWithValue("$id",definition.Id);
                var reserved=claim.ExecuteScalar() as string;
                // A definition that already existed on the NAS is always authoritative.
                if(reserved is null&&existing.ContainsKey(definition.Id))continue;
                if(reserved is null)
                {
                    claim.CommandText="INSERT INTO companion_published_tracker(workspace,id,digest) VALUES($w,$id,$digest)";claim.Parameters.AddWithValue("$digest",digest);claim.ExecuteNonQuery();reserved=digest;
                }
                if(reserved!=digest)continue;
                if(!existing.TryGetValue(definition.Id,out var current))
                {workspace.Repository.AddDefinition(definition);existing.Add(definition.Id,definition);current=definition;added++;}
                if(Hash(JsonSerializer.Serialize(current,Json))!=reserved)continue;
                var binding=item.Binding;
                if(binding is null||binding.TrackerId!=definition.Id||binding.AdapterId!="death-stranding-dc.fabrication"||binding.AdapterVersion!="1"
                    ||binding.Account!=workspace.Account(definition.Id)
                    ||!sources.Any(s=>s.Definition.Provider==definition.Provider&&s.Definition.GameId==definition.GameId&&Current(pair,s.Definition.Id) is not null&&SamePermission(s.Binding,binding)))continue;
                var saved=workspace.Repository.Source(definition.Id,binding.Account);
                if(saved is null)
                {var copy=JsonSerializer.Deserialize<TrackerSourceBinding>(JsonSerializer.Serialize(binding,Json),Json)!;workspace.Repository.SaveSource(copy);saved=workspace.Repository.Source(definition.Id,binding.Account)!;}
                if(!SamePermission(saved,binding)||sources.Any(s=>s.Definition.Id==definition.Id))continue;
                sources.Add(new(saved,current,Revision(saved)));
            }
            if(sources.Count!=pair.Sources.Count)
            {
                using var update=c.CreateCommand();update.CommandText="UPDATE companion_connector SET config=$c WHERE id=1 AND workspace=$w";
                update.Parameters.AddWithValue("$w",workspace.Identity);update.Parameters.AddWithValue("$c",JsonSerializer.Serialize(pair with{Sources=sources,Token=""},Json));update.ExecuteNonQuery();
            }
            sync.Put("tracker-publish");sync.Put("publication-error","");
            var accepted=sources.Where(s=>Current(pair with{Sources=sources},s.Definition.Id) is not null&&request.Trackers.Any(t=>t.Binding is not null&&t.Definition.Id==s.Definition.Id&&SamePermission(t.Binding,s.Binding)
                &&TrackerDefinitionComparison.SameStructure(t.Definition,s.Definition))).ToArray();
            return new(added,accepted);
        }
    }
}
