using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PixelVault.Companion;
using PixelVaultNative;
using PixelVault.Companion.Connector;
using PixelVault.Companion.Export;

if(args.Length==2&&args[0]=="--death-stranding-read")return DeathStrandingSaveWorker.Run(args[1]);
if(args.Length==5&&args[0]=="--sync-screenshot-pilot")return await ScreenshotPilot.Run(args[1],args[2],args[3],int.Parse(args[4]));
if(args.Length==4&&args[0]=="--configure-automatic")
{
    var configFile=Path.GetFullPath(args[1]);var old=JsonSerializer.Deserialize<ConnectorPairing>(File.ReadAllText(configFile),new JsonSerializerOptions(JsonSerializerDefaults.Web))??throw new InvalidDataException("Missing pairing.");
    var manifest=JsonSerializer.Deserialize<WorkspaceManifest>(File.ReadAllText(args[3]))??throw new InvalidDataException("Missing approved manifest.");
    var identity=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(manifest))));
    if(identity!=old.WorkspaceId)throw new InvalidDataException("The PC and NAS workspaces do not match.");
    var updated=old with{Accounts=manifest.Accounts,DesktopDataRoot=Path.GetFullPath(args[2]),AchievementAccounts=AchievementSnapshotExporter.ConnectedAccounts(args[2],manifest.Accounts)};
    var temp=configFile+".tmp";File.WriteAllText(temp,JsonSerializer.Serialize(updated,new JsonSerializerOptions(JsonSerializerDefaults.Web)));File.Move(temp,configFile,true);Console.WriteLine("Approved PC library configured; pairing credentials retained.");return 0;
}
if(args.Length!=1)throw new ArgumentException("Use a private connector configuration file.");
var configPath=Path.GetFullPath(args[0]);var json=new JsonSerializerOptions(JsonSerializerDefaults.Web);
var config=JsonSerializer.Deserialize<ConnectorPairing>(File.ReadAllText(configPath),json)??throw new InvalidDataException("Missing PC pairing.");
var origin=new Uri(config.Url);
if(origin.Scheme is not ("http" or "https")||origin.AbsolutePath!="/"||!string.IsNullOrEmpty(origin.Query)||!string.IsNullOrEmpty(origin.UserInfo)||config.Token.Length!=64||config.Sources.Count==0)throw new InvalidDataException("Invalid PC pairing.");
using var mutex=new Mutex(true,"Local\\PixelVaultCompanionConnector-"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(configPath)))[..24],out var first);
if(!first)return 0;
using var stop=new CancellationTokenSource();Console.CancelKeyPress+=(_,eventArgs)=>{eventArgs.Cancel=true;stop.Cancel();};
using var http=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){BaseAddress=origin,Timeout=TimeSpan.FromSeconds(35)};
http.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",config.Token);
var data=Path.GetDirectoryName(configPath)!;var reader=new DeathStrandingTrackerSource(data,Environment.ProcessPath);
var automatic=new AutomaticConnection(http,config,data,reader);
var monitor=automatic.MonitorAsync(stop.Token);var librarySync=automatic.LibraryAsync(stop.Token);
var publication=automatic.PublicationAsync(stop.Token);
var achievements=automatic.AchievementsAsync(stop.Token);
var heartbeat=Task.Run(async()=>{
    while(!stop.IsCancellationRequested){try{using var response=await http.PostAsync("connector/v1/heartbeat",null,stop.Token);if(response.StatusCode==System.Net.HttpStatusCode.Unauthorized)Log("PC pairing is no longer valid.");}catch(HttpRequestException){}catch(TaskCanceledException){}try{await Task.Delay(10000,stop.Token);}catch(OperationCanceledException){}}
});
Log("Companion PC connection started. Save files remain read-only.");
while(!stop.IsCancellationRequested)
{
    try
    {
        using var response=await http.GetAsync("connector/v1/work",stop.Token);response.EnsureSuccessStatusCode();
        var text=await response.Content.ReadAsStringAsync(stop.Token);var work=string.IsNullOrWhiteSpace(text)?null:JsonSerializer.Deserialize<ConnectorWork>(text,json);
        if(work is not null)foreach(var task in work.Tasks)
        {
            var source=automatic.Sources.FirstOrDefault(s=>s.Definition.Id==task.TrackerId&&s.Binding.Id==task.BindingId&&s.Revision==task.Revision);
            ConnectorObservation result;
            try
            {
                if(source is null)throw new InvalidDataException("The source is outside this PC pairing.");
                TrackerSourceObservation observation;
                if(source.Binding.AdapterId=="death-stranding-dc.fabrication")observation=await reader.ObserveAsync(source.Binding,source.Definition,stop.Token);
                else if(source.Binding.AdapterId=="pixelvault.json-file")
                {
                    var info=new FileInfo(source.Binding.Location);if(!info.Exists||info.Length<1||info.Length>1536*1024)throw new InvalidDataException("Invalid configured progress file.");
                    var stamp=info.LastWriteTimeUtc;var bytes=await File.ReadAllBytesAsync(info.FullName,stop.Token);info.Refresh();
                    if(info.LastWriteTimeUtc!=stamp||info.Length!=bytes.Length)throw new IOException("The progress file changed during reading.");
                    observation=new(){Fingerprint=Convert.ToHexString(SHA256.HashData(bytes)),SnapshotJson=Encoding.UTF8.GetString(bytes)};
                }
                else throw new NotSupportedException();
                result=new(work.Lease,observation.Fingerprint,observation.SnapshotJson,null);
            }
            catch(OperationCanceledException)when(stop.IsCancellationRequested){throw;}
            catch(Exception error)when(error is IOException or InvalidDataException or NotSupportedException or TimeoutException or UnauthorizedAccessException or DeathStrandingReaderException or JsonException or System.ComponentModel.Win32Exception)
            {result=new(work.Lease,"",null,error is TimeoutException?"timeout":"save-unavailable");}
            using var saved=await http.PostAsJsonAsync("connector/v1/jobs/"+Uri.EscapeDataString(work.Id)+"/"+Uri.EscapeDataString(task.TrackerId),result,json,stop.Token);
            saved.EnsureSuccessStatusCode();Log(result.Error is null?"Save observations delivered.":"A configured save could not be read; saved progress was preserved.");
        }
        await Task.Delay(3000,stop.Token);
    }
    catch(OperationCanceledException)when(stop.IsCancellationRequested){break;}
    catch(Exception error)when(error is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
    {Log("Companion is unavailable or its pairing changed. Retrying shortly.");try{await Task.Delay(15000,stop.Token);}catch(OperationCanceledException){break;}}
}
stop.Cancel();await Task.WhenAll(heartbeat,monitor,librarySync,publication,achievements);return 0;
static void Log(string message)=>Console.WriteLine(DateTime.UtcNow.ToString("o")+" "+message);
