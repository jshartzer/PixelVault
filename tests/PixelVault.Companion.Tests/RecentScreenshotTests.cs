using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace PixelVault.Companion.Tests;
public sealed partial class CompanionTests
{
    [Fact]
    public async Task RecentPhotosAreAuthenticatedBoundedChronologicalAndIndependentOfPc()
    {
        using var fixture=new WorkspaceCase();SourceForTest(fixture);LibraryFixture(fixture.Root);string cookie;
        using(var host=new Host(false,root:fixture.Root,screenshotPilot:"steam:42"))using(var owner=host.Client())using(var anonymous=host.Client())
        {
            Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.GetAsync("/api/v1/screenshots/recent")).StatusCode);
            cookie=await Setup(host,owner);
            Assert.Empty((await owner.GetFromJsonAsync<RecentScreenshots>("/api/v1/screenshots/recent"))!.Photos);
            host.Services.GetRequiredService<ConnectorSyncState>().EnableLibraryAndAutomatic();
            var media=host.Services.GetRequiredService<ScreenshotLibrary>();var c=PilotCatalogue(host,65);
            c=c with{Photos=c.Photos.Select((p,i)=>i==64?p with{CaptureUtcTicks=c.Photos[0].CaptureUtcTicks+100}:i==63?p with{CaptureUtcTicks=0,CaptureDay=null}:p).ToArray()};
            var plan=media.Prepare(c);media.Upload(plan.Id,PilotAsset,PilotJpeg);media.Commit(plan.Id);
            var feed=(await owner.GetFromJsonAsync<RecentScreenshots>("/api/v1/screenshots/recent"))!;
            Assert.Equal(65,feed.Total);Assert.Equal(30,feed.Photos.Count);Assert.Equal(c.Photos[64],feed.Photos[0].Photo);
            Assert.Equal(feed.Photos.Select(p=>p.Photo.CaptureUtcTicks).OrderByDescending(t=>t),feed.Photos.Select(p=>p.Photo.CaptureUtcTicks));
            Assert.All(feed.Photos,p=>{Assert.Equal("steam",p.Provider);Assert.Equal("42",p.GameId);Assert.Equal(c.Title,p.Title);});
            Assert.Equal(3,(await owner.GetFromJsonAsync<RecentScreenshots>("/api/v1/screenshots/recent?limit=3"))!.Photos.Count);
            foreach(var limit in new[]{0,31,-1})Assert.Equal(HttpStatusCode.BadRequest,(await owner.GetAsync($"/api/v1/screenshots/recent?limit={limit}")).StatusCode);
            var json=await owner.GetStringAsync("/api/v1/screenshots/recent");Assert.DoesNotContain("workspaceId",json);Assert.DoesNotContain("gameScope",json);Assert.DoesNotContain("exportedUtcTicks",json);
            host.Services.GetRequiredService<ConnectorSyncState>().SetAutomatic(false);host.Services.GetRequiredService<PcConnector>().Revoke();
            Assert.Equal(feed.Version,(await owner.GetFromJsonAsync<RecentScreenshots>("/api/v1/screenshots/recent"))!.Version);
        }
        using var restart=new Host(false,root:fixture.Root,screenshotPilot:"steam:42");using var reader=restart.Client();reader.DefaultRequestHeaders.Add("Cookie",cookie);
        var saved=(await reader.GetFromJsonAsync<RecentScreenshots>("/api/v1/screenshots/recent"))!;Assert.Equal(30,saved.Photos.Count);
        Assert.Equal(PilotJpeg,await reader.GetByteArrayAsync($"/api/v1/screenshots/steam/42/{saved.Photos[0].Photo.Id}/thumbnail"));
        using var disabled=new Host(false,root:fixture.Root);Assert.Empty(disabled.Services.GetRequiredService<ScreenshotLibrary>().Recent().Photos);
    }
}
