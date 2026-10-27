using System.Net;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using PixelVault.Companion;
using PixelVaultNative;
using System.Text.Json;

if(args.Length>0 && args[0]=="--prepare-workspace")
{
    if(args.Length is not (3 or 5) || args.Length==5 && args[3]!="--accounts-file")
        throw new ArgumentException("Use --prepare-workspace <desktop-data-root> <separate-companion-data-root> [--accounts-file <private-json-map>].");
    var approved=args.Length==5?JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(args[4])):null;
    var result=PrepareWorkspace.Copy(args[1],args[2],approved);
    Console.WriteLine($"Prepared and verified Companion workspace: {result.Trackers} trackers, {result.States} state rows, {result.History} history entries. Desktop data was opened read-only.");
    return;
}

if(args.Length==4&&args[0]=="--pair-connector")
{
    var pairOptions=new CompanionOptions{DataRoot=Path.GetFullPath(args[1])};var pairWorkspace=new TrackerWorkspace(pairOptions);
    new PcConnector(pairOptions,pairWorkspace,new GameLibrary(pairOptions,pairWorkspace)).Pair(args[3],args[2]);return;
}
if(args.Length==2&&args[0]=="--revoke-connector")
{
    var revokeOptions=new CompanionOptions{DataRoot=Path.GetFullPath(args[1])};var revokeWorkspace=new TrackerWorkspace(revokeOptions);
    new PcConnector(revokeOptions,revokeWorkspace,new GameLibrary(revokeOptions,revokeWorkspace)).Revoke();return;
}
if(args.Length==2&&args[0]=="--enable-automatic")
{
    var automaticOptions=new CompanionOptions{DataRoot=Path.GetFullPath(args[1])};var automaticWorkspace=new TrackerWorkspace(automaticOptions);
    new ConnectorSyncState(automaticOptions,automaticWorkspace).EnableLibraryAndAutomatic();Console.WriteLine("Automatic save checks and approved library updates enabled.");return;
}
if(args.Length==3&&args[0]=="--configure-achievement-accounts")
{
    var scopeOptions=new CompanionOptions{DataRoot=Path.GetFullPath(args[1])};var scopeWorkspace=new TrackerWorkspace(scopeOptions);AchievementAccountScope.Configure(scopeOptions,scopeWorkspace,JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(args[2]))??throw new InvalidDataException("Missing approved achievement accounts."));Console.WriteLine("Approved achievement accounts configured; tracker pairing unchanged.");return;
}

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 128 * 1024);
builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<IConfiguration>().GetSection("Companion").Get<CompanionOptions>() ?? new();
    options.DataRoot = Path.GetFullPath(options.DataRoot);
    Directory.CreateDirectory(options.DataRoot);
    return options;
});
builder.Services.AddSingleton<OwnerStore>();
builder.Services.AddSingleton<TrackerWorkspace>();
builder.Services.AddSingleton<GameLibrary>();
builder.Services.AddSingleton<ScreenshotLibrary>();
builder.Services.AddSingleton<GameSectionPreferences>();
builder.Services.AddSingleton<LibraryPreferences>();
builder.Services.AddSingleton<PcConnector>();
builder.Services.AddSingleton<ConnectorSyncState>();
builder.Services.AddSingleton<ConnectorLibrarySync>();
builder.Services.AddSingleton<AchievementStore>();
builder.Services.AddSingleton<AchievementIcons>();
builder.Services.AddDataProtection().SetApplicationName("PixelVault.Companion");
builder.Services.AddOptions<KeyManagementOptions>().Configure<CompanionOptions, ILoggerFactory>((o, settings, logs) =>
    o.XmlRepository = new FileSystemXmlRepository(new DirectoryInfo(Path.Combine(settings.DataRoot, "keys")), logs));
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie();
builder.Services.AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme).Configure<CompanionOptions>((o, settings) =>
{
    o.Cookie.Name = "PixelVault.Companion.Session";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = settings.AllowHttp ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    o.ExpireTimeSpan = TimeSpan.FromDays(7);
    o.SlidingExpiration = true;
    o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = 401; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = 403; return Task.CompletedTask; };
    o.Events.OnValidatePrincipal = c =>
    {
        var owner = c.HttpContext.RequestServices.GetRequiredService<OwnerStore>().Read();
        if (owner is null || c.Principal?.FindFirstValue(ClaimTypes.NameIdentifier) != owner.Name) c.RejectPrincipal();
        return Task.CompletedTask;
    };
});
builder.Services.AddAuthorization();
builder.Services.AddAntiforgery();
builder.Services.AddOptions<AntiforgeryOptions>().Configure<CompanionOptions>((o, settings) =>
{
    o.HeaderName = "X-CSRF-TOKEN";
    o.Cookie.Name = "PixelVault.Companion.Csrf";
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = settings.AllowHttp ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
});
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.AddPolicy("login", c => RateLimitPartition.GetFixedWindowLimiter(
        c.Connection.RemoteIpAddress?.ToString() ?? "local", _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 6, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
});
builder.Services.AddOptions<ForwardedHeadersOptions>().Configure<IConfiguration>((o, configuration) =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.ForwardLimit = 1;
    o.KnownProxies.Clear();
    o.KnownIPNetworks.Clear();
    // Never trust arbitrary clients' X-Forwarded-* headers. Proxy IPs must be explicitly configured.
    foreach (var address in configuration.GetSection("Companion:TrustedProxies").Get<string[]>() ?? [])
        o.KnownProxies.Add(IPAddress.Parse(address));
    if (o.KnownProxies.Count == 0) o.KnownProxies.Add(IPAddress.Loopback);
});

