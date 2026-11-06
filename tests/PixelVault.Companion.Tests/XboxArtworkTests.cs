using System.Net;
using Xunit;

namespace PixelVault.Companion.Tests;

public sealed class XboxArtworkTests
{
    const string Url="https://images-eds-ssl.xboxlive.com/image?url=example&format=png";
    [Theory]
    [InlineData("https://image-ssl.xboxlive.com/global/t.58410a30/ach/0/1f",true)]
    [InlineData("https://image-ssl.xboxlive.com/global/t.58410a31/ach/0/1f",false)]
    [InlineData("https://image-ssl.xboxlive.com/global/t.58410a30/icon/0/1f",false)]
    [InlineData("https://image-ssl.xboxlive.com/global/t.58410a30/ach/0/1f?url=https://localhost",false)]
    [InlineData("https://image-ssl.xboxlive.com.evil.test/global/t.58410a30/ach/0/1f",false)]
    [InlineData("http://image-ssl.xboxlive.com/global/t.58410a30/ach/0/1f",false)]
    public void LegacyIconsAreBoundToTheOfficialHostAndRequestedGame(string url,bool accepted)
        =>Assert.Equal(accepted,AchievementIcons.Canonical("xbox","1480657456",url) is not null);
    [Fact]
    public void XboxArtworkKeepsTheOfficialImageQuery()
    {
        Assert.Equal(Url,AchievementIcons.Canonical("xbox","42",Url+"#ignored"));
        Assert.Null(AchievementIcons.Canonical("steam","42",Url));
    }
    [Theory]
    [InlineData("https://images-eds-ssl.xboxlive.com.evil.test/image?url=a")]
    [InlineData("https://images-eds-ssl.xboxlive.com@localhost/image?url=a")]
    [InlineData("https://images-eds-ssl.xboxlive.com:8443/image?url=a")]
    [InlineData("http://images-eds-ssl.xboxlive.com/image?url=a")]
    [InlineData("https://images-eds-ssl.xboxlive.com/other?url=a")]
    [InlineData("https://images-eds-ssl.xboxlive.com/image")]
    public void XboxArtworkRejectsOtherDestinations(string url)=>Assert.Null(AchievementIcons.Canonical("xbox","42",url));

    [Theory]
    [InlineData(3*1024*1024,true,true)]
    [InlineData(3*1024*1024,false,true)]
    [InlineData(8*1024*1024+1,true,false)]
    [InlineData(8*1024*1024+1,false,false)]
    public async Task XboxDownloadsAreBoundedWithOrWithoutContentLength(int size,bool knownLength,bool accepted)
    {
        var root=Path.Combine(Path.GetTempPath(),"pv-xbox-art-"+Guid.NewGuid().ToString("N"));
        try
        {
            var bytes=new byte[size];new byte[]{137,80,78,71,13,10,26,10}.CopyTo(bytes,0);
            using var handler=new ImageHandler(bytes,knownLength);using var client=new HttpClient(handler);
            var icons=new AchievementIcons(new CompanionOptions{DataRoot=root},client);
            var result=await icons.Get("xbox","42",Url,default);
            Assert.Equal(accepted,result.HasValue);
            if(accepted)
            {
                Assert.Equal("image/png",result!.Value.Type);
                Assert.Equal(bytes,await File.ReadAllBytesAsync(result.Value.Path));
                Assert.Equal(result,await icons.Get("xbox","42",Url,default));
                Assert.Equal(1,handler.Requests);
            }
            else Assert.Empty(Directory.EnumerateFiles(root,"*.img",SearchOption.AllDirectories));
        }
        finally {if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    sealed class ImageHandler(byte[] bytes,bool knownLength):HttpMessageHandler
    {
        public int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            Requests++;Assert.Equal(Url,request.RequestUri!.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new ImageContent(bytes,knownLength)});
        }
    }
    sealed class ImageContent(byte[] bytes,bool knownLength):HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream,TransportContext? context)=>stream.WriteAsync(bytes).AsTask();
        protected override bool TryComputeLength(out long length){length=bytes.Length;return knownLength;}
    }
}
