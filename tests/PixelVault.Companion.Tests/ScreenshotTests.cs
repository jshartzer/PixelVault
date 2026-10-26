using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace PixelVault.Companion.Tests;
public sealed partial class CompanionTests
{
    static readonly byte[] PilotJpeg=[255,216,255,224,0,0,0,0,0,0,0,255,217];
    static string PilotAsset=>Convert.ToHexString(SHA256.HashData(PilotJpeg)).ToLowerInvariant()+".jpg";
    static ScreenshotCatalogue PilotCatalogue(Host host,int count=2)
    {
        var workspace=host.Services.GetRequiredService<TrackerWorkspace>();
        return new(1,workspace.Identity,"steam","42","A library journey",new string('a',64),"Central Standard Time",DateTime.UtcNow.Ticks,count,1,
            Enumerable.Range(0,count).Select(i=>new ScreenshotPhoto(i.ToString("x64"),"G1","Steam",638900000000000000-i,"2025-08-19","Test group",1920,1080,PilotAsset,PilotAsset)).ToArray());
    }
    [Fact]
    public async Task ScreenshotPilotIsAuthenticatedScopedAtomicIdempotentAndReadableWithoutPcAfterRestart()
    {
        using var fixture=new WorkspaceCase();SourceForTest(fixture);LibraryFixture(fixture.Root);string cookie;
        using(var host=new Host(false,root:fixture.Root,screenshotPilot:"steam:42"))using(var owner=host.Client())using(var pc=host.Client())
        {
            Assert.Equal(HttpStatusCode.Unauthorized,(await owner.GetAsync("/api/v1/screenshots/steam/42")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized,(await pc.PostAsJsonAsync("/connector/v1/screenshots/prepare",new{})).StatusCode);
            cookie=await Setup(host,owner);var pair=PairForTest(fixture,host);pc.DefaultRequestHeaders.Authorization=new("Bearer",pair.Token);
            var state=host.Services.GetRequiredService<ConnectorSyncState>();state.EnableLibraryAndAutomatic();
            var catalog=PilotCatalogue(host,61);
            catalog=catalog with{Photos=catalog.Photos.Select((p,i)=>i==60?p with{Console="Xbox"}:p).ToArray()};
            Assert.Equal(HttpStatusCode.NotFound,(await pc.PostAsJsonAsync("/connector/v1/screenshots/prepare",catalog with{GameId="20"})).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest,(await pc.PostAsJsonAsync("/connector/v1/screenshots/prepare",catalog with{WorkspaceId="wrong"})).StatusCode);
            var plan=(await (await pc.PostAsJsonAsync("/connector/v1/screenshots/prepare",catalog)).Content.ReadFromJsonAsync<ScreenshotUploadPlan>())!;
            Assert.Single(plan.MissingAssets);Assert.Equal(HttpStatusCode.Conflict,(await pc.PostAsync($"/connector/v1/screenshots/{plan.Id}/commit",null)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound,(await owner.GetAsync("/api/v1/screenshots/steam/42")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest,(await pc.PostAsync($"/connector/v1/screenshots/{plan.Id}/assets/{PilotAsset}",new ByteArrayContent([1,2,3]))).StatusCode);
            Assert.Equal(HttpStatusCode.OK,(await pc.PostAsync($"/connector/v1/screenshots/{plan.Id}/assets/{PilotAsset}",new ByteArrayContent(PilotJpeg))).StatusCode);
            Assert.Equal(HttpStatusCode.OK,(await pc.PostAsync($"/connector/v1/screenshots/{plan.Id}/commit",null)).StatusCode);
            Assert.Equal(HttpStatusCode.OK,(await pc.PostAsync($"/connector/v1/screenshots/{plan.Id}/commit",null)).StatusCode);
            var again=(await (await pc.PostAsJsonAsync("/connector/v1/screenshots/prepare",catalog with{ExportedUtcTicks=DateTime.UtcNow.Ticks})).Content.ReadFromJsonAsync<ScreenshotUploadPlan>())!;Assert.True(again.Current);Assert.Empty(again.MissingAssets);
            var page=(await owner.GetFromJsonAsync<ScreenshotPage>("/api/v1/screenshots/steam/42"))!;Assert.Equal(61,page.Total);Assert.Equal(60,page.NextOffset);Assert.Equal(60,page.Photos.Count);
            Assert.Equal(61,page.FilteredTotal);Assert.Equal(2,page.Consoles.Count);
            var xbox=(await owner.GetFromJsonAsync<ScreenshotPage>("/api/v1/screenshots/steam/42?console=Xbox"))!;Assert.Equal(61,xbox.Total);Assert.Equal(1,xbox.FilteredTotal);Assert.Single(xbox.Photos);Assert.Equal("Xbox",xbox.Photos[0].Console);Assert.Null(xbox.NextOffset);
            Assert.Empty((await owner.GetFromJsonAsync<ScreenshotPage>("/api/v1/screenshots/steam/42?console=Unknown"))!.Photos);
            Assert.Equal(HttpStatusCode.Conflict,(await owner.GetAsync("/api/v1/screenshots/steam/42?version=old")).StatusCode);
            Assert.Equal(catalog.Photos[60],await owner.GetFromJsonAsync<ScreenshotPhoto>($"/api/v1/screenshots/steam/42/photos/{catalog.Photos[60].Id}"));
            Assert.Equal(61,(await owner.GetFromJsonAsync<GameCard[]>("/api/v1/games"))!.Single(g=>g.GameId=="42").Screenshots!.Count);
            Assert.Single((await owner.GetFromJsonAsync<ScreenshotPage>("/api/v1/screenshots/steam/42?offset=60"))!.Photos);
            var before=page.Version;
            var next=catalog with{Photos=catalog.Photos.Select(p=>p with{Preview=new string('b',64)+".jpg"}).ToArray()};var pending=(await (await pc.PostAsJsonAsync("/connector/v1/screenshots/prepare",next)).Content.ReadFromJsonAsync<ScreenshotUploadPlan>())!;
            Assert.Equal(HttpStatusCode.Conflict,(await pc.PostAsync($"/connector/v1/screenshots/{pending.Id}/commit",null)).StatusCode);Assert.Equal(before,(await owner.GetFromJsonAsync<ScreenshotPage>("/api/v1/screenshots/steam/42"))!.Version);
            state.SetAutomatic(false);Assert.Equal(HttpStatusCode.Conflict,(await pc.PostAsJsonAsync("/connector/v1/screenshots/prepare",catalog)).StatusCode);
            host.Services.GetRequiredService<PcConnector>().Revoke();Assert.Equal(HttpStatusCode.Unauthorized,(await pc.GetAsync("/connector/v1/screenshots/steam/42")).StatusCode);
            Assert.Equal(PilotJpeg,await owner.GetByteArrayAsync($"/api/v1/screenshots/steam/42/{catalog.Photos[0].Id}/preview"));
        }
        using var restart=new Host(false,root:fixture.Root,screenshotPilot:"steam:42");using var reader=restart.Client();reader.DefaultRequestHeaders.Add("Cookie",cookie);
        var saved=(await reader.GetFromJsonAsync<ScreenshotPage>("/api/v1/screenshots/steam/42"))!;Assert.Equal(61,saved.Total);Assert.Equal("Test group",saved.Photos[0].Group);
        Assert.Equal(PilotJpeg,await reader.GetByteArrayAsync($"/api/v1/screenshots/steam/42/{saved.Photos[0].Id}/thumbnail"));
        Assert.Equal(HttpStatusCode.BadRequest,(await reader.GetAsync("/api/v1/screenshots/steam/42?limit=61")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await reader.GetAsync($"/api/v1/screenshots/steam/42/{new string('f',64)}/preview")).StatusCode);
    }
    [Fact]
    public void ScreenshotPilotRejectsDuplicateIdsAndUnsafeAssetNamesAndIsDisabledByDefault()
    {
        using var fixture=new WorkspaceCase();fixture.Prepare();LibraryFixture(fixture.Root);using var host=new Host(false,root:fixture.Root,screenshotPilot:"steam:42");_ = host.Client();
        host.Services.GetRequiredService<ConnectorSyncState>().EnableLibraryAndAutomatic();var media=host.Services.GetRequiredService<ScreenshotLibrary>();var c=PilotCatalogue(host);
        Assert.Throws<InvalidDataException>(()=>media.Prepare(c with{Photos=[c.Photos[0],c.Photos[0]]}));
        Assert.Throws<InvalidDataException>(()=>media.Prepare(c with{Photos=[c.Photos[0] with{Preview="../private.jpg"}]}));
        Assert.Throws<InvalidDataException>(()=>media.Prepare(c with{Photos=[c.Photos[0] with{CaptureDay=null}]}));
        using var disabled=new Host(false,root:fixture.Root);_=disabled.Client();Assert.Throws<KeyNotFoundException>(()=>disabled.Services.GetRequiredService<ScreenshotLibrary>().Prepare(c));
    }
}
