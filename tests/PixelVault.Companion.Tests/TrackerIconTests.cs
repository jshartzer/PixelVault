using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Data.Sqlite;
using PixelVaultNative;
using Xunit;

namespace PixelVault.Companion.Tests;
public sealed partial class CompanionTests
{
    [Fact]
    public async Task CosmeticIconChangesRetainPublicationPermissionsAndTitleSyncBaseline()
    {
        using var fixture=new WorkspaceCase();SourceForTest(fixture);using var host=new Host(false,root:fixture.Root);using var owner=host.Client();await Setup(host,owner);var pair=PairForTest(fixture,host);
        host.Services.GetRequiredService<ConnectorSyncState>().EnableLibraryAndAutomatic();using var pc=host.Client();pc.DefaultRequestHeaders.Authorization=new("Bearer",pair.Token);
        var d=new AchievementTrackerDefinition{Id="custom.icon-published",Provider="steam",GameId="42",GameTitle="Test journey",Title="Published title",Items=new(){new(){Id="one",Name="One"}}};
        var publish=new TrackerPublication(pair.WorkspaceId,[new(d,null!)]);Assert.Equal(HttpStatusCode.OK,(await pc.PostAsJsonAsync("/connector/v1/trackers",publish)).StatusCode);
        using(var connection=new SqliteConnection("Data Source="+Path.Combine(fixture.Root,"companion.sqlite")))
        {
            connection.Open();using var command=connection.CreateCommand();command.CommandText="SELECT digest FROM companion_published_tracker WHERE id=$id";command.Parameters.AddWithValue("$id",d.Id);
            var historical=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(d,AchievementTrackingCatalog.Json))));
            Assert.Equal(historical,command.ExecuteScalar());
        }
        Assert.Equal(HttpStatusCode.OK,(await owner.PostAsJsonAsync($"/api/v1/trackers/{d.Id}/icon",new{iconId="building"})).StatusCode);
        var current=fixture.Repo().Definitions().Single(t=>t.Id==d.Id);Assert.True(TrackerDefinitionComparison.SameStructure(d,current));
        var rename=await pc.PostAsJsonAsync("/connector/v1/tracker-titles",new TrackerTitles(pair.WorkspaceId,[new(d.Id,d.Provider,d.GameId,"Renamed on desktop")]));Assert.Equal(HttpStatusCode.OK,rename.StatusCode);
        Assert.Equal(new TrackerTitleResult(1,0),await rename.Content.ReadFromJsonAsync<TrackerTitleResult>());
        Assert.Equal("building",fixture.Repo().Definitions().Single(t=>t.Id==d.Id).IconId);
        current.Items[0].Target=2;Assert.False(TrackerDefinitionComparison.SameStructure(d,current));
    }
    [Fact]
    public async Task TrackerImagesPersistAcrossSessionsAndPreserveCountsNotesHistoryAndEditVersions()
    {
        using var fixture=new WorkspaceCase();fixture.Prepare();using var host=new Host(false,root:fixture.Root);using var owner=host.Client();await Setup(host,owner);
        var before=await Editor(owner);var version=await UpdateVersion(owner);var repo=fixture.Repo();var states=JsonSerializer.Serialize(repo.Load(TrackerId,Approved));var history=JsonSerializer.Serialize(repo.History(TrackerId,Approved));
        var response=await owner.PostAsJsonAsync($"/api/v1/trackers/{TrackerId}/icon",new{iconId="deliveries"});Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        using var second=host.Client();await Token(second);await second.PostAsJsonAsync("/api/v1/login",new{name="Owner",password="test-only-password-123"});
        Assert.Equal("deliveries",(await Editor(second)).GetProperty("definition").GetProperty("iconId").GetString());
        var listing=await second.GetFromJsonAsync<JsonElement>("/api/v1/trackers");Assert.Equal("deliveries",listing.EnumerateArray().Single(t=>t.GetProperty("id").GetString()==TrackerId).GetProperty("iconId").GetString());
        Assert.Equal(version,await UpdateVersion(owner));Assert.Equal(before.GetProperty("versions").GetRawText(),(await Editor(owner)).GetProperty("versions").GetRawText());
        Assert.Equal(states,JsonSerializer.Serialize(repo.Load(TrackerId,Approved)));Assert.Equal(history,JsonSerializer.Serialize(repo.History(TrackerId,Approved)));
        // An item draft created before changing the image remains valid.
        Assert.Equal(HttpStatusCode.OK,(await Save(owner,Edit(before,"count","progress",4))).StatusCode);
        Assert.Null(fixture.Desktop.Definitions().Single(d=>d.Id==TrackerId).IconId);
        Assert.Equal(HttpStatusCode.OK,(await owner.PostAsJsonAsync($"/api/v1/trackers/{TrackerId}/icon",new{iconId=(string?)null})).StatusCode);
        Assert.Null(repo.Definitions().Single(d=>d.Id==TrackerId).IconId);
    }
    [Fact]
    public async Task TrackerImagesEnforceOwnerCsrfReadOnlyAndApprovedTrackerScope()
    {
        using var fixture=new WorkspaceCase();fixture.Desktop.AddDefinition(new(){Id="other.provider",Provider="retroachievements",GameId="42",GameTitle="Other",Title="Other account",Items=new(){new(){Id="one",Name="One"}}});fixture.Prepare();
        using var host=new Host(false,root:fixture.Root);using var owner=host.Client();await Setup(host,owner);using var anonymous=host.Client();await Token(anonymous);
        const string path="/api/v1/trackers/"+TrackerId+"/icon";
        Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.PostAsJsonAsync(path,new{iconId="trophy"})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await owner.PostAsJsonAsync(path,new{iconId="<script>"})).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await owner.PostAsJsonAsync("/api/v1/trackers/other.provider/icon",new{iconId="trophy"})).StatusCode);
        owner.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");Assert.Equal(HttpStatusCode.BadRequest,(await owner.PostAsJsonAsync(path,new{iconId="trophy"})).StatusCode);
        using var demo=new Host();using var sample=demo.Client();await Setup(demo,sample);Assert.Equal(HttpStatusCode.Forbidden,(await sample.PostAsJsonAsync("/api/v1/trackers/demo.explorer/icon",new{iconId="trophy"})).StatusCode);
        Assert.Null(fixture.Repo().Definitions().Single(d=>d.Id==TrackerId).IconId);
    }
}
