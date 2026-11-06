using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PixelVaultNative;
using Xunit;

namespace PixelVault.Companion.Tests;
public sealed partial class CompanionTests
{
    [Fact]
    public async Task TrackerGroupOrderIsSharedBidirectionallyWithoutChangingProgress()
    {
        using var fixture=new DirectCase();using var host=fixture.Host();using var owner=host.Client();await Setup(host,owner);
        var native=new TrackerRepository(fixture.App,pooling:false);var before=JsonSerializer.Serialize(native.Load(TrackerId,Approved));
        async Task<string[]> Order(){var value=await owner.GetFromJsonAsync<JsonElement>("/api/v1/preferences/tracker-groups");return value.GetProperty("groups").EnumerateArray().Select(v=>v.GetString()!).ToArray();}
        Assert.Equal(new[]{"achievement","manual","century"},await Order());
        native.MoveTrackerGroup("century",-1);Assert.Equal(new[]{"achievement","century","manual"},await Order());
        var response=await owner.PostAsJsonAsync("/api/v1/preferences/tracker-groups",new{group="century",direction=-1});Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        Assert.Equal(new[]{"century","achievement","manual"},native.TrackerGroupOrder());
        Assert.Equal(before,JsonSerializer.Serialize(native.Load(TrackerId,Approved)));
        Assert.Equal(HttpStatusCode.BadRequest,(await owner.PostAsJsonAsync("/api/v1/preferences/tracker-groups",new{group="unknown",direction=1})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await owner.PostAsJsonAsync("/api/v1/preferences/tracker-groups",new{group="manual",direction=3})).StatusCode);
        using var anonymous=host.Client();Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.GetAsync("/api/v1/preferences/tracker-groups")).StatusCode);
        owner.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");Assert.Equal(HttpStatusCode.BadRequest,(await owner.PostAsJsonAsync("/api/v1/preferences/tracker-groups",new{group="manual",direction=-1})).StatusCode);
        using var demo=new Host();using var sample=demo.Client();await Setup(demo,sample);Assert.Equal(HttpStatusCode.Forbidden,(await sample.PostAsJsonAsync("/api/v1/preferences/tracker-groups",new{group="manual",direction=-1})).StatusCode);
    }
}
