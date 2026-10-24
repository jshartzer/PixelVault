using System;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace PixelVaultNative
{
    sealed partial class AchievementTrackingStore
    {
        static string CustomCatalogue(SqliteConnection c,SqliteTransaction tx,string tracker)
        {
            using var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText="SELECT definition FROM tracker_definition WHERE id=$t";cmd.Parameters.AddWithValue("$t",tracker);
            return cmd.ExecuteScalar() as string ?? throw new InvalidDataException("Bundled catalogues are read-only. Export the definition and import an independent copy to customize its items.");
        }
        internal bool CanEditCatalogue(string tracker){using var c=Open();try{_=CustomCatalogue(c,null,tracker);return true;}catch(InvalidDataException){return false;}}
        internal TrackerCataloguePlan PrepareCatalogue(string tracker,AchievementTrackerDefinition incoming,TrackerCatalogueOptions options,string source)
        {
            using var c=Open();var current=FromJson<AchievementTrackerDefinition>(CustomCatalogue(c,null,tracker));
            return TrackerCatalogueReconcile.Build(current,incoming,options,source);
        }
        internal int ApplyCatalogue(TrackerCataloguePlan plan)
        {
            var after=FromJson<AchievementTrackerDefinition>(plan.After);AchievementTrackingCatalog.Validate(after);_=TrackerDefinitionTransfer.Serialize(after);
            if(after.Id!=plan.TrackerId || plan.Source?.Length>1000 || plan.Fingerprint?.Length!=64)throw new InvalidDataException("Invalid catalogue update.");
            using var c=Open();using var tx=c.BeginTransaction();var before=CustomCatalogue(c,tx,plan.TrackerId);
            if(TrackerCatalogueReconcile.Json(FromJson<AchievementTrackerDefinition>(before))!=plan.Before)throw new InvalidOperationException("The catalogue changed after this preview. Review it again.");
            if(plan.Before==plan.After)return 0;
            using var cmd=c.CreateCommand();cmd.Transaction=tx;
            cmd.CommandText="UPDATE tracker_definition SET definition=$d WHERE id=$t; INSERT INTO tracker_catalogue_history(tracker_id,before_definition,after_definition,created_utc,source,fingerprint) VALUES($t,$b,$d,$u,$s,$f)";
            cmd.Parameters.AddWithValue("$d",plan.After);cmd.Parameters.AddWithValue("$t",plan.TrackerId);cmd.Parameters.AddWithValue("$b",before);cmd.Parameters.AddWithValue("$u",DateTime.UtcNow.Ticks);cmd.Parameters.AddWithValue("$s",plan.Source??"Catalogue import");cmd.Parameters.AddWithValue("$f",plan.Fingerprint);cmd.ExecuteNonQuery();
            tx.Commit();return plan.Changes;
        }
        internal bool HasCatalogueUndo(string tracker)
        {using var c=Open();using var cmd=c.CreateCommand();cmd.CommandText="SELECT COUNT(*) FROM tracker_catalogue_history WHERE tracker_id=$t AND undone_utc=0";cmd.Parameters.AddWithValue("$t",tracker);return Convert.ToInt32(cmd.ExecuteScalar())>0;}
        internal void UndoCatalogue(string tracker)
        {
            using var c=Open();using var tx=c.BeginTransaction();var current=CustomCatalogue(c,tx,tracker);using var cmd=c.CreateCommand();cmd.Transaction=tx;
            cmd.CommandText="SELECT id,before_definition,after_definition FROM tracker_catalogue_history WHERE tracker_id=$t AND undone_utc=0 ORDER BY id DESC LIMIT 1";cmd.Parameters.AddWithValue("$t",tracker);
            using var read=cmd.ExecuteReader();if(!read.Read())throw new InvalidDataException("No catalogue update is available to undo.");
            var id=read.GetInt64(0);var before=read.GetString(1);var after=read.GetString(2);read.Close();
            if(TrackerCatalogueReconcile.Json(FromJson<AchievementTrackerDefinition>(current))!=TrackerCatalogueReconcile.Json(FromJson<AchievementTrackerDefinition>(after)))throw new InvalidOperationException("The catalogue changed later. No saved data was overwritten.");
            cmd.CommandText="UPDATE tracker_definition SET definition=$b WHERE id=$t; UPDATE tracker_catalogue_history SET undone_utc=$u WHERE id=$i";
            cmd.Parameters.AddWithValue("$b",before);cmd.Parameters.AddWithValue("$u",DateTime.UtcNow.Ticks);cmd.Parameters.AddWithValue("$i",id);cmd.ExecuteNonQuery();tx.Commit();
        }
    }
}