var app = builder.Build();
var settings = app.Services.GetRequiredService<CompanionOptions>();
_ = app.Services.GetRequiredService<OwnerStore>(); // Fail startup on an incompatible/unwritable data store.
_ = app.Services.GetRequiredService<TrackerWorkspace>();
app.UseExceptionHandler(handler => handler.Run(async c =>
{
    c.Response.StatusCode = 500;
    await c.Response.WriteAsJsonAsync(new { error = "The request could not be completed. Check the service logs." });
}));
app.UseForwardedHeaders();
app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'self'";
    context.Response.Headers["Referrer-Policy"] = "same-origin";
    if(context.Request.Path.StartsWithSegments("/connector/v1"))
    {
        context.Response.Headers.CacheControl="no-store";
        if(!context.RequestServices.GetRequiredService<PcConnector>().Authenticate(context.Request.Headers.Authorization.ToString()))
        {context.Response.StatusCode=401;return;}
        var size=context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
        if(size is not null&&!size.IsReadOnly)size.MaxRequestBodySize=context.Request.Path.StartsWithSegments("/connector/v1/screenshots")?(context.Request.Path.Value?.Contains("/originals/",StringComparison.Ordinal)==true?64*1024*1024:6*1024*1024):context.Request.Path.StartsWithSegments("/connector/v1/library/prepare")||context.Request.Path.StartsWithSegments("/connector/v1/trackers")||context.Request.Path.StartsWithSegments("/connector/v1/achievements/snapshot")?10*1024*1024:2*1024*1024;
    }
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.Headers.CacheControl = "no-cache, no-store";
        context.Response.Headers.Pragma = "no-cache";
        if (!settings.AllowHttp && !context.Request.IsHttps)
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsJsonAsync(new { error = "Use HTTPS, or explicitly enable AllowHttp for a trusted local preview." });
            return;
        }
    }
    await next();
});
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context =>
    {
        // A cached HTML shell can keep running an older UI against the updated API.
        // Versioned scripts alone cannot replace that cached shell on an existing phone.
        if (Path.GetExtension(context.File.Name) is ".html" or ".js" or ".css" or ".webmanifest")
        {
            context.Context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
            context.Context.Response.Headers.Pragma = "no-cache";
        }
    }
});
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api") && !HttpMethods.IsGet(context.Request.Method)
        && !HttpMethods.IsHead(context.Request.Method) && !HttpMethods.IsOptions(context.Request.Method))
    {
        try { await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context); }
        catch (AntiforgeryValidationException)
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsJsonAsync(new { error = "Your session changed. Reload the page and try again." });
            return;
        }
    }
    await next();
});

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "PixelVault Companion", version = "0.16.0" }));
var api = app.MapGroup("/api/v1");
api.AddEndpointFilter(async (context,next)=>
{
    var expected=context.HttpContext.Request.Headers["X-Companion-Workspace"].ToString();
    if(expected.Length>0 && expected!=context.HttpContext.RequestServices.GetRequiredService<TrackerWorkspace>().Identity)
        return Results.Conflict(new{error="The server workspace changed. Reconnect before editing this workspace."});
    try{return await next(context);}
    catch(KeyNotFoundException){return Results.NotFound(new{error="The tracker was not found."});}
    catch(InvalidDataException e){return Results.BadRequest(new{error=e.Message});}
    catch(InvalidOperationException e){return Results.Conflict(new{error=e.Message});}
});
api.MapGet("/antiforgery", (HttpContext c, IAntiforgery antiforgery) =>
    Results.Ok(new { token = antiforgery.GetAndStoreTokens(c).RequestToken }));
