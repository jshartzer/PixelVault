using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using PixelVaultNative;
using Xunit;

namespace PixelVault.Companion.Tests;

public sealed partial class CompanionTests
{
    sealed class DirectCase : IDisposable
    {
        internal readonly WorkspaceCase Case=new();
        internal string App=>Case.Source;
        internal string State=>Case.Root;
        internal string Cache=>Path.Combine(Case.Base,"web-cache");
        internal string Guide=>Path.Combine(App,"guides","pixelvault-guides.sqlite");
        internal string FolderCache {get;}
        internal string Index {get;}
        internal DirectCase()
        {
            Directory.CreateDirectory(State);Directory.CreateDirectory(Path.Combine(App,"guides"));Directory.CreateDirectory(Path.Combine(App,"achievement-hub"));Directory.CreateDirectory(Path.Combine(App,"cache"));
            File.WriteAllText(Path.Combine(State,"workspace.json"),JsonSerializer.Serialize(new WorkspaceManifest(1,new(){["steam"]=Approved},DateTime.UtcNow,"direct-local")));
            var library=Path.Combine(Case.Base,"captures");Directory.CreateDirectory(library);
            File.WriteAllText(Path.Combine(App,"PixelVault.settings.ini"),"library="+library+"\nsteam_user_id_64="+Approved+"\n");
            var safe=Regex.Replace(Regex.Replace(library.Replace('_',' ').Replace('-',' ').Replace(':',' '),@"[^\p{L}\p{Nd}]+"," ").Trim().ToLowerInvariant(),@"\s+","_");
            FolderCache=Path.Combine(App,"cache","library-folders-"+safe+".cache");
            File.WriteAllText(FolderCache,string.Join('\t',new[]{"G1",Path.Combine(library,"Journey"),"Test journey","0","","Steam","","42","","","","","","0"}));
            Index=Path.Combine(App,"cache","pixelvault-index-"+safe+".sqlite");
            using(var c=new SqliteConnection("Data Source="+Index+";Pooling=False"))
            {c.Open();using var q=c.CreateCommand();q.CommandText="CREATE TABLE photo_index(root TEXT,file_path TEXT,capture_utc_ticks INTEGER);CREATE TABLE game_index(root TEXT,game_id TEXT,folder_path TEXT,name TEXT,platform_label TEXT,steam_app_id TEXT,retro_achievements_game_id TEXT,file_count INTEGER);INSERT INTO game_index VALUES($root,'G1',$folder,'Test journey','Steam','42','',0)";q.Parameters.AddWithValue("$root",library);q.Parameters.AddWithValue("$folder",Path.Combine(library,"Journey"));q.ExecuteNonQuery();}
            using(var c=new SqliteConnection("Data Source="+Guide+";Pooling=False"))
            {
                c.Open();using var q=c.CreateCommand();q.CommandText="""
                PRAGMA journal_mode=WAL;
                CREATE TABLE achievement_definition(achievement_id INTEGER PRIMARY KEY,provider TEXT,provider_game_id TEXT,provider_achievement_id TEXT,title TEXT,description TEXT,display_order INTEGER,is_hidden INTEGER,icon_url TEXT,group_name TEXT,dlc_name TEXT,group_sort_order INTEGER,is_active INTEGER);
                CREATE TABLE achievement_guide(achievement_id INTEGER,guide_text TEXT,source_url TEXT,source_title TEXT,tags TEXT,is_missable INTEGER);
                CREATE TABLE achievement_category(name TEXT PRIMARY KEY);
                CREATE TABLE achievement_category_order(name TEXT,position INTEGER);
                CREATE TABLE achievement_dlc(provider TEXT,provider_game_id TEXT,name TEXT,PRIMARY KEY(provider,provider_game_id,name));
                INSERT INTO achievement_definition VALUES(1,'steam','42','ONE','First achievement','Description',0,0,'','Story','',0,1);
                INSERT INTO achievement_guide VALUES(1,'Keep this guide','https://example.com','Source','tag',1);
                """;q.ExecuteNonQuery();
            }
            File.WriteAllText(Path.Combine(App,"achievement-hub","account.json"),JsonSerializer.Serialize(new{Provider="steam",Account=Approved,Games=new Dictionary<string,object>{["42"]=new{UpdatedUtc=1,Rows=new[]{new{Provider="steam",ProviderGameId="42",ProviderAchievementId="ONE",Title="First achievement",ProgressKnown=true,Unlocked=false,UnlockUtcTicks=0}}}}}));
            File.WriteAllText(Path.Combine(App,"local-store-preparation.json"),"{\"purpose\":\"pc-companion-rehearsal\"}");
            DirectStorePreparation.Initialize(App);
        }
        internal Host Host()=>new(false,root:State,applicationRoot:App,cacheRoot:Cache);
        internal object Groups(JsonElement row,string category,string? request=null)=>new{version=row.GetProperty("version").GetString(),requestId=request??Guid.NewGuid().ToString("N"),groups=new{category,dlc="",sortOrder=0}};
        public void Dispose()=>Case.Dispose();
    }

