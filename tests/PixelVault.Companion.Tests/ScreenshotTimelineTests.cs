using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace PixelVault.Companion.Tests;
public sealed partial class CompanionTests
{
    [Fact]
    public async Task IndependentPendingGamesAndPublishedPhotosSurviveSourceRemovals()
    {
        using var fixture=new WorkspaceCase();SourceForTest(fixture);LibraryFixture(fixture.Root);using var host=new Host(false,root:fixture.Root,screenshotLibrary:true);using var owner=host.Client();await Setup(host,owner);host.Services.GetRequiredService<ConnectorSyncState>().EnableLibraryAndAutomatic();var media=host.Services.GetRequiredService<ScreenshotLibrary>();
        var one=PilotCatalogue(host,1);var two=PilotCatalogue(host,2) with{Provider="library",GameId="local-one",Title="A game without trackers",GameScope=new string('c',64)};
        var a=media.Prepare(one);var b=media.Prepare(two);media.Upload(a.Id,PilotAsset,PilotJpeg);media.Commit(a.Id);media.Commit(b.Id);Assert.Equal(3,media.Timeline().Total);
        var incoming=two with{Photos=[two.Photos[0] with{Group="Updated"}],Eligible=1};var retained=media.Prepare(incoming);Assert.Single(retained.RetainedPhotos);media.Commit(retained.Id);Assert.Equal(2,media.Page("library","local-one").Photos.Count);Assert.Equal("Updated",media.Page("library","local-one").Photos[0].Group);
        var catalogPath=Path.Combine(fixture.Root,"library","catalog.json");var catalog=System.Text.Json.JsonSerializer.Deserialize<GameCatalogue>(File.ReadAllText(catalogPath),LibraryJson)!;File.WriteAllText(catalogPath,System.Text.Json.JsonSerializer.Serialize(catalog with{Games=catalog.Games.Where(g=>g.GameId!="local-one").ToList()},LibraryJson));host.Services.GetRequiredService<GameLibrary>().Reload();
        Assert.Equal(3,media.Timeline().Total);Assert.Equal(2,media.Page("library","local-one").Photos.Count);Assert.Throws<KeyNotFoundException>(()=>media.Prepare(two));
    }
    [Fact]
    public async Task TimelineCombinesGamesFiltersBeforePagingRejectsStaleVersionsAndKeepsLegacyMedia()
    {
        using var fixture=new WorkspaceCase();SourceForTest(fixture);LibraryFixture(fixture.Root);string cookie;string beforeVersion;
        using(var host=new Host(false,root:fixture.Root,screenshotPilot:"steam:42",screenshotLibrary:true))using(var owner=host.Client())using(var pc=host.Client())
        {
            Assert.Equal(HttpStatusCode.Unauthorized,(await owner.GetAsync("/api/v1/screenshots/timeline")).StatusCode);cookie=await Setup(host,owner);
            var pair=PairForTest(fixture,host);pc.DefaultRequestHeaders.Authorization=new("Bearer",pair.Token);host.Services.GetRequiredService<ConnectorSyncState>().EnableLibraryAndAutomatic();var media=host.Services.GetRequiredService<ScreenshotLibrary>();
            var first=PilotCatalogue(host,61);var plan=media.Prepare(first);media.Upload(plan.Id,PilotAsset,PilotJpeg);media.Commit(plan.Id);
            var mediaRoot=Path.Combine(fixture.Root,"screenshots",first.WorkspaceId.ToLowerInvariant());File.Move(Directory.GetFiles(Path.Combine(mediaRoot,"catalogues"),"*.json").Single(),Path.Combine(mediaRoot,"catalogue.json"));
            var old=media.Page("steam","42");beforeVersion=media.Timeline().Version;
            var second=first with{Provider="library",GameId="local-one",Title="A game without trackers",GameScope=new string('b',64),Photos=[first.Photos[0] with{Console="Xbox",CaptureUtcTicks=first.Photos[0].CaptureUtcTicks+1,CaptureDay="2025-08-20"},first.Photos[1] with{Console="Xbox",CaptureUtcTicks=0,CaptureDay=null}],Eligible=2};
            var next=media.Prepare(second);Assert.Equal(beforeVersion,media.Timeline().Version);media.Commit(next.Id);Assert.Equal(old.Version,media.Page("steam","42").Version);Assert.Equal(old.Photos,media.Page("steam","42").Photos);
            var timeline=(await owner.GetFromJsonAsync<ScreenshotTimeline>("/api/v1/screenshots/timeline"))!;Assert.Equal(63,timeline.Total);Assert.Equal(60,timeline.Photos.Count);Assert.Equal(60,timeline.NextOffset);Assert.Equal("library",timeline.Photos[0].Provider);Assert.Equal("Xbox",timeline.Photos[0].Photo.Console);
            var remaining=(await owner.GetFromJsonAsync<ScreenshotTimeline>("/api/v1/screenshots/timeline?offset=60&version="+timeline.Version))!;Assert.Equal(3,remaining.Photos.Count);Assert.Null(remaining.NextOffset);Assert.Equal(0,remaining.Photos[^1].Photo.CaptureUtcTicks);
            Assert.Equal(63,timeline.Photos.Concat(remaining.Photos).Select(p=>p.Provider+":"+p.GameId+":"+p.Photo.Id).Distinct().Count());
            var xbox=(await owner.GetFromJsonAsync<ScreenshotTimeline>("/api/v1/screenshots/timeline?console=Xbox"))!;Assert.Equal(2,xbox.FilteredTotal);Assert.Equal(63,xbox.Total);Assert.Equal(2,xbox.Consoles.Count);
            Assert.Equal(61,(await owner.GetFromJsonAsync<ScreenshotTimeline>("/api/v1/screenshots/timeline?through=2025-08-19"))!.FilteredTotal);
            Assert.Empty((await owner.GetFromJsonAsync<ScreenshotTimeline>("/api/v1/screenshots/timeline?console=Unknown"))!.Photos);
            foreach(var query in new[]{"limit=61","offset=-1","through=not-a-date"})Assert.Equal(HttpStatusCode.BadRequest,(await owner.GetAsync("/api/v1/screenshots/timeline?"+query)).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict,(await owner.GetAsync("/api/v1/screenshots/timeline?version="+beforeVersion)).StatusCode);
            Assert.Equal("library",media.Recent().Photos[0].Provider);Assert.Equal(30,media.Recent().Photos.Count);
            Assert.Equal(2,(await owner.GetFromJsonAsync<GameCard[]>("/api/v1/games"))!.Count(g=>g.Screenshots is not null));
            Assert.Throws<KeyNotFoundException>(()=>media.Prepare(second with{GameId="unapproved"}));
            host.Services.GetRequiredService<PcConnector>().Revoke();Assert.Equal(63,media.Timeline().Total);
        }
        using var restart=new Host(false,root:fixture.Root,screenshotLibrary:true);using var reader=restart.Client();reader.DefaultRequestHeaders.Add("Cookie",cookie);Assert.Equal(63,(await reader.GetFromJsonAsync<ScreenshotTimeline>("/api/v1/screenshots/timeline"))!.Total);
    }
    [Fact]
    public async Task PhotoMetadataSidecarsStayAtomicAndAreOnlyServedAsAuthenticatedAttachments()
    {
        using var fixture=new WorkspaceCase();SourceForTest(fixture);LibraryFixture(fixture.Root);using var host=new Host(false,root:fixture.Root,screenshotLibrary:true);using var owner=host.Client();await Setup(host,owner);
        host.Services.GetRequiredService<ConnectorSyncState>().EnableLibraryAndAutomatic();var media=host.Services.GetRequiredService<ScreenshotLibrary>();var bytes=System.Text.Encoding.UTF8.GetBytes("<x:xmpmeta>Original tags &amp; metadata</x:xmpmeta>");var name=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()+".xmp";
        var sidecar=new ScreenshotOriginal(name,"Photo.jxr.xmp",bytes.Length,"application/rdf+xml",DateTime.UtcNow.Ticks);var c=PilotCatalogue(host,1);c=c with{Photos=[c.Photos[0] with{Metadata=sidecar}]};var plan=media.Prepare(c);media.Upload(plan.Id,PilotAsset,PilotJpeg);Assert.Throws<InvalidOperationException>(()=>media.Commit(plan.Id));await media.UploadOriginal(plan.Id,name,new MemoryStream(bytes),default);media.Commit(plan.Id);
        var url=$"/api/v1/screenshots/steam/42/{c.Photos[0].Id}/metadata";using var anonymous=host.Client();Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.GetAsync(url)).StatusCode);using var response=await owner.GetAsync(url);Assert.Equal(bytes,await response.Content.ReadAsByteArrayAsync());Assert.Equal("attachment",response.Content.Headers.ContentDisposition!.DispositionType);Assert.Equal(sidecar.FileName,response.Content.Headers.ContentDisposition.FileNameStar);
        Assert.Throws<InvalidDataException>(()=>media.Prepare(c with{Photos=[c.Photos[0] with{Metadata=null}]}));Assert.Throws<InvalidDataException>(()=>media.Prepare(c with{Photos=[c.Photos[0] with{Metadata=sidecar with{FileName="../metadata.xmp"}}]}));
    }
}