api.MapGet("/session", (HttpContext c, OwnerStore store, TrackerWorkspace workspace) => Results.Ok(new
{
    setupRequired = store.Read() is null,
    authenticated = c.User.Identity?.IsAuthenticated == true,
    name = c.User.Identity?.Name,
    demoEnabled = settings.DemoEnabled,
    readOnly = workspace.ReadOnly,
    workspaceMode = workspace.Sample?"sample":workspace.Manifest?.Mode??"empty",
    version = "0.16.0"
}));
api.MapPost("/setup", async (SetupRequest request, HttpContext c, OwnerStore store) =>
{
    if (store.Read() is not null) return Results.Conflict(new { error = "Setup is already complete. Sign in instead." });
    if (!store.Setup(request.Token ?? "", request.Name ?? "", request.Password ?? ""))
        return Results.BadRequest(new { error = "Check the setup token, name and password (12–128 characters)." });
    await SignIn(c, request.Name!.Trim());
    return Results.Ok(new { authenticated = true });
}).RequireRateLimiting("login");
api.MapPost("/login", async (LoginRequest request, HttpContext c, OwnerStore store) =>
{
    var owner = store.Authenticate(request.Name ?? "", request.Password ?? "");
    if (owner is null) return Results.Json(new { error = "The name or password is incorrect." }, statusCode: 401);
    await SignIn(c, owner.Name);
    return Results.Ok(new { authenticated = true });
}).RequireRateLimiting("login");
api.MapPost("/logout", async (HttpContext c) =>
{
    await c.SignOutAsync();
    return Results.Ok(new { authenticated = false });
}).RequireAuthorization();

