using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PixelVaultNative;

// Runs before WPF/Velopack initialization. The service supplies explicit roots; no signed-in profile is assumed.
static class CompanionReaderWorker
{
    internal static int Run(string requestFile)
    {
        LocalReaderResult result;
        try{SQLitePCL.Batteries_V2.Init();using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(65));result=ExecuteAsync(JsonSerializer.Deserialize<LocalReaderRequest>(File.ReadAllText(requestFile),AchievementTrackingCatalog.Json),timeout.Token).GetAwaiter().GetResult();}
        catch(Exception error)
        {
            result=new(){Error="reader-unavailable"};
            var diagnostic=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(requestFile)),"reader-diagnostic.private.txt");
            var operation="";try{operation=JsonDocument.Parse(File.ReadAllText(requestFile)).RootElement.GetProperty("Operation").GetString();}catch{}
            var detail=operation=="provider"?error.GetType().Name:error.GetType().Name+": "+error.Message;
            File.WriteAllText(diagnostic,detail.Length>8192?detail.Substring(0,8192):detail);
        }
        var output=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(requestFile)),"result.json");
        File.WriteAllText(output+".tmp",JsonSerializer.Serialize(result,AchievementTrackingCatalog.Json));File.Move(output+".tmp",output,true);return result.Success?0:1;
    }
    internal static async Task<LocalReaderResult> ExecuteAsync(LocalReaderRequest request,CancellationToken token)
    {
        if(request is null||!Path.IsPathFullyQualified(request.ApplicationRoot)||!Path.IsPathFullyQualified(request.CacheRoot)||!File.Exists(Path.Combine(request.ApplicationRoot,"achievement-tracking","pixelvault-tracking.sqlite")))throw new InvalidDataException();
        if(request.Operation=="provider")return await Provider(request,token).ConfigureAwait(false);
        if(request.Operation=="access")
        {
            using var store=new FileStream(Path.Combine(request.ApplicationRoot,"achievement-tracking","pixelvault-tracking.sqlite"),FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
            var repository=new TrackerRepository(request.ApplicationRoot,pooling:false,initializeSchema:false);repository.ValidateSharedSchema();var definitions=repository.Definitions().ToDictionary(d=>d.Id);
            foreach(var binding in repository.Sources())
            {
                if(!definitions.TryGetValue(binding.TrackerId,out var definition)||!request.Accounts.TryGetValue(definition.Provider,out var approved)||AchievementTrackingCatalog.AccountKey(definition.Provider,approved)!=binding.Account)continue;
                if(binding.AdapterId=="death-stranding-dc.fabrication")
                {
                    new DeathStrandingTrackerSource(request.CacheRoot,Environment.ProcessPath).Validate(binding,definition);
                    var source=DeathStrandingTrackerSource.Candidates(DeathStrandingTrackerSource.ResolveProfileRoot(binding.Location,binding.Account),binding.SpecificSave).First();
                    using var check=new FileStream(source.FullName,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
                    using var codec=File.OpenRead(Path.Combine(binding.GameDirectory,"oo2core_7_win64.dll"));
                }
                else if(binding.AdapterId=="pixelvault.json-file"){using var check=File.OpenRead(binding.Location);}
                // Other Native-only adapters remain owned by Native and are not scheduled by this host.
            }
            if(request.RequireProviderCredentials)
            {
                var settings=File.ReadLines(Path.Combine(request.ApplicationRoot,"PixelVault.settings.ini")).Where(l=>l.Contains('=')).Select(l=>l.Split(new[]{'='},2)).ToDictionary(p=>p[0].Trim(),p=>p[1].Trim(),StringComparer.OrdinalIgnoreCase);
                foreach(var provider in request.Accounts.Keys)if(!settings.TryGetValue(provider=="steam"?"steam_web_api_key":"retroachievements_api_key",out var key)||string.IsNullOrWhiteSpace(key))throw new InvalidDataException();
            }
            return new(){Success=true};
        }
        var b=request.Binding;var d=request.Definition;
        if(b is null||d is null||!request.Accounts.TryGetValue(d.Provider,out var account)||AchievementTrackingCatalog.AccountKey(d.Provider,account)!=b.Account||b.TrackerId!=d.Id)throw new InvalidDataException();
        ITrackerDataSource adapter=b.AdapterId switch
        {
            "death-stranding-dc.fabrication"=>new DeathStrandingTrackerSource(request.CacheRoot,Environment.ProcessPath),
            "pixelvault.json-file"=>new JsonFileTrackerSource(),
            _=>throw new NotSupportedException()
        };
        adapter.Validate(b,d);var detector=adapter as ITrackerSourceChangeDetector??throw new NotSupportedException();var signature=detector.ChangeSignature(b);
        if(request.Operation=="probe")return new(){Success=true,Signature=signature};
        if(request.Operation!="read")throw new InvalidDataException();
        var observation=await adapter.ObserveAsync(b,d,token).ConfigureAwait(false);
        if(signature!=detector.ChangeSignature(b))throw new IOException();
        return new(){Success=true,Signature=signature,Fingerprint=observation.Fingerprint,SnapshotJson=observation.SnapshotJson};
    }
    static async Task<LocalReaderResult> Provider(LocalReaderRequest request,CancellationToken token)
    {
        if(request.Provider is not ("steam" or "retroachievements")||!request.Accounts.TryGetValue(request.Provider,out var account)||!int.TryParse(request.GameId,out var id)||id<=0)throw new InvalidDataException();
        var settings=File.ReadLines(Path.Combine(request.ApplicationRoot,"PixelVault.settings.ini")).Where(l=>l.Contains('=')).Select(l=>l.Split(new[]{'='},2)).ToDictionary(p=>p[0].Trim(),p=>p[1].Trim(),StringComparer.OrdinalIgnoreCase);
        var accountKey=request.Provider=="steam"?"steam_user_id_64":"retroachievements_username";
        if(!settings.TryGetValue(accountKey,out var configured)||!string.Equals(account,configured,request.Provider=="steam"?StringComparison.Ordinal:StringComparison.OrdinalIgnoreCase))throw new InvalidDataException();
        settings.TryGetValue("steam_web_api_key",out var steamKey);settings.TryGetValue("retroachievements_api_key",out var retroKey);
        var platform=request.Provider=="steam"?"Steam":"Emulation";
        var folder=new LibraryFolderInfo{Name="Game",PlatformLabel=platform,SteamAppId=request.Provider=="steam"?request.GameId:"",RetroAchievementsGameId=request.Provider=="retroachievements"?request.GameId:""};
        var result=await GameAchievementsFetchService.FetchAsync(platform,folder,steamKey,retroKey,request.Provider=="steam"?account:"",request.Provider=="retroachievements"?account:"","PixelVault Companion local reader",token).ConfigureAwait(false);
        if(!AchievementHubStore.CanStore(request.Provider,request.GameId,result))return new(){Error="provider-unavailable"};
        var guide=new AchievementGuideService(request.ApplicationRoot,initializeSchema:false);
        await new AchievementHubStore(Path.Combine(request.ApplicationRoot,"achievement-hub"),guide).SaveGameAsync(request.Provider,account,request.GameId,result,token).ConfigureAwait(false);
        if(request.Provider=="steam"&&result.SteamPlaytimeMinutes.HasValue)new LocalProviderSummaryStore(request.ApplicationRoot).Save(new("steam",request.GameId,account,result.SteamPlaytimeMinutes,null,result.CapturedUtcTicks));
        return new(){Success=true};
    }
}
