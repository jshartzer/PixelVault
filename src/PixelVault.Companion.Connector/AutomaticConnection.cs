using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PixelVault.Companion.Export;
using PixelVaultNative;

namespace PixelVault.Companion.Connector;

sealed class AutomaticConnection(HttpClient http,ConnectorPairing config,string data,DeathStrandingTrackerSource reader)
{
    static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    volatile ConnectorSyncSettings settings=new(false,false,config.WorkspaceId);
    IReadOnlyList<ConnectorSource> sources=config.Sources;
    public IReadOnlyList<ConnectorSource> Sources=>Volatile.Read(ref sources);
    readonly string handledFile=Path.Combine(data,"monitored-saves.json");
    static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    static Dictionary<string,string> LoadHandled(string path){try{return JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(path))??new();}catch{return new();}}
    static void Save(string path,string value){var temp=path+".tmp";File.WriteAllText(temp,value);File.Move(temp,path,true);}
    public async Task MonitorAsync(CancellationToken token)
    {
        var monitor=new StableSourceMonitor(LoadHandled(handledFile));var nextSettings=DateTime.MinValue;var nextHealth=DateTime.MinValue;
        while(!token.IsCancellationRequested)
        {
            try
            {
                var now=DateTime.UtcNow;
                if(now>=nextSettings){var latest=await http.GetFromJsonAsync<ConnectorSyncSettings>("connector/v1/settings",Json,token);if(latest?.WorkspaceId!=config.WorkspaceId)throw new InvalidDataException("PC workspace changed.");settings=latest;nextSettings=now.AddSeconds(10);}
                string sourceError="";
                var groups=Sources.GroupBy(s=>Hash(s.Binding.AdapterId+"\n"+s.Binding.Account+"\n"+s.Binding.Location+"\n"+s.Binding.SpecificSave+"\n"+s.Binding.GameDirectory+"\n"+s.Definition.GameId)).ToList();
                if(settings.AutomaticEnabled)foreach(var group in groups)
                {
                    try
                    {
                        var first=group.First();
                        var signature=first.Binding.AdapterId=="death-stranding-dc.fabrication"?reader.ChangeSignature(first.Binding):TrackerSourceFiles.Signature([new FileInfo(first.Binding.Location)],1536*1024);
                        foreach(var source in group)
                        {
                        var key=Hash(group.Key+source.Definition.Id+source.Revision);
                        if(!monitor.Ready(key,signature,now))continue;
                        using var response=await http.PostAsJsonAsync("connector/v1/automatic",new AutomaticSaveChange(signature,[new ConnectorTask(source.Definition.Id,source.Binding.Id,source.Revision)]),Json,token);
                        response.EnsureSuccessStatusCode();var receipt=await response.Content.ReadFromJsonAsync<AutomaticReceipt>(Json,token);
                        if(receipt is not null&&receipt.Signature==signature&&receipt.Status is "complete" or "review")
                        {monitor.Acknowledge(key,signature);Save(handledFile,JsonSerializer.Serialize(monitor.Handled));}
                        else monitor.Delay(key,now,TimeSpan.FromSeconds(receipt?.Status=="reading"?15:60));
                        }
                    }
                    catch(Exception error)when(error is IOException or UnauthorizedAccessException or InvalidDataException){sourceError="source-unavailable";}
                }
                if(now>=nextHealth){using var health=await http.PostAsJsonAsync("connector/v1/health",new ConnectorHealth(sourceError,null),Json,token);health.EnsureSuccessStatusCode();nextHealth=now.AddSeconds(30);}
            }
            catch(OperationCanceledException)when(token.IsCancellationRequested){break;}
            catch(Exception error)when(error is HttpRequestException or TaskCanceledException or JsonException or InvalidDataException){nextSettings=DateTime.MinValue;}
            try{await Task.Delay(5000,token);}catch(OperationCanceledException){break;}
        }
    }
    public async Task PublicationAsync(CancellationToken token)
    {
        var lastHash="";
        while(!token.IsCancellationRequested)
        {
            try
            {
                if(settings.AutomaticEnabled&&settings.LibraryEnabled&&config.Accounts is not null&&!string.IsNullOrWhiteSpace(config.DesktopDataRoot))
                {
                    var publications=DesktopTrackerPublication.Read(config.DesktopDataRoot,config.Accounts,config.Sources,reader.Validate);
                    var digest=Hash(JsonSerializer.Serialize(publications,Json));
                    if(digest!=lastHash)
                    {
                        var accepted=new List<ConnectorSource>();
                        foreach(var batch in publications.Chunk(20))
                        {
                            using var response=await http.PostAsJsonAsync("connector/v1/trackers",new TrackerPublication(config.WorkspaceId,batch),Json,token);response.EnsureSuccessStatusCode();
                            var result=await response.Content.ReadFromJsonAsync<TrackerPublicationResult>(Json,token)??throw new InvalidDataException("Invalid tracker publication response.");
                            foreach(var source in result.Sources)
                            {
                                var local=batch.FirstOrDefault(p=>p.Definition.Id==source.Definition.Id&&p.Binding is not null);
                                if(local is null||!DesktopTrackerPublication.SamePermission(local.Binding,source.Binding)
                                    ||!TrackerDefinitionComparison.SameStructure(local.Definition,source.Definition))throw new InvalidDataException("Unexpected tracker source.");
                                reader.Validate(source.Binding,source.Definition);accepted.Add(source);
                            }
                        }
                        var merged=Sources.ToDictionary(s=>s.Definition.Id,StringComparer.Ordinal);
                        foreach(var source in accepted)merged[source.Definition.Id]=source;
                        Volatile.Write(ref sources,merged.Values.ToArray());lastHash=digest;
                    }
                    // Titles are metadata, including bundled overrides. Poll even when definitions
                    // are unchanged so an independent NAS rename is detected and preserved.
                    var titles=DesktopTrackerPublication.ReadTitles(config.DesktopDataRoot,config.Accounts);
                    foreach(var batch in titles.Chunk(100))
                    {
                        using var response=await http.PostAsJsonAsync("connector/v1/tracker-titles",new TrackerTitles(config.WorkspaceId,batch),Json,token);
                        response.EnsureSuccessStatusCode();
                    }
                }
            }
            catch(OperationCanceledException)when(token.IsCancellationRequested){break;}
            catch(Exception error)when(error is IOException or UnauthorizedAccessException or InvalidDataException or HttpRequestException or TaskCanceledException or JsonException or Microsoft.Data.Sqlite.SqliteException or NotSupportedException or InvalidOperationException)
            {try{using var failed=await http.PostAsJsonAsync("connector/v1/health",new ConnectorHealth(null,null,"publication-unavailable"),Json,token);}catch(HttpRequestException){}catch(TaskCanceledException){}}
            try{await Task.Delay(30000,token);}catch(OperationCanceledException){break;}
        }
    }
    public async Task LibraryAsync(CancellationToken token)
    {
        var lastSignature="";var lastProvider=DateTime.MinValue;var lastSuccess=DateTime.MinValue;var nextAttempt=DateTime.MinValue;
        while(!token.IsCancellationRequested)
        {
            try
            {
                if(settings.AutomaticEnabled&&settings.LibraryEnabled&&config.Accounts is not null&&!string.IsNullOrWhiteSpace(config.DesktopDataRoot)&&DateTime.UtcNow>=nextAttempt)
                {
                    var signature=LibrarySignature(config.DesktopDataRoot);var providers=DateTime.UtcNow-lastProvider>=TimeSpan.FromMinutes(15);
                    if(signature!=lastSignature||providers||DateTime.UtcNow-lastSuccess>=TimeSpan.FromMinutes(15))
                    {
                        var output=Path.Combine(data,"library-cache");
                        var catalog=await LibrarySnapshotExporter.ExportAsync(config.DesktopDataRoot,new WorkspaceManifest(1,config.AchievementAccounts??config.Accounts,DateTime.MinValue),output,providers,true,config.WorkspaceId);
                        if(providers)lastProvider=DateTime.UtcNow;
                        using var response=await http.PostAsJsonAsync("connector/v1/library/prepare",catalog,Json,token);response.EnsureSuccessStatusCode();
                        var plan=await response.Content.ReadFromJsonAsync<LibraryUploadPlan>(Json,token)??throw new InvalidDataException("Invalid library upload response.");
                        foreach(var asset in plan.MissingAssets)
                        {
                            if(!Regex.IsMatch(asset,"^[a-f0-9]{64}\\.(jpg|png|webp)$"))throw new InvalidDataException("Unexpected artwork request.");
                            var file=Path.Combine(output,"assets",asset);if(!File.Exists(file)||new FileInfo(file).Length>2*1024*1024)throw new InvalidDataException("The resized artwork is unavailable.");
                            using var bytes=new ByteArrayContent(await File.ReadAllBytesAsync(file,token));using var sent=await http.PostAsync($"connector/v1/library/{Uri.EscapeDataString(plan.Id)}/assets/{asset}",bytes,token);sent.EnsureSuccessStatusCode();
                        }
                        if(!plan.Current){using var committed=await http.PostAsync($"connector/v1/library/{Uri.EscapeDataString(plan.Id)}/commit",null,token);committed.EnsureSuccessStatusCode();}
                        lastSignature=signature;lastSuccess=DateTime.UtcNow;
                        using var healthy=await http.PostAsJsonAsync("connector/v1/health",new ConnectorHealth(null,""),Json,token);healthy.EnsureSuccessStatusCode();
                    }
                }
            }
            catch(OperationCanceledException)when(token.IsCancellationRequested){break;}
            catch(Exception error)when(error is IOException or UnauthorizedAccessException or InvalidDataException or HttpRequestException or TaskCanceledException or JsonException or System.Runtime.InteropServices.ExternalException or Microsoft.Data.Sqlite.SqliteException or InvalidOperationException or ArgumentException or KeyNotFoundException)
            {
                nextAttempt=DateTime.UtcNow.AddMinutes(1);
                try{using var failed=await http.PostAsJsonAsync("connector/v1/health",new ConnectorHealth(null,"library-unavailable"),Json,token);}catch(HttpRequestException){}catch(TaskCanceledException){}
            }
            try{await Task.Delay(30000,token);}catch(OperationCanceledException){break;}
        }
    }
    public async Task AchievementsAsync(CancellationToken token)
    {
        var hashes=new Dictionary<string,string>();
        while(!token.IsCancellationRequested)
        {
            try
            {
                if(settings.AutomaticEnabled&&settings.LibraryEnabled&&config.Accounts is not null&&!string.IsNullOrWhiteSpace(config.DesktopDataRoot))
                {
                    var rejected=false;
                    foreach(var game in AchievementSnapshotExporter.Read(config.DesktopDataRoot,config.WorkspaceId,config.AchievementAccounts??config.Accounts))
                    {
                        var key=game.Provider+":"+game.GameId;var hash=Hash(JsonSerializer.Serialize(game with{CapturedUtc=0},Json));
                        if(hashes.GetValueOrDefault(key)==hash)continue;
                        using var response=await http.PostAsJsonAsync("connector/v1/achievements/snapshot",game,Json,token);
                        if(response.StatusCode is System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.RequestEntityTooLarge){rejected=true;Console.Error.WriteLine("Achievement snapshot rejected: "+key+" ("+(int)response.StatusCode+"). Existing data kept.");continue;}
                        response.EnsureSuccessStatusCode();hashes[key]=hash;
                    }
                    var changes=await http.GetFromJsonAsync<AchievementGroupChange[]>("connector/v1/achievements/groups",Json,token)??[];
                    if(changes.Length>0)foreach(var ack in DesktopAchievementGroups.Apply(config.DesktopDataRoot,data,config.AchievementAccounts??config.Accounts,changes))
                    {using var sent=await http.PostAsJsonAsync("connector/v1/achievements/groups/ack",ack,Json,token);sent.EnsureSuccessStatusCode();}
                    using var healthy=await http.PostAsJsonAsync("connector/v1/health",new ConnectorHealth(null,null,null,rejected?"snapshot-rejected":""),Json,token);healthy.EnsureSuccessStatusCode();
                }
            }
            catch(OperationCanceledException)when(token.IsCancellationRequested){break;}
            catch(Exception error)when(error is IOException or UnauthorizedAccessException or InvalidDataException or HttpRequestException or TaskCanceledException or JsonException or Microsoft.Data.Sqlite.SqliteException or InvalidOperationException or ArgumentException)
            {try{using var failed=await http.PostAsJsonAsync("connector/v1/health",new ConnectorHealth(null,null,null,"achievement-sync-unavailable"),Json,token);}catch(HttpRequestException){}catch(TaskCanceledException){}}
            try{await Task.Delay(30000,token);}catch(OperationCanceledException){break;}
        }
    }
    static string LibrarySignature(string root)
    {
        var files=new List<string>{Path.Combine(root,"PixelVault.settings.ini")};var cache=Path.Combine(root,"cache");
        if(!Directory.Exists(cache))throw new DirectoryNotFoundException();
        files.AddRange(Directory.EnumerateFiles(cache).Where(f=>Path.GetFileName(f).StartsWith("library-folders-")||Path.GetFileName(f).StartsWith("pixelvault-index-")&&!f.EndsWith("-shm",StringComparison.OrdinalIgnoreCase)));
        foreach(var folder in new[]{Path.Combine(cache,"covers"),Path.Combine(root,"achievement-hub")})if(Directory.Exists(folder))files.AddRange(Directory.EnumerateFiles(folder));
        return Hash(string.Join("\n",files.Order(StringComparer.OrdinalIgnoreCase).Select(f=>{var info=new FileInfo(f);return f+"|"+info.Length+"|"+info.LastWriteTimeUtc.Ticks;})));
    }
}
