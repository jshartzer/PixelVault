using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace PixelVault.Companion;

public sealed class AchievementIcons
{
    static readonly HttpClient Http=new(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(10)};
    readonly CompanionOptions options;
    readonly HttpClient http;
    readonly long cacheBytes;
    public AchievementIcons(CompanionOptions options):this(options,Http) { }
    internal AchievementIcons(CompanionOptions options,HttpClient http,long cacheBytes=128L*1024*1024) { this.options=options;this.http=http;this.cacheBytes=cacheBytes; }
    static readonly SemaphoreSlim Downloads=new(4);
    static readonly SemaphoreSlim CacheWrites=new(1);
    readonly System.Collections.Concurrent.ConcurrentDictionary<string,DateTime> failures=new();
    public static string? Canonical(string provider,string game,string value)
    {
        if(!Uri.TryCreate(value,UriKind.Absolute,out var uri)||uri.Scheme!="https"||uri.UserInfo.Length>0||!uri.IsDefaultPort)return null;
        if(provider=="xbox"&&uri.Host=="images-eds-ssl.xboxlive.com"&&uri.AbsolutePath=="/image"&&uri.Query.Length>1&&value.Length<=8192)
            return uri.GetLeftPart(UriPartial.Query);
        if(provider=="xbox"&&uri.Host=="image-ssl.xboxlive.com"&&uint.TryParse(game,out var title)&&title>0
            &&uri.Query.Length==0&&Regex.IsMatch(uri.AbsolutePath,"^/global/t\\."+title.ToString("x8")+"/ach/0/[a-f0-9]{1,8}$"))
            return uri.GetLeftPart(UriPartial.Path);
        if(provider=="steam"&&new[]{"shared.fastly.steamstatic.com","shared.akamai.steamstatic.com","cdn.akamai.steamstatic.com","cdn.cloudflare.steamstatic.com","steamcdn-a.akamaihd.net","media.steampowered.com"}.Contains(uri.Host))
        {var match=Regex.Match(uri.AbsolutePath,"^/(steamcommunity/public|community_assets)/images/apps/"+Regex.Escape(game)+"/[a-fA-F0-9]{40}\\.(jpg|png)$");return match.Success?"https://shared.fastly.steamstatic.com"+uri.AbsolutePath.Replace("/steamcommunity/public/images/apps/","/community_assets/images/apps/",StringComparison.Ordinal):null;}
        return provider=="retroachievements"&&uri.Host=="media.retroachievements.org"&&Regex.IsMatch(uri.AbsolutePath,"^/Badge/[0-9A-Za-z_-]{1,80}\\.png$")?"https://media.retroachievements.org"+uri.AbsolutePath:null;
    }
    public Task<(string Path,string Type)?> Get(string provider,string game,CompanionAchievement achievement,CancellationToken token)
    {
        var earned=achievement.ProgressKnown&&achievement.Unlocked;
        var icon=!earned&&!string.IsNullOrWhiteSpace(achievement.LockedIcon)?achievement.LockedIcon:achievement.Icon;
        return Get(provider,game,icon,token,earned);
    }
    public async Task<(string Path,string Type)?> Get(string provider,string game,string icon,CancellationToken token,bool allowArtwork=true)
    {
        var century=PixelVault.Tracking.CenturyAchievementCatalog.ForGame(provider,game)
            .Any(d=>icon==PixelVault.Tracking.CenturyAchievementCatalog.ArtworkUri(d.Id,true)||icon==PixelVault.Tracking.CenturyAchievementCatalog.ArtworkUri(d.Id,false));
        var url=Canonical(provider,game,icon);if(url is null&&!century)return null;
        var artwork=century?PixelVaultNative.AchievementArtwork.Find(icon):allowArtwork?PixelVaultNative.AchievementArtwork.Find(url,options.ArtworkDataRoot):null;
        if(artwork is not null)
        {
            var artRoot=options.IconCacheRoot;Directory.CreateDirectory(artRoot);
            var artPath=Path.Combine(artRoot,"art-"+artwork.Sha256+".png");
            if(!File.Exists(artPath))
            {
                var temp=artPath+"."+Guid.NewGuid().ToString("N")+".tmp";
                try{await File.WriteAllBytesAsync(temp,artwork.Bytes,token);File.Move(temp,artPath,true);}
                finally{if(File.Exists(temp))File.Delete(temp);}
            }
            return(artPath,"image/png");
        }
        if(url is null)return null;
        var key=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))).ToLowerInvariant();var root=options.IconCacheRoot;Directory.CreateDirectory(root);var path=Path.Combine(root,key+".img");
        if(File.Exists(path))return(path,Content(await File.ReadAllBytesAsync(path,token))??"image/png");
        if(failures.TryGetValue(key,out var retry)&&retry>DateTime.UtcNow)return null;
        await Downloads.WaitAsync(token);
        try
        {
            if(File.Exists(path))return(path,Content(await File.ReadAllBytesAsync(path,token))??"image/png");
            using var response=await http.GetAsync(url,HttpCompletionOption.ResponseHeadersRead,token);response.EnsureSuccessStatusCode();
            var limit=provider=="xbox"?8*1024*1024:786432;
            if(response.Content.Headers.ContentLength>limit)return null;
            using var stream=await response.Content.ReadAsStreamAsync(token);using var memory=new MemoryStream();var buffer=new byte[16384];int read;
            while((read=await stream.ReadAsync(buffer,token))>0){if(memory.Length+read>limit)return null;memory.Write(buffer,0,read);}
            var bytes=memory.ToArray();var type=Content(bytes);if(type is null)return null;
            await CacheWrites.WaitAsync(token);
            try
            {
                if(File.Exists(path))return(path,type);
                if(bytes.LongLength>cacheBytes)return null;
                // Only downloaded, reproducible images are evictable. Imported artwork lives elsewhere.
                var cached=new DirectoryInfo(root).EnumerateFiles("*.img")
                    .Where(f=>Regex.IsMatch(f.Name,"^[a-f0-9]{64}\\.img$")&&(f.Attributes&FileAttributes.ReparsePoint)==0)
                    .OrderBy(f=>f.LastWriteTimeUtc).ToArray();
                var size=cached.Sum(f=>f.Length);
                if(size+bytes.LongLength>cacheBytes)
                    foreach(var file in cached)
                    {
                        if(size+bytes.LongLength<=cacheBytes*3/4)break;
                        try{var length=file.Length;file.Delete();size-=length;}
                        catch(IOException){}catch(UnauthorizedAccessException){} // An in-use image can wait for the next pass.
                    }
                if(size+bytes.LongLength>cacheBytes)return null;
                var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
                try{await File.WriteAllBytesAsync(temp,bytes,token);if(!OperatingSystem.IsWindows())File.SetUnixFileMode(temp,UnixFileMode.UserRead|UnixFileMode.UserWrite);File.Move(temp,path,true);}
                finally{if(File.Exists(temp))File.Delete(temp);}
                return(path,type);
            }
            finally{CacheWrites.Release();}
        }
        catch(Exception error)when(error is HttpRequestException or IOException or TaskCanceledException){failures[key]=DateTime.UtcNow.AddMinutes(5);return null;}
        finally{Downloads.Release();}
    }
    static string? Content(byte[] bytes)=>bytes.Length<12?null:bytes[0]==255&&bytes[1]==216?"image/jpeg":bytes.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10})?"image/png":null;
}
