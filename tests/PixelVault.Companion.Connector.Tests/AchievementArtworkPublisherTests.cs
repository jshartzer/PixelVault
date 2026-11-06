using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PixelVault.Companion;
using PixelVault.Companion.Connector;
using PixelVaultNative;
using Xunit;

public sealed class AchievementArtworkPublisherTests:IDisposable
{
    readonly string root=Path.Combine(Path.GetTempPath(),"PV-ArtPublisher-"+Guid.NewGuid().ToString("N"));
    const string Url="https://shared.fastly.steamstatic.com/community_assets/images/apps/1850570/2cd0c3046f89413d00e733f015841acd7773fa3f.jpg";
    static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    static AchievementGameSync Game()=>new("workspace","steam","1850570","approved",1,1,[new("062","Homo Faber","",0,false,true,false,0,null,null,null,Url,"","","","",false,new())],[],[],[]);
    void Local()
    {
        var image=AchievementArtwork.Find(Url)!;var folder=Path.Combine(root,"achievement-artwork");Directory.CreateDirectory(folder);File.WriteAllBytes(Path.Combine(folder,image.Sha256+".png"),image.Bytes);
        File.WriteAllText(Path.Combine(folder,"manifest.json"),JsonSerializer.Serialize(new[]{new AchievementArtworkAsset("steam","1850570","062",Url,image.Sha256)},Json));
    }
    sealed class Server:HttpMessageHandler
    {
        public readonly Dictionary<string,string> Inventory=new();public int Uploads;public bool LoseNextReceipt;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            if(request.Method==HttpMethod.Get)return new(HttpStatusCode.OK){Content=JsonContent.Create(Inventory)};
            var transfer=(await request.Content!.ReadFromJsonAsync<AchievementArtworkTransfer>(Json,token))!;Assert.Equal("workspace",transfer.WorkspaceId);Assert.NotNull(AchievementArtwork.Verify(transfer.Bytes,transfer.Artwork.Sha256));
            Uploads++;Inventory[transfer.Artwork.OriginalUrl]=transfer.Artwork.Sha256;
            if(LoseNextReceipt){LoseNextReceipt=false;return new(HttpStatusCode.ServiceUnavailable);}
            return new(HttpStatusCode.OK){Content=JsonContent.Create(new AchievementArtworkReceipt(transfer.Artwork.Sha256,true))};
        }
    }
    [Fact]
    public async Task OnlyApprovedArtworkUploadsAndRestartOrLostResponseDoesNotDuplicateIt()
    {
        Local();using var server=new Server{LoseNextReceipt=true};using var http=new HttpClient(server){BaseAddress=new("http://test.invalid/")};
        Assert.Equal(0,await AchievementArtworkPublisher.Sync(http,root,"workspace",[],default));Assert.Equal(0,server.Uploads);
        await Assert.ThrowsAsync<HttpRequestException>(()=>AchievementArtworkPublisher.Sync(http,root,"workspace",[Game()],default));
        Assert.Equal(0,await AchievementArtworkPublisher.Sync(http,root,"workspace",[Game()],default));Assert.Equal(1,server.Uploads);
        server.Inventory.Clear();Assert.Equal(1,await AchievementArtworkPublisher.Sync(http,root,"workspace",[Game()],default));Assert.Equal(2,server.Uploads);
    }
    [Fact]
    public async Task BadLocalHashesCannotReplaceTheWebArtwork()
    {
        Local();using var server=new Server();using var http=new HttpClient(server){BaseAddress=new("http://test.invalid/")};
        var manifest=Path.Combine(root,"achievement-artwork","manifest.json");var bad=new AchievementArtworkAsset("steam","1850570","062",Url,new string('a',64));File.WriteAllText(manifest,JsonSerializer.Serialize(new[]{bad},Json));
        await Assert.ThrowsAsync<InvalidDataException>(()=>AchievementArtworkPublisher.Sync(http,root,"workspace",[Game()],default));Assert.Equal(0,server.Uploads);
    }
    public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}
}
