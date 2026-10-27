using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PixelVault.Companion;
using PixelVaultNative;
using Xunit;

namespace PixelVault.Companion.Tests;

public sealed partial class CompanionTests
{
    private sealed class Host : WebApplicationFactory<Program>
    {
        public string Root { get; }
        private readonly bool demo;
        private readonly bool allowHttp;
        private readonly bool ownsRoot;
        private readonly string screenshotPilot;
        private readonly bool screenshotLibrary;
        public Host(bool demo = true, bool allowHttp = true, string? root = null,string screenshotPilot="",bool screenshotLibrary=false)
        {
            this.screenshotPilot=screenshotPilot;this.screenshotLibrary=screenshotLibrary;
            this.demo = demo; this.allowHttp = allowHttp; ownsRoot = root is null;
            Root = root ?? Path.Combine(Path.GetTempPath(), "PixelVaultCompanionTests", Guid.NewGuid().ToString("N"));
        }
        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Testing")
            .ConfigureServices(services => services.AddSingleton<IStartupFilter, TestRemoteAddress>())
            .ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Companion:DataRoot"] = Root, ["Companion:DemoEnabled"] = demo.ToString(),
                ["Companion:AllowHttp"] = allowHttp.ToString(), ["Logging:LogLevel:Default"] = "None"
                ,["Companion:ScreenshotPilot"]=screenshotPilot,["Companion:ScreenshotLibraryEnabled"]=screenshotLibrary.ToString()
            }));
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && ownsRoot && Directory.Exists(Root)) Directory.Delete(Root, true);
        }
        public HttpClient Client() => CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    private sealed class TestRemoteAddress : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, continuation) =>
            {
                context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.23");
                return continuation();
            });
            next(app);
        };
    }

    private static async Task Token(HttpClient client)
    {
        var payload = await client.GetFromJsonAsync<JsonElement>("/api/v1/antiforgery");
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", payload.GetProperty("token").GetString());
    }
    private static async Task<string> Setup(Host host, HttpClient client)
    {
        await Token(client);
        var response = await client.PostAsJsonAsync("/api/v1/setup", new
        { token = File.ReadAllText(Path.Combine(host.Root, "setup-token.txt")), name = "Owner", password = "test-only-password-123" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await Token(client); // Antiforgery is bound to the signed-in identity.
        return response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("PixelVault.Companion.Session=", StringComparison.Ordinal)).Split(';')[0];
    }

    [Fact]
    public async Task OnlyHealthAndSessionShellArePublicAndNoPrivatePathsAreServed()
    {
        using var host = new Host(); using var client = host.Client();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
        var session = await client.GetFromJsonAsync<JsonElement>("/api/v1/session");
        Assert.True(session.GetProperty("setupRequired").GetBoolean());
        foreach (var path in new[] { "/api/v1/games", "/api/v1/trackers", "/api/v1/trackers/demo.explorer" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path)).StatusCode);
        foreach (var path in new[] { "/setup-token.txt", "/companion.sqlite", "/keys", "/api/v1/missing" })
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path)).StatusCode);
        var index = await client.GetStringAsync("/");
        Assert.Contains("PixelVault Companion", index);
        Assert.DoesNotContain(File.ReadAllText(Path.Combine(host.Root, "setup-token.txt")), index);
    }

    [Fact]
    public async Task SetupRequiresBothAntiforgeryAndLocalTokenAndCannotBeRepeated()
    {
        using var host = new Host(); using var client = host.Client();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/setup", new
        { token = "incorrect", name = "Owner", password = "test-only-password-123" })).StatusCode);
        await Token(client);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/setup", new
        { token = "incorrect", name = "Owner", password = "test-only-password-123" })).StatusCode);
        await Setup(host, client);
        var repeat = await client.PostAsJsonAsync("/api/v1/setup", new
        { token = File.ReadAllText(Path.Combine(host.Root, "setup-token.txt")), name = "Other", password = "other-password-123" });
        Assert.Equal(HttpStatusCode.Conflict, repeat.StatusCode);
        Assert.Equal("Owner", (await client.GetFromJsonAsync<JsonElement>("/api/v1/session")).GetProperty("name").GetString());
    }

    [Fact]
    public async Task OwnerCanBrowseSampleTrackersSignOutAndSignInAgain()
    {
        using var host = new Host(); using var client = host.Client();
        await Setup(host, client);
        var games = await client.GetFromJsonAsync<JsonElement>("/api/v1/games");
        Assert.Equal(2, games.GetArrayLength());
        var trackerResponse = await client.GetAsync("/api/v1/trackers/demo.explorer");
        Assert.True(trackerResponse.Headers.CacheControl?.NoStore);
        var tracker = await trackerResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(tracker.GetProperty("sample").GetBoolean()); Assert.True(tracker.GetProperty("readOnly").GetBoolean());
        Assert.Equal(2, tracker.GetProperty("totals").GetProperty("completed").GetInt32());
        Assert.Equal(JsonValueKind.Null, tracker.GetProperty("states")[2].GetProperty("current").ValueKind);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/trackers/live-tracker")).StatusCode);
        // No placeholder write endpoint can silently pretend to save.
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PostAsJsonAsync("/api/v1/trackers/demo.explorer", new { current = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/logout", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/trackers")).StatusCode);
        await Token(client);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/login", new { name = "Owner", password = "wrong" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/login", new { name = "owner", password = "test-only-password-123" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/trackers")).StatusCode);
    }

    [Fact]
    public async Task DemoIsOptInAndRequestsCannotSelectAnotherAccount()
    {
        using var host = new Host(demo: false); using var client = host.Client();
        await Setup(host, client);
        Assert.Empty((await client.GetFromJsonAsync<JsonElement>("/api/v1/trackers?account=someone-else")).EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/trackers/demo.explorer")).StatusCode);
    }

    [Fact]
    public async Task HttpAndUntrustedForwardedHeadersCannotBypassHttpsRequirement()
    {
        using var host = new Host(allowHttp: false); using var client = host.Client();
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v1/session")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
    }

    [Fact]
    public async Task LoginAttemptsAreRateLimited()
    {
        using var host = new Host(); using var client = host.Client(); await Token(client);
        for (int i = 0; i < 6; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/login", new { name = "x", password = "y" })).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsJsonAsync("/api/v1/login", new { name = "x", password = "y" })).StatusCode);
    }

    [Fact]
    public async Task AuthenticationCookieAndOwnerSurviveACompleteServiceRestart()
    {
        var root = Path.Combine(Path.GetTempPath(), "CompanionRestart-" + Guid.NewGuid().ToString("N"));
        try
        {
            string cookie;
            using (var first = new Host(root: root))
            using (var client = first.Client()) cookie = await Setup(first, client);
            using var second = new Host(root: root); using var restartedClient = second.Client();
            restartedClient.DefaultRequestHeaders.Add("Cookie", cookie);
            var session = await restartedClient.GetFromJsonAsync<JsonElement>("/api/v1/session");
            Assert.True(session.GetProperty("authenticated").GetBoolean());
            Assert.False(session.GetProperty("setupRequired").GetBoolean());
            Assert.Equal(HttpStatusCode.OK, (await restartedClient.GetAsync("/api/v1/trackers")).StatusCode);
            Assert.NotEmpty(Directory.GetFiles(Path.Combine(root, "keys"), "*.xml"));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void OwnerSurvivesStoreRecreationAndOnlyOneConcurrentSetupWins()
    {
        var root = Path.Combine(Path.GetTempPath(), "CompanionStore-" + Guid.NewGuid().ToString("N"));
        try
        {
            var options = new CompanionOptions { DataRoot = root }; var store = new OwnerStore(options);
            var token = File.ReadAllText(Path.Combine(root, "setup-token.txt"));
            var outcomes = new bool[2];
            Parallel.For(0, 2, n => outcomes[n] = store.Setup(token, "Owner", "test-only-password-123"));
            Assert.Single(outcomes, v => v);
            var restarted = new OwnerStore(options);
            Assert.NotNull(restarted.Authenticate("Owner", "test-only-password-123"));
            Assert.NotEqual("test-only-password-123", restarted.Read()!.PasswordHash);
            using var connection = new SqliteConnection($"Data Source={Path.Combine(root, "companion.sqlite")};Pooling=False");
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version=99"; command.ExecuteNonQuery();
            Assert.Throws<InvalidOperationException>(() => new OwnerStore(options));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void SharedTotalsPreserveUnknownOverridesArchivesAndUniqueCategoryGoals()
    {
        var sample = DemoTrackers.Create();
        var unknown = sample[0].States[2]; Assert.Null(unknown.Current); Assert.False(unknown.Complete);
        unknown.ReportedCurrent = 1; unknown.ManualCurrent = 0; Assert.Equal(0, unknown.Current);
        var ranked = sample[1];
        Assert.Equal(1, ranked.Totals.Completed); Assert.Equal(2, ranked.Totals.Total);
        Assert.Equal(3, ranked.Totals.Records); Assert.Equal(1, ranked.Totals.CompletedRecords);
        foreach (var state in ranked.States.Where(s => s.Item.GroupId == "ridge")) state.Item.Archived = true;
        Assert.Equal(0, ranked.Totals.Completed); Assert.Equal(2, ranked.Totals.Records);
        Assert.Throws<InvalidDataException>(() => TrackerMilestones.Labels(Enumerable.Range(0, 17).Select(n => "M" + n)));
        Assert.Throws<InvalidDataException>(() => TrackerMilestones.Expand(new[] { new AchievementTrackerItem { Id = "x", Target = 2 } }, new[] { "Done" }));
    }
}
