using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace PixelVault.Companion.Tests;

public sealed partial class CompanionTests
{
    // Real rectangular PNG, including partial edge tiles and distinct quadrants.
    static byte[] MapPng(int width=1025,int height=513)
    {
        using var output=new MemoryStream();output.Write(new byte[]{137,80,78,71,13,10,26,10});
        void Chunk(string name,byte[] bytes)
        {
            Span<byte> length=stackalloc byte[4];BinaryPrimitives.WriteInt32BigEndian(length,bytes.Length);output.Write(length);
            var type=Encoding.ASCII.GetBytes(name);output.Write(type);output.Write(bytes);uint crc=0xffffffff;
            foreach(var b in type.Concat(bytes)){crc^=b;for(var i=0;i<8;i++)crc=(crc>>1)^((crc&1)!=0?0xedb88320:0);}
            BinaryPrimitives.WriteUInt32BigEndian(length,~crc);output.Write(length);
        }
        var header=new byte[13];BinaryPrimitives.WriteInt32BigEndian(header,width);BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4),height);header[8]=8;header[9]=2;Chunk("IHDR",header);
        using var pixels=new MemoryStream();using(var zip=new ZLibStream(pixels,CompressionLevel.Fastest,true))
        for(var y=0;y<height;y++)
        {
            zip.WriteByte(0);
            for(var x=0;x<width;x++){zip.WriteByte((byte)(x<width/2?255:0));zip.WriteByte((byte)(x>=width/2?255:0));zip.WriteByte((byte)(y>=height/2?255:0));}
        }
        Chunk("IDAT",pixels.ToArray());Chunk("IEND",[]);return output.ToArray();
    }
    static string InstallMapImage(DirectCase f,byte[]? bytes=null)
    {
        bytes??=MapPng();var image=Convert.ToHexString(SHA256.HashData(bytes))+".png";
        var root=Path.Combine(f.App,"game-maps","images");Directory.CreateDirectory(root);File.WriteAllBytes(Path.Combine(root,image),bytes);
        var map=SavedMap();map.ImageFile=image;map.PixelWidth=1025;map.PixelHeight=513;WriteMap(f.App,map);return Path.Combine(root,image);
    }
    static Host TileHost(DirectCase f)=>new(false,root:f.State,applicationRoot:f.App,cacheRoot:f.Cache,photoWorker:PhotoCase.Worker);
    const string TileBase="/api/v1/maps/steam/42/region-one";
    static string TileUrl(MapTileManifest manifest,int z=0,int x=0,int y=0)=>manifest.TileUrl.Replace("{z}",z.ToString()).Replace("{x}",x.ToString()).Replace("{y}",y.ToString());

    [Fact]
    public async Task MapTileManifestAndTilesArePrivateVersionedCachedAndScoped()
    {
        using var f=new DirectCase();var image=InstallMapImage(f);var original=File.ReadAllBytes(image);
        using var host=TileHost(f);using var client=host.Client();
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync(TileBase+"/manifest")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync(TileBase+"/tiles/0/0/0?v=x")).StatusCode);
        await Setup(host,client);
        var manifest=(await client.GetFromJsonAsync<MapTileManifest>(TileBase+"/manifest"))!;
        Assert.Equal(2,manifest.MaxZoom);Assert.Equal(512,manifest.TileSize);Assert.Equal(1025,manifest.Width);Assert.Equal(513,manifest.Height);
        Assert.DoesNotContain(f.App,System.Text.Json.JsonSerializer.Serialize(manifest));
        var response=await client.GetAsync(TileUrl(manifest));Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        Assert.True(response.Headers.CacheControl!.Private);Assert.False(response.Headers.CacheControl.NoStore);Assert.Empty(response.Headers.Pragma);
        Assert.Equal("image/jpeg",response.Content.Headers.ContentType!.MediaType);Assert.NotNull(response.Headers.ETag);
        var jpeg=await response.Content.ReadAsByteArrayAsync();Assert.Equal(255,jpeg[0]);Assert.Equal(216,jpeg[1]);
        var edge=await client.GetByteArrayAsync(TileUrl(manifest,2,2,1));
        // Baseline JPEG's SOF0 contains height then width: partial edge images stay 512 square.
        var sof=Enumerable.Range(0,edge.Length-9).First(i=>edge[i]==255&&edge[i+1]==192);
        Assert.Equal(512,BinaryPrimitives.ReadUInt16BigEndian(edge.AsSpan(sof+5)));
        Assert.Equal(512,BinaryPrimitives.ReadUInt16BigEndian(edge.AsSpan(sof+7)));
        client.DefaultRequestHeaders.IfNoneMatch.Add(response.Headers.ETag!);
        Assert.Equal(HttpStatusCode.NotModified,(await client.GetAsync(TileUrl(manifest))).StatusCode);client.DefaultRequestHeaders.IfNoneMatch.Clear();
        host.Services.GetRequiredService<CompanionOptions>().PhotoWorkerPath="";
        Assert.Equal(manifest,await client.GetFromJsonAsync<MapTileManifest>(TileBase+"/manifest"));
        Assert.Equal(HttpStatusCode.OK,(await client.GetAsync(TileUrl(manifest,2,2,1))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync(TileUrl(manifest,2,3,0))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync(TileUrl(manifest,-1))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync(TileUrl(manifest).Replace(manifest.Version,"wrong"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync(TileUrl(manifest).Replace("steam/42","steam/99"))).StatusCode);
        Assert.Equal(original,File.ReadAllBytes(image));
    }

    [Fact]
    public async Task ConcurrentMapRequestsPublishOnePyramidAndCorruptTilesRecoverOnRetry()
    {
        using var f=new DirectCase();InstallMapImage(f);using var host=TileHost(f);using var client=host.Client();await Setup(host,client);
        var results=await Task.WhenAll(Enumerable.Range(0,4).Select(_=>client.GetFromJsonAsync<MapTileManifest>(TileBase+"/manifest")));
        Assert.Single(results.Select(r=>r!.Version).Distinct());var manifest=results[0]!;
        var root=Path.Combine(f.Cache,"maps");Assert.Single(Directory.GetDirectories(root));
        var pyramid=Assert.Single(Directory.GetDirectories(root));Assert.Equal(9,Directory.GetFiles(pyramid,"*.jpg").Length);
        File.WriteAllText(Path.Combine(pyramid,"0-0-0.jpg"),"broken");
        Assert.Equal(HttpStatusCode.ServiceUnavailable,(await client.GetAsync(TileUrl(manifest))).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await client.GetAsync(TileUrl(manifest))).StatusCode);
        File.Delete(Path.Combine(pyramid,"2-2-1.jpg"));
        Assert.Equal(HttpStatusCode.OK,(await client.GetAsync(TileUrl(manifest,2,2,1))).StatusCode);
    }

    [Fact]
    public async Task MapSourceChangesMissingImagesAndQuotaNeverServeAnOldPyramid()
    {
        using var f=new DirectCase();var image=InstallMapImage(f);using var host=TileHost(f);using var client=host.Client();await Setup(host,client);
        var manifest=(await client.GetFromJsonAsync<MapTileManifest>(TileBase+"/manifest"))!;
        var options=host.Services.GetRequiredService<CompanionOptions>();
        options.MapCacheBytes=Directory.GetFiles(Path.Combine(f.Cache,"maps",manifest.Version)).Sum(p=>new FileInfo(p).Length)+1024;
        File.SetLastWriteTimeUtc(image,DateTime.UtcNow.AddMinutes(2));
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync(TileUrl(manifest))).StatusCode);
        var newer=(await client.GetFromJsonAsync<MapTileManifest>(TileBase+"/manifest"))!;Assert.NotEqual(manifest.Version,newer.Version);
        Assert.Single(Directory.GetDirectories(Path.Combine(f.Cache,"maps")),d=>Path.GetFileName(d)==newer.Version);
        File.Move(image,image+".offline");
        Assert.Equal(HttpStatusCode.ServiceUnavailable,(await client.GetAsync(TileUrl(newer))).StatusCode);File.Move(image+".offline",image);
        Assert.Single(Directory.GetDirectories(Path.Combine(f.Cache,"maps")));
        options.MapCacheBytes=1024;
        File.SetLastWriteTimeUtc(image,DateTime.UtcNow.AddMinutes(3));
        Assert.Equal(HttpStatusCode.ServiceUnavailable,(await client.GetAsync(TileBase+"/manifest")).StatusCode);
        Assert.Empty(Directory.GetDirectories(Path.Combine(f.Cache,"maps"),"work-*"));
        options.MapCacheBytes=512L*1024*1024;
        Assert.Equal(HttpStatusCode.OK,(await client.GetAsync(TileBase+"/manifest")).StatusCode);
    }

    [Theory]
    [InlineData("corrupt")]
    [InlineData("wrong-size")]
    [InlineData("path")]
    public async Task InvalidMapImagesFailCleanlyWithoutPublishingTiles(string failure)
    {
        using var f=new DirectCase();var image=InstallMapImage(f);
        if(failure=="corrupt")File.WriteAllText(image,"broken");
        else if(failure=="wrong-size")File.WriteAllBytes(image,PhotoCase.Png);
        else{var map=SavedMap();map.ImageFile="../outside.png";WriteMap(f.App,map);}
        using var host=TileHost(f);using var client=host.Client();await Setup(host,client);
        var response=await client.GetAsync(TileBase+"/manifest");Assert.Equal(HttpStatusCode.ServiceUnavailable,response.StatusCode);
        Assert.DoesNotContain(f.App,await response.Content.ReadAsStringAsync());
        var cache=Path.Combine(f.Cache,"maps");if(Directory.Exists(cache))Assert.Empty(Directory.GetDirectories(cache));
    }

    [Fact]
    public async Task CancelledMapGenerationLeavesNoPublishedOrTemporaryPyramid()
    {
        using var f=new DirectCase();InstallMapImage(f);using var host=TileHost(f);using var client=host.Client();await Setup(host,client);
        using var cancelled=new CancellationTokenSource();cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>host.Services.GetRequiredService<MapTileCache>().Manifest("steam","42","region-one",cancelled.Token));
        Assert.False(Directory.Exists(Path.Combine(f.Cache,"maps")));
        Assert.Equal(HttpStatusCode.OK,(await client.GetAsync(TileBase+"/manifest")).StatusCode);
    }

    [Fact]
    public async Task CancellationDuringMapWorkerExecutionCleansStagingAndAllowsRetry()
    {
        using var f=new DirectCase();InstallMapImage(f);using var host=TileHost(f);using var client=host.Client();await Setup(host,client);
        using var cancellation=new CancellationTokenSource();
        var task=host.Services.GetRequiredService<MapTileCache>().Manifest("steam","42","region-one",cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>task);
        var root=Path.Combine(f.Cache,"maps");if(Directory.Exists(root))Assert.Empty(Directory.GetDirectories(root));
        Assert.Equal(HttpStatusCode.OK,(await client.GetAsync(TileBase+"/manifest")).StatusCode);
    }
}