api.MapGet("/games", (GameLibrary library) => Results.Ok(library.Games())).RequireAuthorization();
api.MapGet("/games/{provider}/{gameId}",(string provider,string gameId,GameLibrary library)=>Results.Ok(library.Game(provider,gameId))).RequireAuthorization();
api.MapGet("/games/{provider}/{gameId}/art/{kind}",(string provider,string gameId,string kind,GameLibrary library)=>
{
    var artwork=library.Artwork(provider,gameId,kind);return Results.File(artwork.Path,artwork.ContentType);
}).RequireAuthorization();
api.MapGet("/preferences/game-sections",(GameSectionPreferences preferences)=>Results.Ok(preferences.Load())).RequireAuthorization();
api.MapPost("/preferences/game-sections",(SaveGameSectionOrder request,GameSectionPreferences preferences)=>Results.Ok(preferences.Save(request))).RequireAuthorization();
api.MapGet("/preferences/library",(LibraryPreferences preferences)=>Results.Ok(preferences.Load())).RequireAuthorization();
api.MapPost("/preferences/library/sort",(SaveLibrarySort request,LibraryPreferences preferences)=>Results.Ok(preferences.Sort(request.Sort))).RequireAuthorization();
api.MapPost("/preferences/library/pins",(SaveLibraryPin request,LibraryPreferences preferences,GameLibrary library)=>Results.Ok(preferences.Pin(request.GameId,request.Pinned,library))).RequireAuthorization();
api.MapGet("/refresh/connection",(PcConnector connector)=>Results.Ok(connector.Connection())).RequireAuthorization();
api.MapPost("/refresh/automatic",(SetAutomaticUpdates request,ConnectorSyncState state)=>Results.Ok(state.SetAutomatic(request.Enabled))).RequireAuthorization();
api.MapPost("/refresh",(RequestGameRefresh request,PcConnector connector)=>Results.Ok(connector.Request(request))).RequireAuthorization();
api.MapGet("/refresh/{id}",(string id,PcConnector connector)=>Results.Ok(connector.Get(id))).RequireAuthorization();
api.MapPost("/refresh/{id}/review",(string id,ReviewGameRefresh request,PcConnector connector)=>Results.Ok(connector.Review(id,request))).RequireAuthorization();
api.MapGet("/workspace", (TrackerWorkspace workspace) => Results.Ok(new
{
    id=workspace.Identity,mode=workspace.Sample?"sample":workspace.Manifest?.Mode??"empty",workspace.ReadOnly,workspace.Sample,
    accounts=workspace.Sample?new Dictionary<string,string>():workspace.Manifest?.Accounts??new()
})).RequireAuthorization();
api.MapGet("/trackers", (TrackerWorkspace workspace) => Results.Ok(workspace.Trackers())).RequireAuthorization();
api.MapGet("/achievements/{provider}/{game}",(string provider,string game,AchievementStore achievements)=>Results.Ok(achievements.Game(provider,game))).RequireAuthorization();
api.MapGet("/achievements/{provider}/{game}/{id}",(string provider,string game,string id,AchievementStore achievements)=>Results.Ok(achievements.Detail(provider,game,id))).RequireAuthorization();
api.MapPost("/achievements/{provider}/{game}/{id}/groups",(string provider,string game,string id,EditAchievementGroups edit,AchievementStore achievements)=>Results.Ok(achievements.Edit(provider,game,id,edit))).RequireAuthorization();
api.MapGet("/achievements/{provider}/{game}/{id}/icon",async(string provider,string game,string id,AchievementStore achievements,AchievementIcons icons,HttpContext context)=>{
    var image=await icons.Get(provider,game,achievements.Data(provider,game,id).Icon,context.RequestAborted);return image is null?Results.NoContent():Results.File(image.Value.Path,image.Value.Type);
}).RequireAuthorization();
api.MapGet("/trackers/{id}",(string id,TrackerWorkspace workspace)=>Results.Ok(workspace.View(id))).RequireAuthorization();
api.MapPost("/trackers/{id}/items",(string id,TrackerEdit command,TrackerWorkspace workspace)=>
{
    if(workspace.ReadOnly)return Results.Json(new{error="This preview is read-only."},statusCode:403);
    var account=workspace.Account(id);var result=workspace.Repository.Edit(id,account,command);
    return Results.Ok(new{result,tracker=workspace.View(id)});
}).RequireAuthorization();
api.MapGet("/trackers/{id}/history",(string id,TrackerWorkspace workspace)=>
{
    var account=workspace.Account(id);
    return Results.Ok(workspace.Repository.History(id,account).Select(h=>new {h.Id,h.Kind,h.Label,h.CreatedUtc,h.UndoneUtc}));
}).RequireAuthorization();
api.MapPost("/trackers/{id}/history/{historyId:long}/undo",(string id,long historyId,UndoRequest request,TrackerWorkspace workspace)=>
{
    if(workspace.ReadOnly)return Results.Json(new{error="This preview is read-only."},statusCode:403);
    var account=workspace.Account(id);var result=workspace.Repository.UndoCommand(id,account,historyId,request.RequestId);
    return Results.Ok(new{result,tracker=workspace.View(id)});
}).RequireAuthorization();
var pc=app.MapGroup("/connector/v1");
api.MapGet("/screenshots/timeline",(int? offset,int? limit,string? console,string? through,string? version,ScreenshotLibrary media)=>Results.Ok(media.Timeline(offset??0,limit??60,console,through,version))).RequireAuthorization();
api.MapGet("/screenshots/recent",(int? limit,ScreenshotLibrary media)=>Results.Ok(media.Recent(limit??30))).RequireAuthorization();
api.MapGet("/screenshots/{provider}/{game}",(string provider,string game,int? offset,int? limit,string? console,string? version,ScreenshotLibrary media)=>Results.Ok(media.Page(provider,game,offset??0,limit??60,console,version))).RequireAuthorization();
api.MapGet("/screenshots/{provider}/{game}/photos/{id}",(string provider,string game,string id,ScreenshotLibrary media)=>Results.Ok(media.Photo(provider,game,id))).RequireAuthorization();
api.MapGet("/screenshots/{provider}/{game}/{id}/original",OriginalPhoto).RequireAuthorization();
api.MapGet("/screenshots/{provider}/{game}/{id}/metadata",(string provider,string game,string id,string? asset,ScreenshotLibrary media)=>PhotoDownload(provider,game,id,asset,media,true)).RequireAuthorization();
api.MapGet("/screenshots/{provider}/{game}/{id}/{kind}",(string provider,string game,string id,string kind,ScreenshotLibrary media)=>Results.File(media.Image(provider,game,id,kind),"image/jpeg")).RequireAuthorization();
pc.AddEndpointFilter(async(context,next)=>
{
    try{return await next(context);}
    catch(KeyNotFoundException){return Results.NotFound(new{error="Refresh not found."});}
    catch(InvalidDataException){return Results.BadRequest(new{error="The save observations were invalid."});}
    catch(InvalidOperationException){return Results.Conflict(new{error="The source, workspace or refresh changed. Request a fresh check."});}
});
pc.MapPost("/heartbeat",(PcConnector connector)=>{connector.Heartbeat();return Results.Ok(new{ok=true});});
pc.MapGet("/work",(PcConnector connector)=>Results.Json(connector.Work()));
pc.MapGet("/settings",(ConnectorSyncState state)=>Results.Ok(state.Settings()));
pc.MapPost("/automatic",(AutomaticSaveChange change,PcConnector connector)=>Results.Ok(connector.Automatic(change)));
pc.MapPost("/health",(ConnectorHealth health,ConnectorSyncState state)=>{
    state.Put("monitor-check");if(health.SourceError is not null)state.Put("source-error",health.SourceError.Length==0?"":"A configured save source is unavailable.");
    if(health.LibraryError is not null)state.Put("library-error",health.LibraryError.Length==0?"":"The PC indexed library could not be synchronized.");
    if(health.PublicationError is not null)state.Put("publication-error",health.PublicationError.Length==0?"":"New desktop trackers could not be published.");
    if(health.AchievementError is not null)state.Put("achievement-error",health.AchievementError.Length==0?"":"Achievements could not be synchronized.");return Results.Ok(new{ok=true});
});
pc.MapPost("/achievements/snapshot",(AchievementGameSync snapshot,AchievementStore achievements)=>Results.Ok(achievements.Receive(snapshot)));
pc.MapGet("/achievements/groups",(AchievementStore achievements)=>Results.Ok(achievements.Changes()));
pc.MapPost("/achievements/groups/ack",(AchievementGroupAck ack,AchievementStore achievements)=>{achievements.Ack(ack);return Results.Ok(new{ok=true});});
pc.MapPost("/trackers",(TrackerPublication publication,PcConnector connector)=>Results.Ok(connector.Publish(publication)));
pc.MapPost("/tracker-titles",(TrackerTitles titles,PcConnector connector)=>Results.Ok(connector.PublishTitles(titles)));
pc.MapPost("/library/prepare",(GameCatalogue catalog,ConnectorLibrarySync sync)=>Results.Ok(sync.Prepare(catalog)));
pc.MapPost("/library/{id}/assets/{name}",async(string id,string name,HttpRequest request,ConnectorLibrarySync sync)=>{
    using var buffer=new MemoryStream();await request.Body.CopyToAsync(buffer,request.HttpContext.RequestAborted);sync.Upload(id,name,buffer.ToArray());return Results.Ok(new{ok=true});
});
pc.MapPost("/library/{id}/commit",(string id,ConnectorLibrarySync sync)=>Results.Ok(sync.Commit(id)));
pc.MapGet("/screenshots/capabilities",(CompanionOptions options)=>Results.Ok(new{libraryEnabled=options.ScreenshotLibraryEnabled,metadata=true,retention=true,removals=true}));
pc.MapGet("/screenshots/removals",(ScreenshotLibrary media)=>Results.Ok(media.RemovalInventory()));
pc.MapPost("/screenshots/removals/{provider}/{game}",(string provider,string game,ScreenshotRemovalRequest request,ScreenshotLibrary media)=>Results.Ok(media.SetRemovals(provider,game,request)));
pc.MapPost("/screenshots/prepare",(ScreenshotCatalogue catalog,ScreenshotLibrary media)=>Results.Ok(media.Prepare(catalog)));
pc.MapPost("/screenshots/{id}/assets/{name}",async(string id,string name,HttpRequest request,ScreenshotLibrary media)=>{
    using var buffer=new MemoryStream();await request.Body.CopyToAsync(buffer,request.HttpContext.RequestAborted);media.Upload(id,name,buffer.ToArray());return Results.Ok(new{ok=true});
});
pc.MapPost("/screenshots/{id}/commit",(string id,ScreenshotLibrary media)=>Results.Ok(media.Commit(id)));
pc.MapPost("/screenshots/{id}/originals/{name}",async(string id,string name,HttpRequest request,ScreenshotLibrary media)=>{await media.UploadOriginal(id,name,request.Body,request.HttpContext.RequestAborted);return Results.Ok(new{ok=true});});
pc.MapGet("/screenshots/{provider}/{game}/{id}/original",OriginalPhoto);
pc.MapGet("/screenshots/{provider}/{game}/{id}/metadata",(string provider,string game,string id,string? asset,ScreenshotLibrary media)=>PhotoDownload(provider,game,id,asset,media,true));
pc.MapGet("/screenshots/{provider}/{game}",(string provider,string game,int? offset,int? limit,ScreenshotLibrary media)=>Results.Ok(media.Page(provider,game,offset??0,limit??60)));
pc.MapGet("/screenshots/{provider}/{game}/{id}/{kind}",(string provider,string game,string id,string kind,ScreenshotLibrary media)=>Results.File(media.Image(provider,game,id,kind),"image/jpeg"));
pc.MapPost("/jobs/{id}/{tracker}",(string id,string tracker,ConnectorObservation observation,PcConnector connector)=>Results.Ok(connector.Result(id,tracker,observation)));
app.Run();

