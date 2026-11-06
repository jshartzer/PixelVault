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

if(args.Length==2&&args[0]=="--prepare-canonical-direct-local")
{
    DirectStorePreparation.InitializeCanonical(args[1]);Console.WriteLine("Prepared existing canonical schemas from the frozen cutover guard.");return;
}

if(args.Length==2&&args[0]=="--enable-local-automatic")
{
    var localOptions=JsonDocument.Parse(File.ReadAllText(args[1])).RootElement.GetProperty("Companion").Deserialize<CompanionOptions>()??throw new InvalidDataException("Missing explicit local settings.");
    localOptions.ValidateRoots();if(!localOptions.DirectLocal||!localOptions.LocalWorkersEnabled)throw new InvalidDataException("Explicit direct-local readers are required.");
    using var localWorkspace=new TrackerWorkspace(localOptions);new ConnectorSyncState(localOptions,localWorkspace).EnableLibraryAndAutomatic();
    Console.WriteLine("Approved local automatic work enabled in the explicit installation.");return;
}

if(args.Length==4&&args[0]=="--prepare-local-service")
{
    Console.WriteLine(JsonSerializer.Serialize(LocalServicePreparation.Prepare(args[1],args[2],args[3])));return;
}

if(args.Length==3&&args[0]=="--prepare-direct-local-photos")
{
    Console.WriteLine(JsonSerializer.Serialize(LocalPhotoPreparation.FromEvidence(args[1],args[2])));return;
}

if(args.Length==2&&args[0]=="--prepare-direct-local-schema")
{
    DirectStorePreparation.Initialize(args[1]);Console.WriteLine("Prepared shared schemas in the isolated rehearsal store.");return;
}

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
if(args.Length==2&&args[0]=="--enable-web-tracker-edits")
{
    var o=new CompanionOptions{DataRoot=Path.GetFullPath(args[1])};var w=new TrackerWorkspace(o);new ConnectorSyncState(o,w).EnableTrackerWebEdits();Console.WriteLine("Web tracker return sync enabled for the existing paired workspace.");return;
}
if(args.Length==2&&args[0]=="--verify-achievement-sync")
{
    using var config=JsonDocument.Parse(File.ReadAllText(args[1]));
    var verifyOptions=config.RootElement.GetProperty("Companion").Deserialize<CompanionOptions>()??throw new InvalidDataException("Missing installation options.");
    using var verifyWorkspace=new TrackerWorkspace(verifyOptions);
    var data=verifyWorkspace.LocalData??throw new InvalidDataException("A direct-local installation is required.");
    var games=data.Games();var achievements=data.Achievements();
    Console.WriteLine(JsonSerializer.Serialize(new{Games=games.Count,Providers=achievements.GroupBy(g=>g.Provider).Select(g=>new{Provider=g.Key,Games=g.Count(),Achievements=g.Sum(a=>a.Rows.Count),Earned=g.Sum(a=>a.Rows.Count(r=>r.ProgressKnown&&r.Unlocked))})}));return;
}
if(args.Length==3&&args[0]=="--configure-achievement-accounts")
{
    var scopeOptions=new CompanionOptions{DataRoot=Path.GetFullPath(args[1])};var scopeWorkspace=new TrackerWorkspace(scopeOptions);AchievementAccountScope.Configure(scopeOptions,scopeWorkspace,JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(args[2]))??throw new InvalidDataException("Missing approved achievement accounts."));Console.WriteLine("Approved achievement accounts configured; tracker pairing unchanged.");return;
}

var installationConfig=args.Length==2&&args[0]=="--installation-config"?Path.GetFullPath(args[1]):null;
if(installationConfig is not null)
{
    using var checkedConfiguration=JsonDocument.Parse(File.ReadAllText(installationConfig));
    var explicitOptions=checkedConfiguration.RootElement.GetProperty("Companion").Deserialize<CompanionOptions>()??throw new InvalidDataException("Missing explicit service roots.");
    if(!explicitOptions.DirectLocal||string.IsNullOrWhiteSpace(explicitOptions.DataRoot)||!Path.IsPathFullyQualified(explicitOptions.DataRoot)||!checkedConfiguration.RootElement.TryGetProperty("Urls",out var urls)||string.IsNullOrWhiteSpace(urls.GetString()))throw new InvalidDataException("A service configuration requires direct-local mode, explicit roots and listen addresses.");
    explicitOptions.ValidateRoots();
}
var builder = WebApplication.CreateBuilder(new WebApplicationOptions{Args=installationConfig is null?args:[],ContentRootPath=installationConfig is null?null:AppContext.BaseDirectory});
if(installationConfig is not null)builder.Configuration.AddJsonFile(installationConfig,optional:false,reloadOnChange:false);
builder.Host.UseWindowsService(o=>o.ServiceName=builder.Configuration["Companion:ServiceName"]??"PixelVaultCompanion");
if(installationConfig is not null)
{
    var stateRoot=builder.Configuration["Companion:DataRoot"]??throw new InvalidDataException("Missing service state root.");
    builder.Logging.ClearProviders();builder.Logging.AddProvider(new BoundedRuntimeLog(Path.Combine(stateRoot,"logs")));
}

builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 128 * 1024);
builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<IConfiguration>().GetSection("Companion").Get<CompanionOptions>() ?? new();
    options.ValidateRoots();
    Directory.CreateDirectory(options.DataRoot);
    return options;
});
builder.Services.AddSingleton<OwnerStore>();
builder.Services.AddSingleton<TrackerWorkspace>();
builder.Services.AddSingleton<GameLibrary>();
builder.Services.AddSingleton<MapLibrary>();
builder.Services.AddSingleton<MapTileCache>();
builder.Services.AddSingleton<ScreenshotLibrary>();
builder.Services.AddSingleton<ILocalReaderClient,LocalReaderClient>();
builder.Services.AddSingleton<LocalRefreshService>();
builder.Services.AddSingleton<LocalArtworkCache>();
builder.Services.AddHostedService<LocalRuntimeHost>();
builder.Services.AddSingleton<GameSectionPreferences>();
builder.Services.AddSingleton<LibraryPreferences>();
builder.Services.AddSingleton<PcConnector>();
builder.Services.AddSingleton<ConnectorSyncState>();
builder.Services.AddSingleton<ConnectorLibrarySync>();
builder.Services.AddSingleton<AchievementStore>();
builder.Services.AddSingleton<AchievementIcons>();
builder.Services.AddSingleton<AchievementArtworkSync>();
var protection=builder.Services.AddDataProtection().SetApplicationName("PixelVault.Companion");
if(OperatingSystem.IsWindows()&&builder.Configuration["Companion:StorageMode"]=="direct-local")protection.ProtectKeysWithDpapi(protectToLocalMachine:true);
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
builder.Services.AddResponseCompression(o=>{o.EnableForHttps=false;});
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
if(settings.DirectLocal){_=app.Services.GetRequiredService<GameLibrary>();_=app.Services.GetRequiredService<AchievementStore>();_=app.Services.GetRequiredService<TrackerWorkspace>().LocalPhotos;}
app.UseExceptionHandler(handler => handler.Run(async c =>
{
    c.Response.StatusCode = 500;
    await c.Response.WriteAsJsonAsync(new { error = "The request could not be completed. Check the service logs." });
}));
app.UseForwardedHeaders();
app.UseResponseCompression();
app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; object-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'self'";
    context.Response.Headers["Referrer-Policy"] = "same-origin";
    if(context.Request.Path.StartsWithSegments("/connector/v1"))
    {
        if(settings.DirectLocal){context.Response.StatusCode=410;return;}
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
var browserAssets=new BrowserAssets(app.Environment.WebRootFileProvider);
app.Use(async(context,next)=>{
    if(context.Request.Method is "GET" or "HEAD"&&context.Request.Path is {Value:"/" or "/index.html"})
    {
        context.Response.ContentType="text/html; charset=utf-8";context.Response.Headers.CacheControl="no-cache, no-store, must-revalidate";context.Response.Headers.Pragma="no-cache";
        if(context.Request.Method=="GET")await context.Response.WriteAsync(browserAssets.Html());return;
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
        if(Path.GetExtension(context.File.Name) is ".js" or ".css"&&context.Context.Request.Query["v"]==browserAssets.Version(context.Context.Request.Path.Value!))
            context.Context.Response.Headers.CacheControl="public, max-age=31536000, immutable";
        else if (Path.GetExtension(context.File.Name) is ".html" or ".js" or ".css" or ".webmanifest")
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

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "PixelVault Companion", version = typeof(AchievementIcons).Assembly.GetName().Version!.ToString(3) }));
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
    version = typeof(AchievementIcons).Assembly.GetName().Version!.ToString(3)
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
api.MapSavedMaps();
api.MapGet("/games/{provider}/{gameId}",(string provider,string gameId,GameLibrary library)=>Results.Ok(library.Game(provider,gameId))).RequireAuthorization();
api.MapGet("/games/{provider}/{gameId}/art/{kind}",async(string provider,string gameId,string kind,GameLibrary library,LocalArtworkCache cache,HttpContext context,CancellationToken token)=>
{
    context.Response.Headers.CacheControl="private, no-cache";context.Response.Headers.Remove("Pragma");
    var artwork=await cache.Get(library.Artwork(provider,gameId,kind),kind,token);return Results.File(artwork.Path,artwork.ContentType,entityTag:new Microsoft.Net.Http.Headers.EntityTagHeaderValue("\""+Path.GetFileNameWithoutExtension(artwork.Path)+"\""));
}).RequireAuthorization();
api.MapGet("/preferences/game-sections",(GameSectionPreferences preferences)=>Results.Ok(preferences.Load())).RequireAuthorization();
api.MapGet("/preferences/tracker-groups",(TrackerWorkspace workspace)=>Results.Ok(new{groups=workspace.ReadOnly?TrackerRepository.DefaultTrackerGroupOrder:workspace.Repository.TrackerGroupOrder()})).RequireAuthorization();
api.MapPost("/preferences/tracker-groups",(MoveTrackerGroupRequest request,TrackerWorkspace workspace)=>
{
    if(workspace.ReadOnly)return Results.Json(new{error="This workspace is read-only."},statusCode:403);
    return Results.Ok(new{groups=workspace.Repository.MoveTrackerGroup(request.Group,request.Direction)});
}).RequireAuthorization();
api.MapPost("/preferences/game-sections",(SaveGameSectionOrder request,GameSectionPreferences preferences)=>Results.Ok(preferences.Save(request))).RequireAuthorization();
api.MapGet("/preferences/library",(LibraryPreferences preferences,GameLibrary library)=>Results.Ok(preferences.Load(library))).RequireAuthorization();
api.MapPost("/preferences/library/sort",(SaveLibrarySort request,LibraryPreferences preferences,GameLibrary library)=>Results.Ok(preferences.Sort(request.Sort,library))).RequireAuthorization();
api.MapPost("/preferences/library/pins",(SaveLibraryPin request,LibraryPreferences preferences,GameLibrary library)=>Results.Ok(preferences.Pin(request.GameId,request.Pinned,library))).RequireAuthorization();
api.MapGet("/refresh/connection",(PcConnector connector,LocalRefreshService local)=>Results.Ok(settings.DirectLocal?local.Connection():connector.Connection())).RequireAuthorization();
api.MapPost("/refresh/automatic",(SetAutomaticUpdates request,ConnectorSyncState state)=>settings.DirectLocal&&!settings.LocalWorkersEnabled?Results.Conflict(new{error="Local readers are not enabled."}):Results.Ok(state.SetAutomatic(request.Enabled))).RequireAuthorization();
api.MapPost("/refresh",(RequestGameRefresh request,PcConnector connector,LocalRefreshService local)=>Results.Ok(settings.DirectLocal?local.Request(request):connector.Request(request))).RequireAuthorization();
api.MapGet("/refresh/{id}",(string id,PcConnector connector,LocalRefreshService local)=>Results.Ok(settings.DirectLocal?local.Get(id):connector.Get(id))).RequireAuthorization();
api.MapPost("/refresh/{id}/review",(string id,ReviewGameRefresh request,PcConnector connector,LocalRefreshService local)=>Results.Ok(settings.DirectLocal?local.Review(id,request):connector.Review(id,request))).RequireAuthorization();
api.MapGet("/workspace", (TrackerWorkspace workspace) => Results.Ok(new
{
    id=workspace.Identity,mode=workspace.Sample?"sample":workspace.Manifest?.Mode??"empty",workspace.ReadOnly,workspace.Sample,
    accounts=workspace.Sample?new Dictionary<string,string>():workspace.Manifest?.Accounts??new()
})).RequireAuthorization();
api.MapGet("/trackers", (TrackerWorkspace workspace) => Results.Ok(workspace.Trackers())).RequireAuthorization();
api.MapGet("/achievements/{provider}/{game}",(string provider,string game,AchievementStore achievements)=>Results.Ok(achievements.Game(provider,game))).RequireAuthorization();
api.MapGet("/century/{provider}/{game}/{id}/icon",(string provider,string game,string id,string state,AchievementStore achievements,HttpContext context)=>{
    var bytes=achievements.CenturyArtwork(provider,game,id,state);
    context.Response.Headers.CacheControl="private, max-age=3600";context.Response.Headers.Remove("Pragma");
    return Results.File(bytes,"image/png",entityTag:new Microsoft.Net.Http.Headers.EntityTagHeaderValue("\""+Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))+"\""));
}).RequireAuthorization();
api.MapGet("/achievements/{provider}/{game}/{id}",(string provider,string game,string id,AchievementStore achievements)=>Results.Ok(achievements.Detail(provider,game,id))).RequireAuthorization();
api.MapPost("/achievements/{provider}/{game}/{id}/groups",(string provider,string game,string id,EditAchievementGroups edit,AchievementStore achievements)=>Results.Ok(achievements.Edit(provider,game,id,edit))).RequireAuthorization();
api.MapGet("/achievements/{provider}/{game}/{id}/icon",async(string provider,string game,string id,AchievementStore achievements,AchievementIcons icons,HttpContext context)=>{
    var image=await icons.Get(provider,game,achievements.Data(provider,game,id),context.RequestAborted);
    if(image is null)return Results.NoContent();
    context.Response.Headers.CacheControl="private, max-age=3600";context.Response.Headers.Remove("Pragma");
    return Results.File(image.Value.Path,image.Value.Type,lastModified:File.GetLastWriteTimeUtc(image.Value.Path),entityTag:new Microsoft.Net.Http.Headers.EntityTagHeaderValue("\""+Path.GetFileNameWithoutExtension(image.Value.Path)+"\""));
}).RequireAuthorization();
api.MapGet("/achievements/{provider}/{game}/{id}/unlocked-icon",async(string provider,string game,string id,AchievementStore achievements,AchievementIcons icons,HttpContext context)=>{
    var image=await icons.Get(provider,game,achievements.Data(provider,game,id).Icon,context.RequestAborted);
    if(image is null)return Results.NoContent();
    context.Response.Headers.CacheControl="private, max-age=3600";context.Response.Headers.Remove("Pragma");
    return Results.File(image.Value.Path,image.Value.Type,lastModified:File.GetLastWriteTimeUtc(image.Value.Path),entityTag:new Microsoft.Net.Http.Headers.EntityTagHeaderValue("\""+Path.GetFileNameWithoutExtension(image.Value.Path)+"\""));
}).RequireAuthorization();
api.MapPost("/refresh/opened/{id}",(string id,TrackerWorkspace workspace,LocalRefreshService local)=>{_=workspace.Account(id);if(settings.DirectLocal)local.Opened(id);return Results.Ok(new{ok=true});}).RequireAuthorization();
api.MapGet("/trackers/{id}",(string id,bool? compact,TrackerWorkspace workspace)=>Results.Ok(workspace.View(id,compact==true))).RequireAuthorization();
api.MapPost("/trackers/{id}/icon",(string id,TrackerIconRequest request,TrackerWorkspace workspace)=>
{
    if(workspace.ReadOnly)return Results.Json(new{error="This preview is read-only."},statusCode:403);
    _=workspace.Account(id);
    workspace.Repository.SetTrackerIcon(id,request.IconId);
    return Results.Ok(new{id,request.IconId});
}).RequireAuthorization();
api.MapPost("/trackers/{id}/items",(string id,TrackerEdit command,TrackerWorkspace workspace)=>
{
    if(workspace.ReadOnly)return Results.Json(new{error="This preview is read-only."},statusCode:403);
    var account=workspace.Account(id);var result=workspace.Repository.Edit(id,account,command);
    return Results.Ok(new{result,tracker=workspace.View(id)});
}).RequireAuthorization();
api.MapPost("/trackers/{id}/item-rules",(string id,TrackerItemRuleEdit command,TrackerWorkspace workspace)=>
{
    if(workspace.ReadOnly)return Results.Json(new{error="This preview is read-only."},statusCode:403);
    _=workspace.Account(id); workspace.Repository.SetItemRules(id,command);
    return Results.Ok(new{tracker=workspace.View(id)});
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
api.MapGet("/screenshots/{provider}/{game}/{id}/metadata",(string provider,string game,string id,string? asset,ScreenshotLibrary media,CancellationToken token)=>PhotoDownload(provider,game,id,asset,media,true,token)).RequireAuthorization();
api.MapGet("/screenshots/{provider}/{game}/{id}/{kind}",async (string provider,string game,string id,string kind,ScreenshotLibrary media,HttpContext context,CancellationToken token)=>{
    context.Response.Headers.CacheControl="private, no-cache";context.Response.Headers.Remove("Pragma");
    var stream=await media.OpenImage(provider,game,id,kind,token);
    return Results.File(stream,"image/jpeg",entityTag:stream is FileStream file?new Microsoft.Net.Http.Headers.EntityTagHeaderValue("\""+Path.GetFileNameWithoutExtension(file.Name)+"\""):null);
}).RequireAuthorization();
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
pc.MapGet("/tracker-edits/{id}",(string id,int? offset,PcConnector connector)=>Results.Ok(connector.TrackerEdits(id,offset??0)));
pc.MapPost("/automatic",(AutomaticSaveChange change,PcConnector connector)=>Results.Ok(connector.Automatic(change)));
pc.MapPost("/health",(ConnectorHealth health,ConnectorSyncState state)=>{
    state.Put("monitor-check");if(health.SourceError is not null)state.Put("source-error",health.SourceError.Length==0?"":"A configured save source is unavailable.");
    if(health.LibraryError is not null)state.Put("library-error",health.LibraryError.Length==0?"":"The PC indexed library could not be synchronized.");
    if(health.PublicationError is not null)state.Put("publication-error",health.PublicationError.Length==0?"":"New desktop trackers could not be published.");
    if(health.AchievementError is not null)state.Put("achievement-error",health.AchievementError.Length==0?"":"Achievements could not be synchronized.");
    if(health.TrackerEditsError is not null){if(health.TrackerEditsError.Length==0)state.Put("tracker-edits-check");state.Put("tracker-edits-error",health.TrackerEditsError.Length==0?"":"Web tracker edits could not be delivered to the PC. Both copies were preserved.");}
    if(health.TrackerEditConflicts is >=0 and <=20000)state.Put("tracker-edits-conflicts",health.TrackerEditConflicts.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));return Results.Ok(new{ok=true});
});
pc.MapPost("/achievements/snapshot",(AchievementGameSync snapshot,AchievementStore achievements)=>Results.Ok(achievements.Receive(snapshot)));
pc.MapGet("/achievement-artwork",(AchievementArtworkSync artwork)=>Results.Ok(artwork.Inventory()));
pc.MapPost("/achievement-artwork",(AchievementArtworkTransfer transfer,AchievementArtworkSync artwork)=>Results.Ok(artwork.Receive(transfer)));
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
pc.MapGet("/screenshots/{provider}/{game}/{id}/metadata",(string provider,string game,string id,string? asset,ScreenshotLibrary media,CancellationToken token)=>PhotoDownload(provider,game,id,asset,media,true,token));
pc.MapGet("/screenshots/{provider}/{game}",(string provider,string game,int? offset,int? limit,ScreenshotLibrary media)=>Results.Ok(media.Page(provider,game,offset??0,limit??60)));
pc.MapGet("/screenshots/{provider}/{game}/{id}/{kind}",async (string provider,string game,string id,string kind,ScreenshotLibrary media,CancellationToken token)=>Results.File(await media.OpenImage(provider,game,id,kind,token),"image/jpeg"));
pc.MapPost("/jobs/{id}/{tracker}",(string id,string tracker,ConnectorObservation observation,PcConnector connector)=>Results.Ok(connector.Result(id,tracker,observation)));
app.Run();

static Task<IResult> OriginalPhoto(string provider,string game,string id,string? asset,ScreenshotLibrary media,CancellationToken token)=>PhotoDownload(provider,game,id,asset,media,false,token);
static async Task<IResult> PhotoDownload(string provider,string game,string id,string? asset,ScreenshotLibrary media,bool metadata,CancellationToken token)
{
    if(media.DirectLocal)
    {
        var local=await media.DownloadLocal(provider,game,id,asset,metadata,token);
        return Results.File(local.Stream,local.Original.ContentType,fileDownloadName:local.Original.FileName,lastModified:new DateTimeOffset(local.Original.LastWriteUtcTicks,TimeSpan.Zero),entityTag:new Microsoft.Net.Http.Headers.EntityTagHeaderValue("\""+local.Digest+"\""),enableRangeProcessing:true);
    }
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
public sealed record MoveTrackerGroupRequest(string Group,int Direction);
public partial class Program;
