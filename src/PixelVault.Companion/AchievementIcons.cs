using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace PixelVault.Companion;

public sealed class AchievementIcons(CompanionOptions options)
{
    static readonly HttpClient Http=new(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(10)};
    static readonly SemaphoreSlim Downloads=new(4);
    readonly System.Collections.Concurrent.ConcurrentDictionary<string,DateTime> failures=new();
    public static string? Canonical(string provider,string game,string value)
    {
        if(!Uri.TryCreate(value,UriKind.Absolute,out var uri)||uri.Scheme!="https"||uri.UserInfo.Length>0||!uri.IsDefaultPort)return null;
        if(provider=="steam"&&new[]{"shared.fastly.steamstatic.com","shared.akamai.steamstatic.com","cdn.akamai.steamstatic.com","cdn.cloudflare.steamstatic.com","steamcdn-a.akamaihd.net","media.steampowered.com"}.Contains(uri.Host))
        {var match=Regex.Match(uri.AbsolutePath,"^/(steamcommunity/public|community_assets)/images/apps/"+Regex.Escape(game)+"/[a-fA-F0-9]{40}\\.(jpg|png)$");return match.Success?"https://shared.fastly.steamstatic.com"+uri.AbsolutePath.Replace("/steamcommunity/public/images/apps/","/community_assets/images/apps/",StringComparison.Ordinal):null;}
        return provider=="retroachievements"&&uri.Host=="media.retroachievements.org"&&Regex.IsMatch(uri.AbsolutePath,"^/Badge/[0-9A-Za-z_-]{1,80}\\.png$")?"https://media.retroachievements.org"+uri.AbsolutePath:null;
    }
    public async Task<(string Path,string Type)?> Get(string provider,string game,string icon,CancellationToken token)
    {
        var url=Canonical(provider,game,icon);if(url is null)return null;
        var key=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))).ToLowerInvariant();var root=Path.Combine(options.DataRoot,"achievement-icons");Directory.CreateDirectory(root);var path=Path.Combine(root,key+".img");
        if(File.Exists(path))return(path,Content(await File.ReadAllBytesAsync(path,token))??"image/png");
        if(failures.TryGetValue(key,out var retry)&&retry>DateTime.UtcNow)return null;
        await Downloads.WaitAsync(token);
        try
        {
            if(File.Exists(path))return(path,Content(await File.ReadAllBytesAsync(path,token))??"image/png");
            using var response=await Http.GetAsync(url,HttpCompletionOption.ResponseHeadersRead,token);response.EnsureSuccessStatusCode();
            if(response.Content.Headers.ContentLength>786432)return null;
            using var stream=await response.Content.ReadAsStreamAsync(token);using var memory=new MemoryStream();var buffer=new byte[16384];int read;
            while((read=await stream.ReadAsync(buffer,token))>0){if(memory.Length+read>786432)return null;memory.Write(buffer,0,read);}
            var bytes=memory.ToArray();var type=Content(bytes);if(type is null)return null;
            if(Directory.EnumerateFiles(root,"*.img").Sum(f=>new FileInfo(f).Length)>128L*1024*1024)return null;
            var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";await File.WriteAllBytesAsync(temp,bytes,token);if(!OperatingSystem.IsWindows())File.SetUnixFileMode(temp,UnixFileMode.UserRead|UnixFileMode.UserWrite);File.Move(temp,path,true);return(path,type);
        }
        catch(Exception error)when(error is HttpRequestException or IOException or TaskCanceledException){failures[key]=DateTime.UtcNow.AddMinutes(5);return null;}
        finally{Downloads.Release();}
    }
    static string? Content(byte[] bytes)=>bytes.Length<12?null:bytes[0]==255&&bytes[1]==216?"image/jpeg":bytes.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10})?"image/png":null;
}
