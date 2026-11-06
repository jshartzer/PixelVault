using System.Net;
using System.Net.Http.Json;
using Microsoft.Data.Sqlite;
using PixelVaultNative;

namespace PixelVault.Companion.Connector;

public static class TrackerWebSync
{
    static readonly System.Text.Json.JsonSerializerOptions Json=new(System.Text.Json.JsonSerializerDefaults.Web);
    public static void Backup(string root,string privateData)
    {
        var path=Path.Combine(Path.GetFullPath(root),"achievement-tracking","pixelvault-tracking.sqlite");
        if(!File.Exists(path)||new FileInfo(path).LinkTarget is not null||new DirectoryInfo(Path.GetDirectoryName(path)!).LinkTarget is not null)throw new InvalidDataException("The configured PC tracker database is unavailable or linked.");
        var destination=Path.Combine(privateData,"backups","tracker-web-edits");Directory.CreateDirectory(destination);
        using var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString());c.Open();
        using var copy=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=Path.Combine(destination,"before-web-"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffff")+"-"+Guid.NewGuid().ToString("N")+".sqlite"),Pooling=false}.ToString());copy.Open();c.BackupDatabase(copy);
        using var check=copy.CreateCommand();check.CommandText="PRAGMA quick_check";if(check.ExecuteScalar() as string!="ok")throw new InvalidDataException("The PC tracker backup could not be verified.");
    }
    public static TrackerWebResult Apply(ConnectorPairing config,string privateData,TrackerWebPage page,bool preview=false)
    {
        if(!config.WebTrackerEditsEnabled||config.Accounts is null||string.IsNullOrWhiteSpace(config.DesktopDataRoot)||page.WorkspaceId!=config.WorkspaceId||!config.Accounts.ContainsKey(page.Provider)||page.Fields is null||page.Fields.Count>100)
            throw new InvalidDataException("The web edits are outside the enabled PC workspace.");
        var titles=DesktopTrackerPublication.ReadTitles(config.DesktopDataRoot,config.Accounts);var title=titles.SingleOrDefault(d=>d.Id==page.TrackerId);
        if(title is null||title.Provider!=page.Provider||title.GameId!=page.GameId)throw new InvalidDataException("The web edit belongs to a different PC tracker or game.");
        // No paths or accounts supplied by the server are used for PC writes.
        var repo=new TrackerRepository(config.DesktopDataRoot,pooling:false);var account=AchievementTrackingCatalog.AccountKey(page.Provider,config.Accounts[page.Provider]);
        if(!repo.NeedsWebSync(config.WorkspaceId,page.TrackerId,account,page.Fields))return new(0,repo.WebSyncConflicts(page.TrackerId,account).Count);
        if(preview)return repo.ApplyWebEdits(config.WorkspaceId,page.TrackerId,account,page.Fields,preview:true);
        Backup(config.DesktopDataRoot,privateData);
        return repo.ApplyWebEdits(config.WorkspaceId,page.TrackerId,account,page.Fields);
    }
    public static async Task<TrackerWebResult> OnceAsync(HttpClient http,ConnectorPairing config,string privateData,CancellationToken token,bool preview=false)
    {
        if(!config.WebTrackerEditsEnabled||config.Accounts is null||string.IsNullOrWhiteSpace(config.DesktopDataRoot))throw new InvalidDataException("Web tracker return sync is not enabled on this PC.");
        int applied=0,unavailable=0,previewConflicts=0;
        var definitions=new TrackerRepository(config.DesktopDataRoot,pooling:false).Definitions().ToDictionary(d=>d.Id,StringComparer.Ordinal);
        foreach(var tracker in DesktopTrackerPublication.ReadTitles(config.DesktopDataRoot,config.Accounts))
        {
            int offset=0;
            do
            {
                using var response=await http.GetAsync("connector/v1/tracker-edits/"+Uri.EscapeDataString(tracker.Id)+"?offset="+offset,token);
                if(response.StatusCode==HttpStatusCode.NotFound)break;response.EnsureSuccessStatusCode();
                var page=await response.Content.ReadFromJsonAsync<TrackerWebPage>(Json,token)??throw new InvalidDataException("Missing web tracker edit page.");
                if(page.TrackerId!=tracker.Id||page.Provider!=tracker.Provider||page.GameId!=tracker.GameId||page.NextOffset is not null&&(page.NextOffset<=offset||page.NextOffset>20000))throw new InvalidDataException("Unexpected tracker edit page.");
                if(page.Fields is null||page.Fields.Count>100)throw new InvalidDataException("Invalid web edit page.");
                var compatible=page.Fields.Where(f=>f is not null&&definitions[tracker.Id].Items.Any(i=>!i.Archived&&i.Id==f.ItemId&&i.Name==f.Name&&i.Target==f.Target&&(i.GroupId??"")==(f.GroupId??"")&&(i.Milestone??"")==(f.Milestone??""))).ToArray();
                unavailable+=page.Fields.Count-compatible.Length;
                var result=Apply(config,privateData,page with{Fields=compatible},preview);applied+=result.Applied;if(preview)previewConflicts+=result.Conflicts;
                if(page.NextOffset is null)break;offset=page.NextOffset.Value;
            }while(true);
        }
        var conflicts=new TrackerRepository(config.DesktopDataRoot,pooling:false).WebSyncConflicts().Count(c=>c.WorkspaceId==config.WorkspaceId&&definitions.TryGetValue(c.TrackerId,out var d)&&config.Accounts.TryGetValue(d.Provider,out var account)&&AchievementTrackingCatalog.AccountKey(d.Provider,account)==c.Account);
        return new(applied,preview?previewConflicts:conflicts,unavailable);
    }
}
