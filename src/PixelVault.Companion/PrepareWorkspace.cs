using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PixelVaultNative;

namespace PixelVault.Companion;

public static class PrepareWorkspace
{
    public static (int Trackers,int States,int History) Copy(string sourceRoot,string destinationRoot,Dictionary<string,string>? accounts=null)
    {
        sourceRoot=Path.GetFullPath(sourceRoot);destinationRoot=Path.GetFullPath(destinationRoot);
        var relative=Path.GetRelativePath(sourceRoot,destinationRoot);
        if(relative=="." || !Path.IsPathRooted(relative) && relative!=".." && !relative.StartsWith(".."+Path.DirectorySeparatorChar,StringComparison.Ordinal))
            throw new InvalidDataException("Choose a separate destination outside the desktop data folder.");
        var sourceFile=Path.Combine(sourceRoot,"achievement-tracking","pixelvault-tracking.sqlite");
        var destinationFile=Path.Combine(destinationRoot,"achievement-tracking","pixelvault-tracking.sqlite");
        var manifestFile=Path.Combine(destinationRoot,"workspace.json");
        if(!File.Exists(sourceFile))throw new FileNotFoundException("The desktop tracker database was not found.");
        Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
        var lockFile=Path.Combine(destinationRoot,"prepare.lock");
        using var preparationLock=new FileStream(lockFile,FileMode.CreateNew,FileAccess.Write,FileShare.None,1,FileOptions.DeleteOnClose);
        if(File.Exists(destinationFile)||File.Exists(manifestFile))throw new InvalidDataException("The destination already has tracker data. It will not be replaced.");
        bool completed=false;
        try
        {
            using(var source=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=sourceFile,Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString()))
            using(var destination=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=destinationFile,Pooling=false}.ToString()))
            {
                source.Open();destination.Open();
                using var snapshot=source.BeginTransaction(deferred:true);
                using var pin=source.CreateCommand();pin.Transaction=snapshot;pin.CommandText="SELECT COUNT(*) FROM tracker_item_state";pin.ExecuteScalar();
                source.BackupDatabase(destination);
                using var check=destination.CreateCommand();check.CommandText="PRAGMA quick_check";
                if((string?)check.ExecuteScalar()!="ok")throw new InvalidDataException("The copied database failed its integrity check.");
                using var tables=source.CreateCommand();tables.Transaction=snapshot;tables.CommandText="SELECT name FROM sqlite_master WHERE type='table' AND name NOT GLOB 'sqlite_*' ORDER BY name";
                var names=new List<string>();using(var reader=tables.ExecuteReader())while(reader.Read())names.Add(reader.GetString(0));
                foreach(var table in names)
                    if(TableDigest(source,snapshot,table)!=TableDigest(destination,null,table))throw new InvalidDataException("Tracker copy verification failed: "+table);
                snapshot.Commit();
                check.CommandText="PRAGMA wal_checkpoint(TRUNCATE)";check.ExecuteNonQuery();
            }
            var repo=new TrackerRepository(destinationRoot,pooling:false);var definitions=repo.Definitions();
            foreach(var definition in definitions)AchievementTrackingCatalog.Validate(definition);
            using var copy=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=destinationFile,Pooling=false}.ToString());copy.Open();
            var candidates=new Dictionary<string,HashSet<string>>(StringComparer.Ordinal);
            using(var command=copy.CreateCommand())
            {
                command.CommandText="SELECT DISTINCT tracker_id,account FROM tracker_item_state";using var reader=command.ExecuteReader();
                while(reader.Read())
                {
                    var definition=definitions.FirstOrDefault(d=>d.Id==reader.GetString(0));if(definition is null)continue;
                    if(!candidates.TryGetValue(definition.Provider,out var set))candidates[definition.Provider]=set=new(StringComparer.Ordinal);
                    set.Add(AchievementTrackingCatalog.AccountKey(definition.Provider,reader.GetString(1)));
                }
            }
            accounts??=candidates.ToDictionary(p=>p.Key,p=>p.Value.Count==1?p.Value.Single():throw new InvalidDataException("More than one account was found. Supply an explicit approved account map."));
            accounts=accounts.ToDictionary(p=>p.Key,p=>AchievementTrackingCatalog.AccountKey(p.Key,p.Value));
            if(accounts.Count==0||accounts.Any(p=>!candidates.TryGetValue(p.Key,out var set)||!set.Contains(p.Value)))
                throw new InvalidDataException("Choose provider accounts present in the copied tracker data.");
            using(var manifest=new FileStream(manifestFile,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                JsonSerializer.Serialize(manifest,new WorkspaceManifest(1,accounts,DateTime.UtcNow),new JsonSerializerOptions{WriteIndented=true});
            int Count(string table){using var command=copy.CreateCommand();command.CommandText="SELECT COUNT(*) FROM "+table;return Convert.ToInt32(command.ExecuteScalar());}
            var result=(definitions.Count,Count("tracker_item_state"),Count("tracker_history"));completed=true;return result;
        }
        finally
        {
            if(!completed)foreach(var file in new[]{destinationFile,destinationFile+"-wal",destinationFile+"-shm",manifestFile})if(File.Exists(file))File.Delete(file);
        }
    }

    private static string TableDigest(SqliteConnection connection,SqliteTransaction? transaction,string table)
    {
        using var exists=connection.CreateCommand();exists.Transaction=transaction;exists.CommandText="SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name";exists.Parameters.AddWithValue("$name",table);
        if(Convert.ToInt32(exists.ExecuteScalar())==0)return "missing";
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var command=connection.CreateCommand();command.Transaction=transaction;command.CommandText="SELECT * FROM \""+table.Replace("\"","\"\"")+"\" ORDER BY rowid";
        using var rows=command.ExecuteReader();
        while(rows.Read())
        {
            var values=Enumerable.Range(0,rows.FieldCount).Select(n=>rows.IsDBNull(n)?null:rows.GetValue(n)).ToArray();
            hash.AppendData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(values)+"\n"));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