static IResult OriginalPhoto(string provider,string game,string id,string? asset,ScreenshotLibrary media)=>PhotoDownload(provider,game,id,asset,media,false);
static IResult PhotoDownload(string provider,string game,string id,string? asset,ScreenshotLibrary media,bool metadata)
{
    var download=media.Original(provider,game,id,metadata);var original=download.Original;
    if(!string.IsNullOrEmpty(asset)&&asset!=original.Asset)return Results.Conflict(new{error="The original photo changed. Refresh photos and try again."});
    return Results.File(download.Path,original.ContentType,fileDownloadName:original.FileName,lastModified:new DateTimeOffset(original.LastWriteUtcTicks,TimeSpan.Zero),entityTag:new Microsoft.Net.Http.Headers.EntityTagHeaderValue("\""+original.Asset[..64]+"\""),enableRangeProcessing:true);
}

static Task SignIn(HttpContext context, string name) => context.SignInAsync(
    CookieAuthenticationDefaults.AuthenticationScheme,
    new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, name), new Claim(ClaimTypes.Name, name) },
        CookieAuthenticationDefaults.AuthenticationScheme)), new AuthenticationProperties { IsPersistent = true });

public sealed record SetupRequest(string? Token, string? Name, string? Password);
public sealed record LoginRequest(string? Name, string? Password);
public sealed record UndoRequest(string RequestId);
public partial class Program;
