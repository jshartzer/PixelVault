using System.Text.Json;
using PixelVaultNative;

namespace PixelVault.Companion;

public sealed partial class PcConnector
{
    public TrackerTitleResult PublishTitles(TrackerTitles request)
    {
        lock(gate)
        {
            var settings=sync.Settings();
            if(!settings.AutomaticEnabled||!settings.LibraryEnabled)throw new InvalidOperationException("Automatic publication is paused.");
            if(request.WorkspaceId!=workspace.Identity||request.Titles is null||request.Titles.Count>100
                ||request.Titles.Any(t=>t is null||string.IsNullOrEmpty(t.Id)||string.IsNullOrEmpty(t.Provider))
                ||request.Titles.Select(t=>t.Id).Distinct(StringComparer.Ordinal).Count()!=request.Titles.Count)
                throw new InvalidDataException("Invalid tracker titles.");
            var definitions=workspace.Definitions().ToDictionary(d=>d.Id,StringComparer.Ordinal);
            // Validate the complete batch before making any changes.
            foreach(var title in request.Titles)
                if(!definitions.TryGetValue(title.Id,out var d)||!workspace.Manifest!.Accounts.ContainsKey(title.Provider)
                    ||d.Provider!=title.Provider||d.GameId!=title.GameId||string.IsNullOrWhiteSpace(title.Title)
                    ||title.Title!=title.Title.Trim()||title.Title.Length>200||title.Title.Any(char.IsControl))
                    throw new InvalidDataException("The tracker title is outside the approved workspace.");
            using var c=Open();var pair=Pairing(c)??throw new InvalidOperationException("The PC pairing changed.");
            int updated=0,conflicts=0;
            foreach(var title in request.Titles)
            {
                var current=definitions[title.Id];
                using var cmd=c.CreateCommand();
                cmd.CommandText="SELECT baseline FROM companion_tracker_title WHERE workspace=$w AND id=$id";
                cmd.Parameters.AddWithValue("$w",workspace.Identity);cmd.Parameters.AddWithValue("$id",title.Id);
                var baseline=cmd.ExecuteScalar() as string;
                if(baseline is null)
                {
                    // First contact may already contain a desktop rename. Only historical evidence
                    // of an unchanged NAS title can establish the initial common ancestor.
                    var paired=pair.Sources.FirstOrDefault(s=>s.Definition.Id==title.Id)?.Definition;
                    var builtin=AchievementTrackingCatalog.Builtins().FirstOrDefault(d=>d.Id==title.Id);
                    cmd.CommandText="SELECT digest FROM companion_published_tracker WHERE workspace=$w AND id=$id";
                    var original=cmd.ExecuteScalar() as string;
                    if(current.Title==title.Title||paired?.Title==current.Title||builtin?.Title==current.Title
                        ||original==Hash(JsonSerializer.Serialize(current,Json)))baseline=current.Title;
                }
                bool conflict=false;
                if(current.Title!=title.Title)
                {
                    if(baseline!=current.Title)conflict=true;
                    else
                    {
                        try{workspace.Repository.RenameTracker(title.Id,title.Title,current.Title);updated++;}
                        catch(InvalidOperationException){conflict=true;}
                    }
                }
                if(!conflict)baseline=title.Title;else conflicts++;
                cmd.CommandText="""
                INSERT INTO companion_tracker_title(workspace,id,baseline,desktop_title,conflict) VALUES($w,$id,$baseline,$desktop,$conflict)
                ON CONFLICT(workspace,id) DO UPDATE SET baseline=excluded.baseline,desktop_title=excluded.desktop_title,conflict=excluded.conflict
                """;
                cmd.Parameters.AddWithValue("$baseline",(object?)baseline??DBNull.Value);
                cmd.Parameters.AddWithValue("$desktop",title.Title);cmd.Parameters.AddWithValue("$conflict",conflict?1:0);cmd.ExecuteNonQuery();
            }
            sync.Put("tracker-title-check");sync.Put("publication-error","");return new(updated,conflicts);
        }
    }
    int TitleConflicts(Microsoft.Data.Sqlite.SqliteConnection c)
    {
        using var cmd=c.CreateCommand();cmd.CommandText="SELECT COUNT(*) FROM companion_tracker_title WHERE workspace=$w AND conflict=1";
        cmd.Parameters.AddWithValue("$w",workspace.Identity);return Convert.ToInt32(cmd.ExecuteScalar());
    }
}
