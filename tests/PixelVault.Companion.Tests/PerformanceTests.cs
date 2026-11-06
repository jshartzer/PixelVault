using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace PixelVault.Companion.Tests;

public sealed partial class CompanionTests
{
    [Fact]
    public void LightweightTrackerAccountLookupRejectsRemovedAndUnapprovedTrackers()
    {
        using var f=new DirectCase();using var w=new TrackerWorkspace(new(){StorageMode="direct-local",DataRoot=f.State,ApplicationDataRoot=f.App,CacheRoot=f.Cache});
        Assert.Equal(Approved,w.Account(TrackerId));
        Assert.Equal(Approved,w.Account(PixelVaultNative.AchievementTrackingCatalog.DeathStrandingId));
        Assert.Throws<KeyNotFoundException>(()=>w.Account("missing' OR 1=1 --"));
        using var c=new SqliteConnection("Data Source="+w.Repository.DatabasePath+";Pooling=False");c.Open();using var q=c.CreateCommand();
        q.Parameters.AddWithValue("$id",TrackerId);
        q.CommandText="UPDATE tracker_definition SET definition=json_set(definition,'$.Provider','xbox') WHERE id=$id";q.ExecuteNonQuery();
        Assert.Throws<KeyNotFoundException>(()=>w.Account(TrackerId));
        q.CommandText="UPDATE tracker_definition SET definition=json_set(definition,'$.Provider','steam') WHERE id=$id";q.ExecuteNonQuery();
        Assert.Equal(Approved,w.Account(TrackerId));
        q.CommandText="DELETE FROM tracker_definition WHERE id=$id";q.ExecuteNonQuery();
        Assert.Throws<KeyNotFoundException>(()=>w.Account(TrackerId));
    }

    [Fact]
    public void CachedEditorsObserveNativeWritesUndoAndRecycledWal()
    {
        using var f=new DirectCase();using var w=new TrackerWorkspace(new(){StorageMode="direct-local",DataRoot=f.State,ApplicationDataRoot=f.App,CacheRoot=f.Cache});
        JsonElement View(bool compact=false)=>JsonSerializer.SerializeToElement(w.View(TrackerId,compact),LibraryJson);
        var original=View();var revision=w.TrackerRevision;
        var edit=w.Repository.Edit(TrackerId,Approved,Edit(original,"count","progress",current:4));
        Assert.NotEqual(revision,w.TrackerRevision);
        var changed=View();Assert.Equal(4,changed.GetProperty("states").EnumerateArray().Single(s=>s.GetProperty("item").GetProperty("id").GetString()=="count").GetProperty("current").GetInt32());
        w.Repository.UndoCommand(TrackerId,Approved,edit.HistoryId!.Value,Guid.NewGuid().ToString());
        var undone=View();Assert.NotEqual(original.GetProperty("versions").GetProperty("count").GetProperty("progress").GetString(),undone.GetProperty("versions").GetProperty("count").GetProperty("progress").GetString());
        var compact=View(true);Assert.False(compact.GetProperty("states")[0].TryGetProperty("item",out _));
        foreach(var state in undone.GetProperty("states").EnumerateArray())
        {
            var small=compact.GetProperty("states").EnumerateArray().Single(s=>s.GetProperty("itemId").GetString()==state.GetProperty("item").GetProperty("id").GetString());
            foreach(var field in new[]{"current","reportedCurrent","manualCurrent","isKnown","complete","isManual","hasConflict","availability","note"})Assert.Equal(state.GetProperty(field).GetRawText(),small.GetProperty(field).GetRawText());
        }
        using var writer=new SqliteConnection("Data Source="+w.Repository.DatabasePath+";Pooling=False");writer.Open();using var q=writer.CreateCommand();
        q.CommandText="PRAGMA journal_mode=WAL;UPDATE tracker_item_state SET note='First note' WHERE tracker_id=$t AND account=$a AND item_id='first'";q.Parameters.AddWithValue("$t",TrackerId);q.Parameters.AddWithValue("$a",Approved);q.ExecuteNonQuery();
        _=View();var before=w.TrackerRevision;var wal=w.Repository.DatabasePath+"-wal";var time=File.GetLastWriteTimeUtc(wal);
        q.CommandText="UPDATE tracker_item_state SET note='Later note' WHERE tracker_id=$t AND account=$a AND item_id='first'";q.ExecuteNonQuery();File.SetLastWriteTimeUtc(wal,time);
        Assert.NotEqual(before,w.TrackerRevision);Assert.Contains("Later note",View().GetRawText());Assert.DoesNotContain("Private other account",View().GetRawText());
    }