    [Fact]
    public async Task DirectLocalWebAndNativeShareRecordsConflictsRetryUndoAndAccountIsolation()
    {
        using var fixture=new DirectCase();using var host=fixture.Host();using var client=host.Client();await Setup(host,client);
        var native=new TrackerRepository(fixture.App,pooling:false);
        var nativeBefore=native.ReadEditor(TrackerId,Approved);
        var command=Edit(await Editor(client),"first","note",note:"Saved directly from phone");
        var response=await Save(client,command);Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        Assert.Equal("Saved directly from phone",native.Load(TrackerId,Approved).Single(s=>s.Item.Id=="first").Note);
        Assert.Throws<InvalidOperationException>(()=>native.Edit(TrackerId,Approved,new("first","note",null,"Stale Native draft",nativeBefore.Versions["first"].Note,Guid.NewGuid().ToString())));
        Assert.Equal(HttpStatusCode.OK,(await Save(client,command)).StatusCode);
        var payload=await response.Content.ReadFromJsonAsync<JsonElement>();var history=payload.GetProperty("result").GetProperty("historyId").GetInt64();
        Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync($"/api/v1/trackers/{TrackerId}/history/{history}/undo",new{requestId=Guid.NewGuid().ToString()})).StatusCode);
        Assert.Equal("Original note",native.Load(TrackerId,Approved).Single(s=>s.Item.Id=="first").Note);
        Assert.Equal("Private other account note",native.Load(TrackerId,Other).Single(s=>s.Item.Id=="first").Note);
        var view=await client.GetStringAsync("/api/v1/trackers/"+TrackerId+"?account="+Other);Assert.DoesNotContain(Other,view);Assert.Contains("direct-local",view);
        Assert.False(Directory.Exists(Path.Combine(fixture.State,"achievement-tracking")));
        Assert.Equal(HttpStatusCode.Gone,(await client.GetAsync("/connector/v1/settings")).StatusCode);
    }

    [Fact]
    public async Task DirectLocalGroupsUseCanonicalTransactionsDetectAbaAndKeepGuides()
    {
        using var fixture=new DirectCase();using var host=fixture.Host();using var client=host.Client();await Setup(host,client);
        const string endpoint="/api/v1/achievements/steam/42/ONE";
        var original=await client.GetFromJsonAsync<JsonElement>(endpoint);
        var edit=fixture.Groups(original,"Collectibles");
        Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync(endpoint+"/groups",edit)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync(endpoint+"/groups",edit)).StatusCode);
        using(var c=new SqliteConnection("Data Source="+fixture.Guide+";Pooling=False"))
        {
            c.Open();using var q=c.CreateCommand();q.CommandText="SELECT group_name FROM achievement_definition WHERE achievement_id=1";Assert.Equal("Collectibles",q.ExecuteScalar());
            q.CommandText="SELECT guide_text FROM achievement_guide WHERE achievement_id=1";Assert.Equal("Keep this guide",q.ExecuteScalar());
            // Legacy Native/import SQL also advances the revision trigger, including ABA.
            q.CommandText="UPDATE achievement_definition SET group_name='Actions';UPDATE achievement_definition SET group_name='Story';";q.ExecuteNonQuery();
        }
        Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync(endpoint+"/groups",fixture.Groups(original,"Difficulty"))).StatusCode);
        var current=await client.GetFromJsonAsync<JsonElement>(endpoint);Assert.Equal("Story",current.GetProperty("achievement").GetProperty("groups").GetProperty("category").GetString());
        Assert.NotEqual(original.GetProperty("version").GetString(),current.GetProperty("version").GetString());
        var responses=await Task.WhenAll(client.PostAsJsonAsync(endpoint+"/groups",fixture.Groups(current,"Difficulty")),client.PostAsJsonAsync(endpoint+"/groups",fixture.Groups(current,"Multiplayer")));
        Assert.Single(responses,r=>r.StatusCode==HttpStatusCode.OK);Assert.Single(responses,r=>r.StatusCode==HttpStatusCode.Conflict);
        using var state=new SqliteConnection("Data Source="+Path.Combine(fixture.State,"companion.sqlite")+";Pooling=False");state.Open();using var count=state.CreateCommand();count.CommandText="SELECT COUNT(*) FROM sqlite_master WHERE name='companion_achievement'";Assert.Equal(0L,count.ExecuteScalar());
    }

    [Fact]
    public async Task DirectLocalLibraryAndProviderChangesAppearWithoutExportOrNativeUi()
    {
        using var fixture=new DirectCase();using var host=fixture.Host();using var client=host.Client();await Setup(host,client);
        var summaries=new LocalProviderSummaryStore(fixture.App);
        summaries.Save(new("steam","42",Approved,180,null,1));summaries.Save(new("steam","42",Other,99999,null,2));
        var game=await client.GetFromJsonAsync<JsonElement>("/api/v1/games/steam/42");Assert.Equal(180,game.GetProperty("stats").GetProperty("playtimeMinutes").GetInt32());
        Assert.Equal("Test journey",game.GetProperty("title").GetString());
        using(var c=new SqliteConnection("Data Source="+fixture.Index+";Pooling=False")){c.Open();using var q=c.CreateCommand();q.CommandText="UPDATE game_index SET name='Renamed journey'";q.ExecuteNonQuery();}
        summaries.Save(new("steam","42",Approved,181,null,3));
        game=await client.GetFromJsonAsync<JsonElement>("/api/v1/games/steam/42");Assert.Equal("Renamed journey",game.GetProperty("title").GetString());Assert.Equal(181,game.GetProperty("stats").GetProperty("playtimeMinutes").GetInt32());
        Assert.DoesNotContain(fixture.App,game.GetRawText());Assert.DoesNotContain(Approved,game.GetRawText());
        Assert.False(File.Exists(Path.Combine(fixture.State,"library","catalog.json")));
    }

    [Fact]
    public void DirectLocalMissingOrOverlappingRootsAndUnpreparedSchemaFailClosed()
    {
        var absent=Path.Combine(Path.GetTempPath(),"NoReplacement-"+Guid.NewGuid().ToString("N"));
        Assert.Throws<InvalidDataException>(()=>new TrackerWorkspace(new(){DataRoot=absent+"-state",ApplicationDataRoot=absent,CacheRoot=absent+"-cache",StorageMode="direct-local"}));Assert.False(Directory.Exists(absent));
        using var fixture=new DirectCase();
        Assert.Throws<InvalidDataException>(()=>new TrackerWorkspace(new(){DataRoot=fixture.App,ApplicationDataRoot=fixture.App,CacheRoot=fixture.Cache,StorageMode="direct-local"}));
        using(var c=new SqliteConnection("Data Source="+fixture.Case.Desktop.DatabasePath+";Pooling=False")){c.Open();using var q=c.CreateCommand();q.CommandText="DROP TABLE tracker_client_request";q.ExecuteNonQuery();}
        Assert.Throws<InvalidDataException>(()=>new TrackerWorkspace(new(){DataRoot=fixture.State,ApplicationDataRoot=fixture.App,CacheRoot=fixture.Cache,StorageMode="direct-local"}));
    }

    [Fact]
    public async Task DirectLocalRequestsDoNotMigrateCanonicalSchemasOrCreateReplacementStores()
    {
        using var fixture=new DirectCase();
        long Version(string path){using var c=LocalApplicationData.Read(path);using var q=c.CreateCommand();q.CommandText="PRAGMA schema_version";return Convert.ToInt64(q.ExecuteScalar());}
        var tracker=Version(fixture.Case.Desktop.DatabasePath);var guide=Version(fixture.Guide);
        using var host=fixture.Host();using var client=host.Client();await Setup(host,client);
        Assert.Equal(HttpStatusCode.OK,(await Save(client,Edit(await Editor(client),"first","note",note:"Shared save"))).StatusCode);
        var row=await client.GetFromJsonAsync<JsonElement>("/api/v1/achievements/steam/42/ONE");
        Assert.Equal(HttpStatusCode.OK,(await client.PostAsJsonAsync("/api/v1/achievements/steam/42/ONE/groups",fixture.Groups(row,"Actions"))).StatusCode);
        Assert.Equal(tracker,Version(fixture.Case.Desktop.DatabasePath));Assert.Equal(guide,Version(fixture.Guide));
        var original=fixture.Case.Desktop.DatabasePath;var unavailable=original+".unavailable";
        File.Move(original,unavailable);
        try{Assert.Equal(HttpStatusCode.InternalServerError,(await client.GetAsync("/api/v1/trackers")).StatusCode);Assert.False(File.Exists(original));}
        finally{File.Move(unavailable,original);}
    }
}
