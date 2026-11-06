using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace PixelVault.Companion;

/// <summary>Explicit migration owner. This rehearsal command does not touch a live application store.</summary>
public static class LocalPhotoPreparation
{
    static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    public static object Initialize(CompanionOptions options,string epoch,IReadOnlyList<ScreenshotCatalogue> catalogues,IReadOnlyDictionary<string,IReadOnlyList<ScreenshotPhoto>> removed)
    {
        options.ValidateRoots();
        var marker=Path.Combine(options.ApplicationDataRoot,"local-store-preparation.json");
        if(!options.DirectLocal||!File.Exists(marker)||JsonDocument.Parse(File.ReadAllText(marker)).RootElement.GetProperty("purpose").GetString()!="pc-companion-rehearsal")throw new InvalidDataException("Photo preparation requires an explicitly staged rehearsal store.");
        if(epoch.Length!=64||!epoch.All(Uri.IsHexDigit)||catalogues.Any(c=>c.WorkspaceId!=epoch))throw new InvalidDataException("Photo identity epochs do not agree.");
        using var workspace=new TrackerWorkspace(options);var source=LocalPhotoIndex.Read(options.ApplicationDataRoot,new(1,workspace.Identity,DateTime.UtcNow,workspace.LocalData!.Games().Values.ToList()));
        LocalPhotoFile.CheckDestinations(source.Root,options);
        var byId=source.Rows.ToDictionary(r=>LocalPhotoIndex.Id(epoch,r.Edition,r.Relative));
        var seed=new Dictionary<string,LocalPhotoLibrary.Entry>();
        foreach(var catalogue in catalogues)
        {
            var removals=removed.GetValueOrDefault(catalogue.Provider+"\n"+catalogue.GameId)??[];
            foreach(var photo in catalogue.Photos.Concat(removals).DistinctBy(p=>p.Id))
            {
                byId.TryGetValue(photo.Id,out var row);LocalPhotoFile.Stamp? stamp=null;
                if(row is not null)
                {
                    if(row.Provider!=catalogue.Provider||row.Game!=catalogue.GameId||row.Edition!=photo.EditionId)throw new InvalidDataException("The photo scope mapping is ambiguous.");
                    try{stamp=LocalPhotoFile.Inspect(source.Root,row.Relative);}catch(IOException){}
                }
                var entry=new LocalPhotoLibrary.Entry(catalogue.Provider,catalogue.GameId,catalogue.Title,row?.Relative??"",photo,stamp,removals.Any(p=>p.Id==photo.Id),DateTime.UtcNow.Ticks,photo);seed.Add(LocalPhotoLibrary.Key(entry),entry);
            }
        }
        foreach(var pair in removed)
        {
            var scope=pair.Key.Split('\n');foreach(var photo in pair.Value)
            {
                var entry=new LocalPhotoLibrary.Entry(scope[0],scope[1],catalogues.FirstOrDefault(c=>c.Provider==scope[0]&&c.GameId==scope[1])?.Title??"Photos","",photo,null,true,DateTime.UtcNow.Ticks);seed.TryAdd(LocalPhotoLibrary.Key(entry),entry);
            }
        }
        var directory=Path.Combine(options.ApplicationDataRoot,"local-photos");Directory.CreateDirectory(directory);var database=Path.Combine(directory,"identities.sqlite");
        if(File.Exists(database))
        {
            using var existing=LocalApplicationData.Read(database);using var query=existing.CreateCommand();query.CommandText="SELECT value FROM configuration WHERE id=1";var prior=JsonSerializer.Deserialize<LocalPhotoLibrary.Configuration>((string)query.ExecuteScalar()!,Json)!;
            if(prior.Root!=source.Root||prior.Epoch!=epoch)throw new InvalidDataException("An existing photo mapping belongs to another source.");
            return new{prepared=false,identities=LocalPhotoLibrary.Load(existing).Count};
        }
        // Publish a fully committed database atomically; a failed preparation never leaves an empty authority.
        var temporary=database+".preparing-"+Guid.NewGuid().ToString("N");
        using(var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=temporary,Pooling=false}.ToString()))
        {
            c.Open();using var q=c.CreateCommand();q.CommandText="CREATE TABLE configuration(id INTEGER PRIMARY KEY,value TEXT NOT NULL);CREATE TABLE photo(id TEXT PRIMARY KEY,payload TEXT NOT NULL);";q.ExecuteNonQuery();
            using var tx=c.BeginTransaction();q.Transaction=tx;q.CommandText="INSERT INTO configuration VALUES(1,$value)";q.Parameters.AddWithValue("$value",JsonSerializer.Serialize(new LocalPhotoLibrary.Configuration(1,source.Root,epoch,DateTime.UtcNow.Ticks),Json));q.ExecuteNonQuery();
            foreach(var e in seed.Values)LocalPhotoLibrary.Save(c,tx,e);tx.Commit();
        }
        File.Move(temporary,database);
        return new{prepared=true,identities=seed.Count,removed=seed.Values.Count(e=>e.Removed),mapped=seed.Values.Count(e=>e.Relative.Length>0),originalBytesCopied=0};
    }
    public static object FromEvidence(string configuration,string evidence)
    {
        var options=JsonDocument.Parse(File.ReadAllText(configuration)).RootElement.GetProperty("Companion").Deserialize<CompanionOptions>()??throw new InvalidDataException("Missing options.");
        var screenshots=Path.Combine(evidence,"nas","screenshots");var roots=Directory.GetDirectories(screenshots);if(roots.Length!=1)throw new InvalidDataException("An explicit photo epoch is required.");
        var epoch=JsonDocument.Parse(File.ReadAllText(Path.Combine(evidence,"pc","connector-redacted.json"))).RootElement.GetProperty("workspaceId").GetString()!;
        var root=roots[0];var files=Directory.EnumerateFiles(Path.Combine(root,"catalogues"),"*.json").Prepend(Path.Combine(root,"catalogue.json")).Where(File.Exists);
        var catalogues=files.Select(f=>JsonSerializer.Deserialize<ScreenshotCatalogue>(File.ReadAllText(f),Json)!).DistinctBy(c=>c.Provider+"\n"+c.GameId).ToArray();
        var removed=new Dictionary<string,IReadOnlyList<ScreenshotPhoto>>();
        foreach(var file in Directory.EnumerateFiles(Path.Combine(root,"removals"),"*.json"))
        {
            var photos=JsonSerializer.Deserialize<Dictionary<string,ScreenshotPhoto>>(File.ReadAllText(file),Json)!;
            var scope=catalogues.FirstOrDefault(c=>LocalPhotoIndex.Hash(c.Provider+"\n"+c.GameId)==Path.GetFileNameWithoutExtension(file))??throw new InvalidDataException("Unmapped removal scope.");removed[scope.Provider+"\n"+scope.GameId]=photos.Values.ToArray();
        }
        return Initialize(options,epoch,catalogues,removed);
    }
}
