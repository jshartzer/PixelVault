using System.Net;
using Xunit;

namespace PixelVault.Companion.Tests;

public sealed class AchievementIconCacheTests
{
    sealed class Images:HttpMessageHandler
    {
        internal int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {Interlocked.Increment(ref Requests);var bytes=new byte[40];new byte[]{137,80,78,71,13,10,26,10}.CopyTo(bytes,0);return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(bytes)});}
    }
    [Theory][InlineData(90)][InlineData(110)]
    public async Task FullCacheEvictsOldDownloadsAndStillReturnsNewArt(int existingBytes)
    {
        var root=Path.Combine(Path.GetTempPath(),"pv-icon-cache-"+Guid.NewGuid().ToString("N"));
        try
        {
            var cache=Path.Combine(root,"achievement-icons");Directory.CreateDirectory(cache);
            var old=Path.Combine(cache,new string('a',64)+".img");File.WriteAllBytes(old,new byte[existingBytes]);
            var imported=Path.Combine(cache,"art-imported.png");File.WriteAllText(imported,"preserved");
            var unrelated=Path.Combine(cache,"unrelated.img");File.WriteAllText(unrelated,"preserved");
            using var handler=new Images();using var http=new HttpClient(handler);
            var icons=new AchievementIcons(new CompanionOptions{DataRoot=root},http,100);
            const string url="https://shared.fastly.steamstatic.com/community_assets/images/apps/42/1111111111111111111111111111111111111111.jpg";
            var result=await icons.Get("steam","42",url,default);
            Assert.NotNull(result);Assert.Equal(40,new FileInfo(result.Value.Path).Length);Assert.False(File.Exists(old));
            Assert.Equal("preserved",File.ReadAllText(imported));Assert.Equal("preserved",File.ReadAllText(unrelated));
            Assert.Equal(result,await icons.Get("steam","42",url,default));Assert.Equal(1,handler.Requests);
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    [Fact]
    public async Task ParallelDownloadsCannotOverfillCache()
    {
        var root=Path.Combine(Path.GetTempPath(),"pv-icon-cache-"+Guid.NewGuid().ToString("N"));
        try
        {
            using var handler=new Images();using var http=new HttpClient(handler);var icons=new AchievementIcons(new CompanionOptions{DataRoot=root},http,100);
            var results=await Task.WhenAll(Enumerable.Range(1,8).Select(i=>icons.Get("steam","42","https://shared.fastly.steamstatic.com/community_assets/images/apps/42/"+i.ToString("d40")+".jpg",default)));
            Assert.All(results,r=>Assert.NotNull(r));Assert.InRange(Directory.GetFiles(Path.Combine(root,"achievement-icons"),"*.img").Sum(p=>new FileInfo(p).Length),1,100);
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }
}
