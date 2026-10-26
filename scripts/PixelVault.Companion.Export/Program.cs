using System.Text.Json;
using PixelVault.Companion;
using PixelVault.Companion.Export;
if(args.Length==7&&args[0] is "--screenshot-pilot" or "--screenshot-originals-pilot")
{
    var c=ScreenshotSnapshotExporter.Export(args[1],args[2],args[3],args[4],args[5],int.Parse(args[6]),includeOriginals:args[0]=="--screenshot-originals-pilot");
    Console.WriteLine(JsonSerializer.Serialize(new{Photos=c.Photos.Count,c.Eligible,c.SkippedVideos,Grouped=c.Photos.Count(p=>p.Group.Length>0),Consoles=c.Photos.GroupBy(p=>p.Console).ToDictionary(g=>g.Key,g=>g.Count())}));return;
}
if(args.Length==3&&args[0]=="--inspect-achievements")
{
    var scope=JsonSerializer.Deserialize<WorkspaceManifest>(File.ReadAllText(args[2]))??throw new InvalidDataException("Invalid workspace manifest.");
    var id=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(scope))));
    var snapshots=AchievementSnapshotExporter.Read(args[1],id,AchievementSnapshotExporter.ConnectedAccounts(args[1],scope.Accounts));
    using var digest=System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
    foreach(var game in snapshots.OrderBy(g=>g.Provider,StringComparer.Ordinal).ThenBy(g=>g.GameId,StringComparer.Ordinal))foreach(var row in game.Rows.OrderBy(r=>r.Id,StringComparer.Ordinal))digest.AppendData(System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{Provider=game.Provider,Game=game.GameId,row.Id,row.Title,row.Description,row.Guide,row.SourceUrl,row.SourceTitle,row.Tags,row.Missable,row.Groups})+"\n"));
    Console.WriteLine(JsonSerializer.Serialize(new{Games=snapshots.Count,Achievements=snapshots.Sum(g=>g.Rows.Count),Guides=snapshots.Sum(g=>g.Rows.Count(r=>r.Guide.Length>0)),Unknown=snapshots.Sum(g=>g.Rows.Count(r=>!r.ProgressKnown)),MaxGameBytes=snapshots.Max(g=>JsonSerializer.SerializeToUtf8Bytes(g).Length),BlankTitles=snapshots.Sum(g=>g.Rows.Count(r=>string.IsNullOrWhiteSpace(r.Title))),MetadataDigest=Convert.ToHexString(digest.GetHashAndReset())}));return;
}
if(args.Length!=3)throw new ArgumentException("Use <desktop-data-root> <workspace-manifest> <new-export-directory>.");
var manifest=JsonSerializer.Deserialize<WorkspaceManifest>(File.ReadAllText(args[1]))??throw new InvalidDataException("Invalid workspace manifest.");
var catalog=await LibrarySnapshotExporter.ExportAsync(args[0],manifest,args[2]);
Console.WriteLine(JsonSerializer.Serialize(new{Games=catalog.Games.Count,Covers=catalog.Games.Count(g=>g.Cover is not null),Banners=catalog.Games.Count(g=>g.Banner is not null)}));
