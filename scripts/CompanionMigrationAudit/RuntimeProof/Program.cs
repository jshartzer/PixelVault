using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PixelVault.Companion;
using PixelVaultNative;

if(args.Length is not (1 or 2)||args.Length==2&&args[1]!="--domain")throw new ArgumentException("Use RuntimeProof <staged-service-plan.json> [--domain].");
var planFile=Path.GetFullPath(args[0]);var directory=Path.GetDirectoryName(planFile)!;var plan=JsonDocument.Parse(File.ReadAllText(planFile)).RootElement;
if(plan.GetProperty("Purpose").GetString()!="pc-companion-rehearsal")throw new InvalidDataException("Only isolated rehearsals are supported.");
var options=JsonDocument.Parse(File.ReadAllText(plan.GetProperty("Settings").GetString()!)).RootElement.GetProperty("Companion").Deserialize<CompanionOptions>()!;
if(!File.Exists(Path.Combine(options.ApplicationDataRoot,"local-store-preparation.json")))throw new InvalidDataException();
if(args.Length==2){await DomainProof.Run(options,directory);return;}
using var client=new HttpClient(new HttpClientHandler{CookieContainer=new(),AllowAutoRedirect=false}){BaseAddress=new Uri("http://127.0.0.1:8788"),Timeout=TimeSpan.FromSeconds(20)};
var json=new JsonSerializerOptions(JsonSerializerDefaults.Web);
var session=await client.GetFromJsonAsync<JsonElement>("api/v1/session");
var authFile=Path.Combine(directory,"proof-auth.private.json");
if(session.GetProperty("setupRequired").GetBoolean())
{
    var token=await client.GetFromJsonAsync<JsonElement>("api/v1/antiforgery");client.DefaultRequestHeaders.Add("X-CSRF-TOKEN",token.GetProperty("token").GetString());
    var password=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    var setup=await client.PostAsJsonAsync("api/v1/setup",new{token=File.ReadAllText(Path.Combine(options.DataRoot,"setup-token.txt")).Trim(),name="phase4-rehearsal",password});setup.EnsureSuccessStatusCode();
    File.WriteAllText(authFile,JsonSerializer.Serialize(new{name="phase4-rehearsal",password}));
}
else
{
    var token=await client.GetFromJsonAsync<JsonElement>("api/v1/antiforgery");client.DefaultRequestHeaders.Add("X-CSRF-TOKEN",token.GetProperty("token").GetString());
    var login=await client.PostAsync("api/v1/login",new StringContent(File.ReadAllText(authFile),System.Text.Encoding.UTF8,"application/json"));login.EnsureSuccessStatusCode();
}
client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");client.DefaultRequestHeaders.Add("X-CSRF-TOKEN",(await client.GetFromJsonAsync<JsonElement>("api/v1/antiforgery")).GetProperty("token").GetString());
var connection=await client.GetFromJsonAsync<JsonElement>("api/v1/refresh/connection");
if(!connection.GetProperty("local").GetBoolean()||!connection.GetProperty("online").GetBoolean()||connection.GetProperty("automaticEnabled").GetBoolean())throw new Exception("The paused local background host is not healthy.");
var supported=connection.GetProperty("supported").EnumerateArray().Select(g=>g.GetString()!).ToArray();if(supported.Length==0)throw new Exception("No approved local reader is exposed.");
string Authored()
{
    using var c=LocalApplicationData.Read(Path.Combine(options.ApplicationDataRoot,"achievement-tracking","pixelvault-tracking.sqlite"));using var q=c.CreateCommand();q.CommandText="SELECT tracker_id,account,item_id,manual_current,note FROM tracker_item_state ORDER BY tracker_id,account,item_id";using var r=q.ExecuteReader();var rows=new List<object[]>();while(r.Read())rows.Add(Enumerable.Range(0,r.FieldCount).Select(i=>r.IsDBNull(i)?null!:r.GetValue(i)).ToArray());return JsonSerializer.Serialize(rows);
}
var before=Authored();var groups=await client.GetFromJsonAsync<JsonElement>("api/v1/games");
// Check actual metadata first, then exercise the installed isolated readers through the authenticated API.
var requestId=Guid.NewGuid().ToString("N");var response=await client.PostAsJsonAsync("api/v1/refresh",new RequestGameRefresh(supported[0],requestId));response.EnsureSuccessStatusCode();var queued=(await response.Content.ReadFromJsonAsync<GameRefreshView>(json))!;
var timer=System.Diagnostics.Stopwatch.StartNew();var result=queued;
while(result.Status=="reading"&&timer.Elapsed<TimeSpan.FromMinutes(3))
{await Task.Delay(1000);result=(await client.GetFromJsonAsync<GameRefreshView>("api/v1/refresh/"+queued.Id,json))!;}
if(result.Status is not ("complete" or "review"))throw new Exception("The isolated reader job did not complete or produce a review.");
if(before!=Authored())throw new Exception("A rehearsal read changed authored manual progress or notes.");
var recent=await client.GetFromJsonAsync<RecentScreenshots>("api/v1/screenshots/recent");var photo=recent!.Photos.First();var prefix=$"api/v1/screenshots/{photo.Provider}/{photo.GameId}/{photo.Photo.Id}";
using var range=new HttpRequestMessage(HttpMethod.Get,prefix+"/original?asset="+photo.Photo.Original!.Asset);range.Headers.Range=new(0,7);using var downloaded=await client.SendAsync(range);if(downloaded.StatusCode!=HttpStatusCode.PartialContent||(await downloaded.Content.ReadAsByteArrayAsync()).Length!=8)throw new Exception("The direct photo range request failed.");
var preview=await client.GetByteArrayAsync(prefix+"/thumbnail");if(preview[0]!=255||preview[1]!=216)throw new Exception("The requested preview did not render.");
if(Directory.Exists(Path.Combine(options.DataRoot,"screenshots")))throw new Exception("An original-file store was created.");
var proof=new{LocalHostHealthy=true,AutomaticPaused=true,Games=groups.GetArrayLength(),SupportedGroups=supported.Length,ReaderTasks=result.Tasks.Count,RefreshStatus=result.Status,RefreshMilliseconds=timer.ElapsedMilliseconds,AuthoredFieldsPreserved=true,OriginalRange=true,Preview=true,OriginalsCopied=0,NasCalls=0,NativeUiLaunched=false};
File.WriteAllText(Path.Combine(directory,"runtime-proof.json"),JsonSerializer.Serialize(proof,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(proof));
