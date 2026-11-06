using System.Security.Principal;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PixelVault.Companion;
using PixelVaultNative;

if(args.Length is not (1 or 2)||args.Length==2&&args[1]!="--console")throw new ArgumentException("Use CredentialProof <rehearsal-plan> [--console].");
var planFile=Path.GetFullPath(args[0]);
if(args.Length==2){Console.WriteLine(await Check.Run(planFile,false));return;}
var builder=Host.CreateApplicationBuilder();
builder.Logging.ClearProviders();
builder.Services.AddWindowsService(o=>o.ServiceName="PixelVaultCompanionPhase4");
builder.Services.AddHostedService(sp=>new ProofWorker(planFile,sp.GetRequiredService<IHostApplicationLifetime>()));
await builder.Build().RunAsync();

sealed class ProofWorker(string plan,IHostApplicationLifetime lifetime):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        await Task.Yield();
        try{await Check.Run(plan,true);}finally{lifetime.StopApplication();}
    }
}
static class Check
{
    internal static async Task<string> Run(string planFile,bool service)
    {
        if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException();
        var directory=Path.GetDirectoryName(planFile)!;
        var resultFile=Path.Combine(directory,"credential-console-proof.json");
        var results=new List<object>();
        try
        {
            var plan=JsonDocument.Parse(File.ReadAllText(planFile)).RootElement;
            if(plan.GetProperty("Purpose").GetString()!="pc-companion-rehearsal"||plan.GetProperty("ServiceName").GetString()!="PixelVaultCompanionPhase4")throw new InvalidDataException();
            if(service)resultFile=Path.Combine(plan.GetProperty("StateRoot").GetString()!,"credential-proof.json");
            if(service&&!WindowsIdentity.GetCurrent().Name.Equals(@"NT SERVICE\PixelVaultCompanionPhase4",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Wrong identity.");
            var options=JsonDocument.Parse(File.ReadAllText(plan.GetProperty("Settings").GetString()!)).RootElement.GetProperty("Companion").Deserialize<CompanionOptions>()!;
            options.ValidateRoots();
            if(JsonDocument.Parse(File.ReadAllText(Path.Combine(options.ApplicationDataRoot,"local-store-preparation.json"))).RootElement.GetProperty("purpose").GetString()!="pc-companion-rehearsal")throw new InvalidDataException();
            using var workspace=new TrackerWorkspace(options);
            var state=new ConnectorSyncState(options,workspace);
            if(state.Settings().AutomaticEnabled)throw new InvalidOperationException("Rehearsal must remain paused.");
            var accounts=AchievementAccountScope.Read(options,workspace);
            if(accounts.Count!=2||!accounts.ContainsKey("steam")||!accounts.ContainsKey("retroachievements"))throw new InvalidDataException();
            var before=Authored(options.ApplicationDataRoot);
            var reader=new LocalReaderClient(options);
            using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(4));
            await reader.Run(new(){Operation="access",ApplicationRoot=options.ApplicationDataRoot,CacheRoot=options.CacheRoot,Accounts=accounts,RequireProviderCredentials=true},timeout.Token);
            var games=new GameLibrary(options,workspace).Games();
            foreach(var provider in new[]{"steam","retroachievements"})
            {
                var game=games.Where(g=>g.Provider==provider).OrderByDescending(g=>g.GameId=="1850570").ThenByDescending(g=>g.Total).First();
                await reader.Run(new(){Operation="provider",ApplicationRoot=options.ApplicationDataRoot,CacheRoot=options.CacheRoot,Accounts=accounts,Provider=provider,GameId=game.GameId},timeout.Token);
                results.Add(new{Provider=provider,AuthenticatedProgressRead=true,PackagedReader=true});
            }
            if(before!=Authored(options.ApplicationDataRoot))throw new InvalidOperationException("Authored tracker fields changed.");
            var json=JsonSerializer.Serialize(new{Passed=true,ServiceIdentityVerified=service,Utc=DateTime.UtcNow,Providers=results,AutomaticPaused=true,AuthoredFieldsPreserved=true,LiveDataChanged=false},new JsonSerializerOptions{WriteIndented=true});
            Write(resultFile,json);return json;
        }
        catch(Exception error)
        {
            // Never persist native/provider exception messages: URLs can contain API keys.
            var json=JsonSerializer.Serialize(new{Passed=false,ServiceIdentityVerified=service,Utc=DateTime.UtcNow,Providers=results,ErrorType=error.GetType().Name,LiveDataChanged=false},new JsonSerializerOptions{WriteIndented=true});
            Write(resultFile,json);return json;
        }
    }
    static void Write(string path,string json){File.WriteAllText(path+".tmp",json);File.Move(path+".tmp",path,true);}
    static string Authored(string root)
    {
        using var c=LocalApplicationData.Read(Path.Combine(root,"achievement-tracking","pixelvault-tracking.sqlite"));
        using var q=c.CreateCommand();q.CommandText="SELECT tracker_id,account,item_id,manual_current,note FROM tracker_item_state ORDER BY tracker_id,account,item_id";
        using var r=q.ExecuteReader();var rows=new List<object[]>();while(r.Read())rows.Add(Enumerable.Range(0,r.FieldCount).Select(i=>r.IsDBNull(i)?null!:r.GetValue(i)).ToArray());return JsonSerializer.Serialize(rows);
    }
}