    [Fact]
    public async Task PhotoDerivativesRevalidatePrivatelyAndNeverBypassAuthorizationOrMissingOriginals()
    {
        if(!OperatingSystem.IsWindows())return;
        using var f=new PhotoCase();using var host=f.Host();using var client=host.Client();await Setup(host,client);
        var page=(await client.GetFromJsonAsync<ScreenshotPage>(PhotoPage))!;Assert.All(page.Photos,p=>Assert.True(p.ResponsiveThumbnails));
        foreach(var kind in new[]{"thumbnail","tile960","tile1280","preview"})
        {
            var url=PhotoPage+"/"+PhotoCase.Id("a.png")+"/"+kind;
            using var first=await client.GetAsync(url);Assert.Equal(HttpStatusCode.OK,first.StatusCode);Assert.NotNull(first.Headers.ETag);
            Assert.True(first.Headers.CacheControl!.Private);Assert.True(first.Headers.CacheControl.NoCache);Assert.False(first.Headers.CacheControl.NoStore);
            using var request=new HttpRequestMessage(HttpMethod.Get,url);request.Headers.IfNoneMatch.Add(first.Headers.ETag!);
            using var cached=await client.SendAsync(request);Assert.Equal(HttpStatusCode.NotModified,cached.StatusCode);Assert.Empty(await cached.Content.ReadAsByteArrayAsync());
            using var anonymous=host.Client();anonymous.DefaultRequestHeaders.IfNoneMatch.Add(first.Headers.ETag!);Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.GetAsync(url)).StatusCode);
        }
        var previewUrl=PhotoPage+"/"+PhotoCase.Id("a.png")+"/preview";using var preview=await client.GetAsync(previewUrl);
        File.Move(Path.Combine(f.Root,"a.png"),Path.Combine(f.Root,"a.removed"));
        client.DefaultRequestHeaders.IfNoneMatch.Add(preview.Headers.ETag!);var missing=await client.GetAsync(previewUrl);
        Assert.False(missing.IsSuccessStatusCode);Assert.NotEqual(HttpStatusCode.NotModified,missing.StatusCode);
    }

    [Fact]
    public async Task ShellFingerprintsCacheableAssetsAndLeavesMapsLazy()
    {
        using var host=new Host();using var client=host.Client();var html=await client.GetStringAsync("/");
        var asset=Regex.Match(html,"src=\"(/app.js\\?v=[a-f0-9]{16})\"");Assert.True(asset.Success);
        using var response=await client.GetAsync(asset.Groups[1].Value);Assert.True(response.Headers.CacheControl!.Public);Assert.False(response.Headers.CacheControl.NoStore);Assert.True(response.Headers.CacheControl.MaxAge>TimeSpan.FromDays(30));
        Assert.Contains("name=\"pixelvault-map-script\"",html);Assert.DoesNotContain("<script src=\"/vendor/leaflet",html);
        using var compressedRequest=new HttpRequestMessage(HttpMethod.Get,asset.Groups[1].Value);compressedRequest.Headers.AcceptEncoding.ParseAdd("gzip");
        using var compressed=await client.SendAsync(compressedRequest);Assert.Contains("gzip",compressed.Content.Headers.ContentEncoding);
    }
}
