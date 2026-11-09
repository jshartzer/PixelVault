using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PixelVault.Companion.Client;
using Xunit;

namespace PixelVault.Companion.Tests;

public sealed partial class CompanionTests
{
    [Fact]
    public async Task RefreshStreamRequiresOwnerAndMatchingWorkspace()
    {
        using var fixture=new RuntimeCase();using var host=fixture.Host();
        using var owner=host.Client();await Setup(host,owner);
        Assert.Equal(HttpStatusCode.Unauthorized,(await host.Client().GetAsync("/api/v1/refresh/events?workspace=wrong")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await owner.GetAsync("/api/v1/refresh/events?workspace=wrong")).StatusCode);
        var updates=host.Services.GetRequiredService<RefreshUpdates>();
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var response=await owner.SendAsync(new HttpRequestMessage(HttpMethod.Get,"/api/v1/refresh/events?workspace="+fixture.Repo.DatabasePath.GetHashCode()),HttpCompletionOption.ResponseHeadersRead,timeout.Token);
        Assert.Equal(HttpStatusCode.Conflict,response.StatusCode);
        var workspace=host.Services.GetRequiredService<TrackerWorkspace>();
        using var streamResponse=await owner.SendAsync(new HttpRequestMessage(HttpMethod.Get,"/api/v1/refresh/events?workspace="+workspace.Identity),HttpCompletionOption.ResponseHeadersRead,timeout.Token);
        streamResponse.EnsureSuccessStatusCode();Assert.Equal("text/event-stream",streamResponse.Content.Headers.ContentType!.MediaType);
        using var body=new StreamReader(await streamResponse.Content.ReadAsStreamAsync(timeout.Token));
        string? line;
        do{line=await body.ReadLineAsync(timeout.Token);}while(line is not null&&!line.StartsWith("data: "));
        var snapshot=JsonSerializer.Deserialize<RefreshSnapshot>(line![6..],new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(workspace.Identity,snapshot.WorkspaceId);Assert.True(snapshot.Sequence>0);
        Assert.DoesNotContain(fixture.Data.App,line);Assert.DoesNotContain(Approved,line);
    }

    [Fact]
    public void RefreshCoordinatorIgnoresTimestampsAndCoalescesToLatestCommittedState()
    {
        using var fixture=new DirectCase();using var host=fixture.Host();
        var updates=host.Services.GetRequiredService<RefreshUpdates>();
        var first=updates.Sample();var subscriber=updates.Subscribe();
        Assert.True(subscriber.Channel.Reader.TryRead(out var hello));Assert.Equal(first.Id,hello!.Id);
        host.Services.GetRequiredService<ConnectorSyncState>().Put("local-worker-tick");
        Assert.Equal(first.Id,updates.Sample().Id);Assert.False(subscriber.Channel.Reader.TryRead(out _));
        var path=Path.Combine(fixture.App,"achievement-hub","account.json");
        File.WriteAllText(path,File.ReadAllText(path).Replace("\"UpdatedUtc\":1","\"UpdatedUtc\":123"));
        Assert.Equal(first.Id,updates.Sample().Id);
        File.WriteAllText(path,File.ReadAllText(path).Replace("\"Unlocked\":false","\"Unlocked\":true"));
        var earned=updates.Sample();Assert.NotEqual(first.Versions["achievements"],earned.Versions["achievements"]);
        Assert.Equal(first.Versions["trackers"],earned.Versions["trackers"]);
        File.WriteAllText(path,File.ReadAllText(path).Replace("\"Unlocked\":true","\"Unlocked\":false"));
        var latest=updates.Sample();Assert.True(subscriber.Channel.Reader.TryRead(out var delivered));Assert.Equal(latest.Id,delivered!.Id);
        Assert.False(subscriber.Channel.Reader.TryRead(out _));subscriber.Close();
        var reconnect=updates.Subscribe();Assert.True(reconnect.Channel.Reader.TryRead(out var catchup));Assert.Equal(latest.Id,catchup!.Id);reconnect.Close();
    }
}
