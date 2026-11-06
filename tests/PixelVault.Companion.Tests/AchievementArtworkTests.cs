using System.Buffers.Binary;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PixelVaultNative;
using Xunit;

namespace PixelVault.Companion.Tests;

public sealed class AchievementArtworkTests
{
    const string Homo="https://shared.fastly.steamstatic.com/community_assets/images/apps/1850570/2cd0c3046f89413d00e733f015841acd7773fa3f.jpg";
    const string Fount="https://shared.fastly.steamstatic.com/community_assets/images/apps/1850570/07aa917cc21190deaa777e630c6e5d8337730db6.jpg";
    [Fact]
    public void AllBundledReplacementsHaveVerifiedHashesAnd240PixelPngs()
    {
        using var stream=typeof(AchievementArtwork).Assembly.GetManifestResourceStream("PixelVault.Artwork.manifest.json");
        var entries=JsonSerializer.Deserialize<JsonElement[]>(stream!);Assert.Equal(6,entries!.Length);
        foreach(var entry in entries)
        {
            var image=AchievementArtwork.Find(entry.GetProperty("originalUrl").GetString()!);Assert.NotNull(image);
            Assert.Equal(entry.GetProperty("sha256").GetString(),image.Sha256);
            Assert.Equal(240u,BinaryPrimitives.ReadUInt32BigEndian(image.Bytes.AsSpan(16,4)));
            Assert.Equal(240u,BinaryPrimitives.ReadUInt32BigEndian(image.Bytes.AsSpan(20,4)));
        }
        Assert.Null(AchievementArtwork.Find(Homo.Replace("1850570","42")));
    }
    [Fact]
    public async Task ImportedArtOverridesBuiltinWhileInvalidHashOrPathFallsBackAndGameScopeIsEnforced()
    {
        var root=Path.Combine(Path.GetTempPath(),"PV-Art-"+Guid.NewGuid().ToString("N"));
        try
        {
            var imported=AchievementArtwork.Find(Fount)!;var artRoot=Path.Combine(root,"achievement-artwork");Directory.CreateDirectory(artRoot);
            var file=imported.Sha256+".png";File.WriteAllBytes(Path.Combine(artRoot,file),imported.Bytes);
            void Manifest(string name)=>File.WriteAllText(Path.Combine(artRoot,"manifest.json"),JsonSerializer.Serialize(new[]{new{originalUrl=Homo,file=name,sha256=imported.Sha256}}));
            Manifest(file);Assert.Equal(imported.Sha256,AchievementArtwork.Find(Homo,root)!.Sha256);
            var icons=new AchievementIcons(new CompanionOptions{DataRoot=root});var response=await icons.Get("steam","1850570",Homo,default);
            Assert.NotNull(response);Assert.Equal("image/png",response.Value.Type);Assert.Equal(imported.Bytes,File.ReadAllBytes(response.Value.Path));
            Assert.Null(await icons.Get("steam","42",Homo,default));
            Manifest("../"+file);Assert.NotEqual(imported.Sha256,AchievementArtwork.Find(Homo,root)!.Sha256);
            Manifest(file);File.WriteAllBytes(Path.Combine(artRoot,file),new byte[]{1,2,3});Assert.NotEqual(imported.Sha256,AchievementArtwork.Find(Homo,root)!.Sha256);
            File.WriteAllText(Path.Combine(artRoot,"manifest.json"),"invalid json");Assert.NotNull(AchievementArtwork.Find(Homo,root));
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }
}

public sealed partial class CompanionTests
{
    [Fact]
    public async Task IconEndpointUsesProvidedLockedArtUntilEarnedEvenWithImportedUnlockedArt()
    {
        using var fixture=new WorkspaceCase();SourceForTest(fixture);using var host=new Host(false,root:fixture.Root);
        using var owner=host.Client();await Setup(host,owner);var pair=PairForTest(fixture,host);
        host.Services.GetRequiredService<ConnectorSyncState>().EnableLibraryAndAutomatic();
        using var pc=host.Client();pc.DefaultRequestHeaders.Authorization=new("Bearer",pair.Token);
        const string original="https://shared.fastly.steamstatic.com/community_assets/images/apps/42/2cd0c3046f89413d00e733f015841acd7773fa3f.jpg";
        var locked=original.Replace("2cd0c3046f89413d00e733f015841acd7773fa3f","07aa917cc21190deaa777e630c6e5d8337730db6");
        var art=AchievementArtwork.Find(original.Replace("/42/","/1850570/"))!;
        var lockedArt=AchievementArtwork.Find(locked.Replace("/42/","/1850570/"))!;
        var options=host.Services.GetRequiredService<CompanionOptions>();var folder=Path.Combine(options.DataRoot,"achievement-artwork");Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder,art.Sha256+".png"),art.Bytes);
        File.WriteAllText(Path.Combine(folder,"manifest.json"),JsonSerializer.Serialize(new[]{new{originalUrl=original,file=art.Sha256+".png",sha256=art.Sha256}}));
        var cache=Path.Combine(options.DataRoot,"achievement-icons");Directory.CreateDirectory(cache);
        foreach(var (url,bytes) in new[]{(original,lockedArt.Bytes),(locked,lockedArt.Bytes)})
            File.WriteAllBytes(Path.Combine(cache,Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))).ToLowerInvariant()+".img"),bytes);
        foreach(var (known,earned) in new[]{(true,false),(true,true),(false,false),(true,false)})
        {
            var snapshot=AchievementFixture(pair.WorkspaceId);
            snapshot=snapshot with{Rows=snapshot.Rows.Select(r=>r with{Icon=original,LockedIcon=locked,ProgressKnown=known,Unlocked=earned}).ToArray()};
            (await pc.PostAsJsonAsync("/connector/v1/achievements/snapshot",snapshot)).EnsureSuccessStatusCode();
            var detail=await owner.GetFromJsonAsync<JsonElement>("/api/v1/achievements/steam/42/ONE");
            Assert.Equal(locked,detail.GetProperty("achievement").GetProperty("lockedIcon").GetString());
            var response=await owner.GetAsync("/api/v1/achievements/steam/42/ONE/icon");response.EnsureSuccessStatusCode();
            Assert.Equal(known&&earned?art.Bytes:lockedArt.Bytes,await response.Content.ReadAsByteArrayAsync());
            Assert.True(response.Headers.CacheControl!.Private);Assert.Equal(TimeSpan.FromHours(1),response.Headers.CacheControl.MaxAge);Assert.False(response.Headers.CacheControl.NoStore);Assert.Empty(response.Headers.Pragma);
            using var conditional=new HttpRequestMessage(System.Net.Http.HttpMethod.Get,"/api/v1/achievements/steam/42/ONE/icon");
            conditional.Headers.IfNoneMatch.Add(response.Headers.ETag!);
            using var unchanged=await owner.SendAsync(conditional);Assert.Equal(System.Net.HttpStatusCode.NotModified,unchanged.StatusCode);Assert.Empty(await unchanged.Content.ReadAsByteArrayAsync());
            var trackerImage=await owner.GetAsync("/api/v1/achievements/steam/42/ONE/unlocked-icon");trackerImage.EnsureSuccessStatusCode();
            Assert.Equal(art.Bytes,await trackerImage.Content.ReadAsByteArrayAsync());
            Assert.True(trackerImage.Headers.CacheControl!.Private);Assert.Equal(TimeSpan.FromHours(1),trackerImage.Headers.CacheControl.MaxAge);Assert.False(trackerImage.Headers.CacheControl.NoStore);Assert.Empty(trackerImage.Headers.Pragma);
        }
        // Older snapshots without a locked URL still use the provider original until earned.
        var legacy=AchievementFixture(pair.WorkspaceId);
        legacy=legacy with{Rows=legacy.Rows.Select(r=>r with{Icon=original}).ToArray()};
        (await pc.PostAsJsonAsync("/connector/v1/achievements/snapshot",legacy)).EnsureSuccessStatusCode();
        Assert.Equal(lockedArt.Bytes,await owner.GetByteArrayAsync("/api/v1/achievements/steam/42/ONE/icon"));
        Assert.Equal(art.Bytes,await owner.GetByteArrayAsync("/api/v1/achievements/steam/42/ONE/unlocked-icon"));
        using var anonymous=host.Client();
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized,(await anonymous.GetAsync("/api/v1/achievements/steam/42/ONE/unlocked-icon")).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.NotFound,(await owner.GetAsync("/api/v1/achievements/steam/42/missing/unlocked-icon")).StatusCode);
    }

    [Fact]
    public async Task HealthAndSessionReportThePackagedVersion()
    {
        using var host=new Host(false);using var client=host.Client();
        var expected=typeof(AchievementIcons).Assembly.GetName().Version!.ToString(3);
        foreach(var path in new[]{"/health","/api/v1/session"})
        {
            using var json=JsonDocument.Parse(await client.GetStringAsync(path));
            Assert.Equal(expected,json.RootElement.GetProperty("version").GetString());
        }
    }
}
