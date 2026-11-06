using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace PixelVault.Companion.Tests;

public sealed partial class CompanionTests
{
    [Fact] public async Task ItemRulesPersistRequireAuthenticationAndRejectConcurrentChanges()
    {
        using var fixture = new WorkspaceCase(); fixture.Prepare(); using var host = new Host(false, root: fixture.Root);
        using var owner = host.Client(); await Setup(host, owner); using var anonymous = host.Client(); await Token(anonymous);
        var before = await Editor(owner); var item = before.GetProperty("states")[0].GetProperty("item").GetProperty("id").GetString()!;
        var version = before.GetProperty("rulesVersions").GetProperty(item).GetString();
        var request = new { itemId = item, missable = true, requiredForCompletion = false, expectedVersion = version };
        var path = $"/api/v1/trackers/{TrackerId}/item-rules";
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync(path, request)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync(path, request)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsJsonAsync(path, request)).StatusCode);
        var after = await Editor(owner);
        var saved = after.GetProperty("states").EnumerateArray().Single(s => s.GetProperty("item").GetProperty("id").GetString() == item);
        Assert.True(saved.GetProperty("item").GetProperty("missable").GetBoolean());
        Assert.True(saved.GetProperty("item").GetProperty("excludedFromTotal").GetBoolean());
        var previous = before.GetProperty("states").EnumerateArray().Single(s => s.GetProperty("item").GetProperty("id").GetString() == item);
        Assert.Equal(previous.GetProperty("note").GetRawText(), saved.GetProperty("note").GetRawText());
        Assert.Equal(previous.GetProperty("current").GetRawText(), saved.GetProperty("current").GetRawText());
        owner.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(path, request)).StatusCode);
        using var demo = new Host(); using var preview = demo.Client(); await Setup(demo, preview);
        Assert.Equal(HttpStatusCode.Forbidden, (await preview.PostAsJsonAsync("/api/v1/trackers/demo.explorer/item-rules", request)).StatusCode);
    }
}
