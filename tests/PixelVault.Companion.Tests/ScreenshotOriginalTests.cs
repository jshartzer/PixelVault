using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace PixelVault.Companion.Tests;
public sealed partial class CompanionTests
{
    [Fact]
    public async Task ScreenshotOriginalsStreamWithoutChangesKeepNamesSupportRangesAndRemainPrivateAndPcIndependent()
    {
        using var fixture=new WorkspaceCase();SourceForTest(fixture);LibraryFixture(fixture.Root);string cookie;string id;
        var bytes=new byte[8*1024*1024];RandomNumberGenerator.Fill(bytes);System.Text.Encoding.UTF8.GetBytes("Original embedded metadata remains byte-identical.").CopyTo(bytes,16);
        var original=new ScreenshotOriginal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()+".png","Capture 日本.png",bytes.LongLength,"image/png",DateTime.UtcNow.AddDays(-1).Ticks);
        using(var host=new Host(false,root:fixture.Root,screenshotPilot:"steam:42"))using(var owner=host.Client())using(var pc=host.Client())
        {
            cookie=await Setup(host,owner);var pair=PairForTest(fixture,host);pc.DefaultRequestHeaders.Authorization=new("Bearer",pair.Token);host.Services.GetRequiredService<ConnectorSyncState>().EnableLibraryAndAutomatic();
            var media=host.Services.GetRequiredService<ScreenshotLibrary>();var baseline=PilotCatalogue(host,1);id=baseline.Photos[0].Id;var first=media.Prepare(baseline);media.Upload(first.Id,PilotAsset,PilotJpeg);media.Commit(first.Id);
            var next=baseline with{Photos=[baseline.Photos[0] with{Original=original}]};var plan=media.Prepare(next);Assert.Equal([original.Asset],plan.MissingOriginals);
            Assert.Throws<InvalidOperationException>(()=>media.Commit(plan.Id));Assert.Null(media.Photo("steam","42",id).Original);
            var endpoint=$"/connector/v1/screenshots/{plan.Id}/originals/{original.Asset}";
            Assert.Equal(HttpStatusCode.BadRequest,(await pc.PostAsync(endpoint,new ByteArrayContent([1,2,3]))).StatusCode);
            Assert.Empty(Directory.GetFiles(Path.Combine(fixture.Root,"screenshots",baseline.WorkspaceId.ToLowerInvariant(),"originals"),"*.upload"));
            Assert.Equal(HttpStatusCode.OK,(await pc.PostAsync(endpoint,new ByteArrayContent(bytes))).StatusCode);media.Commit(plan.Id);
            Assert.True(media.Prepare(next).Current);Assert.Throws<InvalidDataException>(()=>media.Prepare(baseline));
            var url=$"/api/v1/screenshots/steam/42/{id}/original";
            using var unauth=host.Client();Assert.Equal(HttpStatusCode.Unauthorized,(await unauth.GetAsync(url)).StatusCode);
            using var full=await owner.GetAsync(url);Assert.Equal(HttpStatusCode.OK,full.StatusCode);Assert.Equal(bytes,await full.Content.ReadAsByteArrayAsync());Assert.Equal("attachment",full.Content.Headers.ContentDisposition!.DispositionType);Assert.Equal(original.FileName,full.Content.Headers.ContentDisposition.FileNameStar);Assert.Equal("image/png",full.Content.Headers.ContentType!.MediaType);Assert.NotNull(full.Headers.ETag);Assert.NotNull(full.Content.Headers.LastModified);
            using var request=new HttpRequestMessage(HttpMethod.Get,url);request.Headers.Range=new(0,7);using var range=await owner.SendAsync(request);Assert.Equal(HttpStatusCode.PartialContent,range.StatusCode);Assert.Equal(bytes.Take(8),await range.Content.ReadAsByteArrayAsync());
            Assert.Equal(HttpStatusCode.Conflict,(await owner.GetAsync(url+"?asset=outdated")).StatusCode);
            Assert.Throws<InvalidDataException>(()=>media.Prepare(next with{Photos=[next.Photos[0] with{Original=original with{FileName="../outside.png"}}]}));
            host.Services.GetRequiredService<ConnectorSyncState>().SetAutomatic(false);Assert.Equal(HttpStatusCode.Conflict,(await pc.PostAsync(endpoint,new ByteArrayContent(bytes))).StatusCode);
            host.Services.GetRequiredService<PcConnector>().Revoke();Assert.Equal(bytes,await owner.GetByteArrayAsync(url));
            Assert.Equal(HttpStatusCode.Unauthorized,(await pc.GetAsync($"/connector/v1/screenshots/steam/42/{id}/original")).StatusCode);
        }
        using var restart=new Host(false,root:fixture.Root,screenshotPilot:"steam:42");using var reader=restart.Client();reader.DefaultRequestHeaders.Add("Cookie",cookie);Assert.Equal(bytes,await reader.GetByteArrayAsync($"/api/v1/screenshots/steam/42/{id}/original"));
    }
}
