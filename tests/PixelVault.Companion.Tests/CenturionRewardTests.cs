using System.Net;
using System.Net.Http.Json;
using PixelVault.Tracking;
using Xunit;

namespace PixelVault.Companion.Tests;
public sealed partial class CompanionTests
{
    [Fact] public async Task CenturionCollectionIsAuthenticatedScopedAndDoesNotInventUnlocks()
    {
        using var fixture=new WorkspaceCase();SourceForTest(fixture);using var host=new Host(false,root:fixture.Root);using var owner=host.Client();await Setup(host,owner);
        Assert.Equal(HttpStatusCode.Unauthorized,(await host.Client().GetAsync("/api/v1/centurion")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,(await host.Client().GetAsync("/api/v1/centurion/steam/1850570/badge")).StatusCode);
        var games=(await owner.GetFromJsonAsync<CenturionGame[]>("/api/v1/centurion"))!;
        var game=Assert.Single(games);Assert.Equal(5,game.Items.Length);Assert.All(game.Items,i=>Assert.False(i.Unlocked));Assert.False(game.BadgeUnlocked);Assert.True(game.BadgeAvailable);Assert.Equal(64,game.Scope.Length);
        var image=await owner.GetAsync("/api/v1/centurion/steam/1850570/badge");Assert.Equal("image/png",image.Content.Headers.ContentType!.MediaType);Assert.True((await image.Content.ReadAsByteArrayAsync()).Length>1000);
        Assert.Equal(HttpStatusCode.NotFound,(await owner.GetAsync("/api/v1/centurion/steam/42/badge")).StatusCode);
    }
}
