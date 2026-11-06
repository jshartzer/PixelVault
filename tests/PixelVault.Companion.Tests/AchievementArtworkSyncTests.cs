using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PixelVaultNative;
using Xunit;

namespace PixelVault.Companion.Tests;

public sealed partial class CompanionTests
{
    const string ArtUrl="https://shared.fastly.steamstatic.com/community_assets/images/apps/1850570/2cd0c3046f89413d00e733f015841acd7773fa3f.jpg";
    static AchievementGameSync ArtGame(string workspace)=>AchievementFixture(workspace) with{GameId="1850570",Rows=[AchievementFixture(workspace).Rows[0] with{Id="062",Title="Homo Faber",Icon=ArtUrl,Unlocked=true}]};
    static AchievementArtworkTransfer ArtTransfer(string workspace)
    {
        var image=AchievementArtwork.Find(ArtUrl)!;
        return new(workspace,new("steam","1850570","062",ArtUrl,image.Sha256),image.Bytes);
    }
    [Fact]
    public async Task PairedArtworkSyncIsVerifiedIdempotentScopedAndImmediatelyServedToTheOwner()
    {
        using var fixture=new WorkspaceCase();SourceForTest(fixture);using var host=new Host(false,root:fixture.Root);using var owner=host.Client();await Setup(host,owner);var pair=PairForTest(fixture,host);
        host.Services.GetRequiredService<ConnectorSyncState>().EnableLibraryAndAutomatic();using var pc=host.Client();pc.DefaultRequestHeaders.Authorization=new("Bearer",pair.Token);
        await pc.PostAsJsonAsync("/connector/v1/achievements/snapshot",ArtGame(pair.WorkspaceId));var transfer=ArtTransfer(pair.WorkspaceId);
        var repository=fixture.Repo();var history=JsonSerializer.Serialize(repository.History(TrackerId,Approved));var states=JsonSerializer.Serialize(repository.Load(TrackerId,Approved));
        using var anonymous=host.Client();Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.PostAsJsonAsync("/connector/v1/achievement-artwork",transfer)).StatusCode);
        var response=await pc.PostAsJsonAsync("/connector/v1/achievement-artwork",transfer);Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.True((await response.Content.ReadFromJsonAsync<AchievementArtworkReceipt>())!.Updated);
        Assert.False((await (await pc.PostAsJsonAsync("/connector/v1/achievement-artwork",transfer)).Content.ReadFromJsonAsync<AchievementArtworkReceipt>())!.Updated);
        var inventory=await pc.GetFromJsonAsync<Dictionary<string,string>>("/connector/v1/achievement-artwork");Assert.Equal(transfer.Artwork.Sha256,inventory![ArtUrl]);
        var icon=await owner.GetAsync("/api/v1/achievements/steam/1850570/062/icon");Assert.Equal("image/png",icon.Content.Headers.ContentType!.MediaType);Assert.Equal(transfer.Bytes,await icon.Content.ReadAsByteArrayAsync());Assert.True(icon.Headers.CacheControl!.Private);Assert.Equal(TimeSpan.FromHours(1),icon.Headers.CacheControl.MaxAge);Assert.False(icon.Headers.CacheControl.NoStore);Assert.Empty(icon.Headers.Pragma);
        Assert.Equal(history,JsonSerializer.Serialize(repository.History(TrackerId,Approved)));Assert.Equal(states,JsonSerializer.Serialize(repository.Load(TrackerId,Approved)));
        var manifest=await File.ReadAllBytesAsync(Path.Combine(fixture.Root,"achievement-artwork","manifest.json"));
        foreach(var bad in new[]{transfer with{WorkspaceId="other"},transfer with{Bytes=new byte[]{1,2,3}},transfer with{Artwork=transfer.Artwork with{OriginalUrl=ArtUrl.Replace("1850570","42")}},transfer with{Artwork=transfer.Artwork with{Sha256="../../bad"}}})
            Assert.Equal(HttpStatusCode.BadRequest,(await pc.PostAsJsonAsync("/connector/v1/achievement-artwork",bad)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await pc.PostAsJsonAsync("/connector/v1/achievement-artwork",transfer with{Artwork=transfer.Artwork with{AchievementId="unapproved"}})).StatusCode);
        Assert.Equal(manifest,await File.ReadAllBytesAsync(Path.Combine(fixture.Root,"achievement-artwork","manifest.json")));
        host.Services.GetRequiredService<ConnectorSyncState>().SetAutomatic(false);Assert.Equal(HttpStatusCode.Conflict,(await pc.PostAsJsonAsync("/connector/v1/achievement-artwork",transfer)).StatusCode);
    }
    [Fact]
    public async Task ArtworkInventoryDetectsMissingOrCorruptAssetsSoThePcCanRepairThem()
    {
        using var fixture=new WorkspaceCase();SourceForTest(fixture);using var host=new Host(false,root:fixture.Root);var pair=PairForTest(fixture,host);host.Services.GetRequiredService<ConnectorSyncState>().EnableLibraryAndAutomatic();using var pc=host.Client();pc.DefaultRequestHeaders.Authorization=new("Bearer",pair.Token);
        await pc.PostAsJsonAsync("/connector/v1/achievements/snapshot",ArtGame(pair.WorkspaceId));var transfer=ArtTransfer(pair.WorkspaceId);await pc.PostAsJsonAsync("/connector/v1/achievement-artwork",transfer);
        var file=Path.Combine(fixture.Root,"achievement-artwork",transfer.Artwork.File);await File.WriteAllBytesAsync(file,new byte[]{1});
        Assert.Empty((await pc.GetFromJsonAsync<Dictionary<string,string>>("/connector/v1/achievement-artwork"))!);
        Assert.Equal(HttpStatusCode.OK,(await pc.PostAsJsonAsync("/connector/v1/achievement-artwork",transfer)).StatusCode);Assert.Equal(transfer.Bytes,await File.ReadAllBytesAsync(file));
    }
}
