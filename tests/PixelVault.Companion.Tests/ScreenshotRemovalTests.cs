using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace PixelVault.Companion.Tests;
public sealed partial class CompanionTests
{
    [Fact]
    public async Task ExplicitRemovalsHideEveryMediaSurfaceRejectStaleUpdatesAndCanRestoreAfterRepublishing()
    {
        using var fixture=new WorkspaceCase();SourceForTest(fixture);LibraryFixture(fixture.Root);
        string cookie;string removedId;string workspace;
        using(var host=new Host(false,root:fixture.Root,screenshotLibrary:true))using(var owner=host.Client())using(var pc=host.Client())
        {
            cookie=await Setup(host,owner);var pairing=PairForTest(fixture,host);workspace=pairing.WorkspaceId;pc.DefaultRequestHeaders.Authorization=new("Bearer",pairing.Token);host.Services.GetRequiredService<ConnectorSyncState>().EnableLibraryAndAutomatic();
            var media=host.Services.GetRequiredService<ScreenshotLibrary>();var c=PilotCatalogue(host,2);c=c with{Photos=c.Photos.Select(p=>p with{Original=new(PilotAsset,"capture.jpg",PilotJpeg.LongLength,"image/jpeg",DateTime.UtcNow.Ticks)}).ToArray()};var plan=media.Prepare(c);media.Upload(plan.Id,PilotAsset,PilotJpeg);await media.UploadOriginal(plan.Id,PilotAsset,new MemoryStream(PilotJpeg),default);media.Commit(plan.Id);removedId=c.Photos[0].Id;
            using var anonymous=host.Client();Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.GetAsync("/connector/v1/screenshots/removals")).StatusCode);
            var game=(await pc.GetFromJsonAsync<ScreenshotRemovalGame[]>("/connector/v1/screenshots/removals"))!.Single();var beforeTimeline=media.Timeline().Version;
            var request=new ScreenshotRemovalRequest(workspace,game.Version,[removedId],[]);
            Assert.Equal(HttpStatusCode.BadRequest,(await pc.PostAsJsonAsync("/connector/v1/screenshots/removals/steam/42",request with{WorkspaceId=new string('f',64)})).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest,(await pc.PostAsJsonAsync("/connector/v1/screenshots/removals/steam/42",request with{RemoveIds=[new string('f',64)]})).StatusCode);
            using var response=await pc.PostAsJsonAsync("/connector/v1/screenshots/removals/steam/42",request);response.EnsureSuccessStatusCode();var receipt=(await response.Content.ReadFromJsonAsync<ScreenshotRemovalGame>())!;Assert.True(receipt.Photos.Single(p=>p.Id==removedId).Removed);
            Assert.Equal(1,media.Timeline().Total);Assert.Single(media.Recent().Photos);Assert.Single(media.Page("steam","42").Photos);Assert.NotEqual(beforeTimeline,media.Timeline().Version);
            Assert.Equal(1,(await owner.GetFromJsonAsync<GameCard[]>("/api/v1/games"))!.Single(g=>g.GameId=="42").Screenshots!.Count);
            Assert.Equal(HttpStatusCode.NotFound,(await owner.GetAsync($"/api/v1/screenshots/steam/42/photos/{removedId}")).StatusCode);Assert.Equal(HttpStatusCode.NotFound,(await owner.GetAsync($"/api/v1/screenshots/steam/42/{removedId}/thumbnail")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound,(await owner.GetAsync($"/api/v1/screenshots/steam/42/{removedId}/original")).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict,(await pc.PostAsJsonAsync("/connector/v1/screenshots/removals/steam/42",request)).StatusCode);
            var stale=media.Prepare(c);Assert.Contains(removedId,stale.SuppressedPhotoIds);if(!stale.Current)media.Commit(stale.Id);Assert.Equal(1,media.Timeline().Total);
            // A normal update can drop old raw rows; the removal record still restores their metadata.
            var changed=c with{Photos=[c.Photos[1] with{Group="Updated"}],Eligible=1};var update=media.Prepare(changed);media.Commit(update.Id);
            game=media.RemovalInventory().Single();media.SetRemovals("steam","42",new(workspace,game.Version,[],[removedId]));Assert.Equal(2,media.Timeline().Total);Assert.Equal(removedId,media.Photo("steam","42",removedId).Id);Assert.Equal(PilotJpeg,await owner.GetByteArrayAsync($"/api/v1/screenshots/steam/42/{removedId}/original"));
            game=media.RemovalInventory().Single();media.SetRemovals("steam","42",new(workspace,game.Version,game.Photos.Select(p=>p.Id).ToArray(),[]));Assert.Empty(media.Timeline().Photos);Assert.Empty(media.Recent().Photos);Assert.Empty(media.Page("steam","42").Photos);
            Assert.Null((await owner.GetFromJsonAsync<GameCard[]>("/api/v1/games"))!.Single(g=>g.GameId=="42").Screenshots);
            var root=Path.Combine(fixture.Root,"screenshots",workspace.ToLowerInvariant());Assert.True(File.Exists(Path.Combine(root,"assets",PilotAsset)));Assert.True(File.Exists(Path.Combine(root,"originals",PilotAsset)));Assert.NotEmpty(Directory.GetFiles(Path.Combine(root,"removal-history"),"*.json"));
            var pausedVersion=media.RemovalInventory().Single().Version;host.Services.GetRequiredService<ConnectorSyncState>().SetAutomatic(false);
            Assert.Equal(HttpStatusCode.Conflict,(await pc.PostAsJsonAsync("/connector/v1/screenshots/removals/steam/42",new ScreenshotRemovalRequest(workspace,pausedVersion,[],[removedId]))).StatusCode);
        }
        using var restart=new Host(false,root:fixture.Root,screenshotLibrary:true);using var browser=restart.Client();browser.DefaultRequestHeaders.Add("Cookie",cookie);Assert.Empty((await browser.GetFromJsonAsync<ScreenshotTimeline>("/api/v1/screenshots/timeline"))!.Photos);
    }
}
