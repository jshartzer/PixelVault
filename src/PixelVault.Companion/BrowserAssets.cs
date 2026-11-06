using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Extensions.FileProviders;

namespace PixelVault.Companion;

internal sealed class BrowserAssets(IFileProvider files)
{
    readonly ConcurrentDictionary<string,(DateTimeOffset Modified,long Length,string Hash)> hashes=new();
    public string Version(string path)
    {
        var file=files.GetFileInfo(path);if(!file.Exists)throw new FileNotFoundException();
        if(hashes.TryGetValue(path,out var saved)&&saved.Modified==file.LastModified&&saved.Length==file.Length)return saved.Hash;
        using var stream=file.CreateReadStream();var hash=Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant()[..16];
        hashes[path]=(file.LastModified,file.Length,hash);return hash;
    }
    public string Html()
    {
        using var stream=files.GetFileInfo("index.html").CreateReadStream();using var reader=new StreamReader(stream);
        return Regex.Replace(reader.ReadToEnd(),"(?<prefix>(?:src|href|content)=\")(?<path>/[A-Za-z0-9/_.-]+\\.(?:js|css))(?:\\?[^\"]*)?\"",m=>m.Groups["prefix"].Value+m.Groups["path"].Value+"?v="+Version(m.Groups["path"].Value)+"\"");
    }
}
