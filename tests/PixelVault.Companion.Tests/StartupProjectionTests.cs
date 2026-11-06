using System.Text.Json;
using Microsoft.Data.Sqlite;
using PixelVaultNative;
using Xunit;

namespace PixelVault.Companion.Tests;

public sealed partial class CompanionTests
{
    [Fact]
    public async Task LibraryUsesSavedPhotoCountsAndRefreshPublishesNewCounts()
    {
        if(!OperatingSystem.IsWindows())return;
        using var f=new PhotoCase();var options=f.Options;_=new OwnerStore(options);using var workspace=new TrackerWorkspace(options);
        var state=new ConnectorSyncState(options,workspace);var library=new GameLibrary(options,workspace,new AchievementStore(options,workspace,state));
        var identities=Path.Combine(f.Data.App,"local-photos","identities.sqlite");var before=File.ReadAllBytes(identities);
        Assert.Equal(2,library.Games().Single(g=>g.GameId=="42").Screenshots!.Count);
        Assert.Equal(before,File.ReadAllBytes(identities));
        _=workspace.LocalPhotos!.Catalogues();f.Capture("new.png");await Task.Delay(1100);_=workspace.LocalPhotos.Catalogues();await Task.Delay(1100);_=workspace.LocalPhotos.Catalogues();
        Assert.Equal(3,library.Games().Single(g=>g.GameId=="42").Screenshots!.Count);
    }

    [Fact]
    public void ProjectionSeesCommittedWalChangesWhileTheNativeConnectionStaysOpen()
    {
        using var f=new DirectCase();using var writer=new SqliteConnection("Data Source="+f.Guide+";Pooling=False");writer.Open();
        using var command=writer.CreateCommand();command.CommandText="UPDATE achievement_guide SET guide_text='First committed guide' WHERE achievement_id=1";command.ExecuteNonQuery();
        command.CommandText="PRAGMA wal_checkpoint(RESTART)";command.ExecuteNonQuery();
        using var workspace=new TrackerWorkspace(new(){DataRoot=f.State,ApplicationDataRoot=f.App,CacheRoot=f.Cache,StorageMode="direct-local"});var local=workspace.LocalData!;
        Assert.Equal("First committed guide",local.Game("steam","42").Rows.Single().Guide);
        var wal=f.Guide+"-wal";var guideTime=File.GetLastWriteTimeUtc(f.Guide);var walTime=File.GetLastWriteTimeUtc(wal);var walLength=new FileInfo(wal).Length;
        command.CommandText="UPDATE achievement_guide SET guide_text='Later committed guide' WHERE achievement_id=1";command.ExecuteNonQuery();
        File.SetLastWriteTimeUtc(f.Guide,guideTime);File.SetLastWriteTimeUtc(wal,walTime);Assert.Equal(walLength,new FileInfo(wal).Length);
        Assert.Equal("Later committed guide",local.Game("steam","42").Rows.Single().Guide);
    }

    [Fact]
    public async Task UnchangedProjectionSurvivesPollingButNativeWritesAndAccountChangesInvalidateIt()
    {
        using var f=new DirectCase();using var workspace=new TrackerWorkspace(new(){DataRoot=f.State,ApplicationDataRoot=f.App,CacheRoot=f.Cache,StorageMode="direct-local"});
        var local=workspace.LocalData!;var before=local.Game("steam","42");
        await Task.Delay(1200);
        Assert.Same(before,local.Game("steam","42"));
        using(var c=new SqliteConnection("Data Source="+f.Guide+";Pooling=False"))
        {c.Open();using var q=c.CreateCommand();q.CommandText="UPDATE achievement_guide SET guide_text='Native changed this guide' WHERE achievement_id=1";q.ExecuteNonQuery();}
        Assert.Equal("Native changed this guide",local.Game("steam","42").Rows.Single().Guide);
        var settings=Path.Combine(f.App,"PixelVault.settings.ini");File.WriteAllText(settings,File.ReadAllText(settings).Replace(Approved,Other));
        Assert.Throws<InvalidDataException>(()=>local.Games());
    }

    [Fact]
    public void ConnectionStatusDoesNotOpenPhotoInventoryAndUsesCurrentLibraryTitles()
    {
        if(!OperatingSystem.IsWindows())return;
        using var f=new PhotoCase();var options=f.Options;options.LocalWorkersEnabled=true;
        _=new OwnerStore(options);using var workspace=new TrackerWorkspace(options);
        workspace.Repository.SaveSource(new(){TrackerId=TrackerId,Account=Approved,AdapterId="pixelvault.json-file",AdapterVersion="1",Location=Path.Combine(f.Data.App,"approved.json")});
        workspace.Repository.SaveSource(new(){TrackerId=TrackerId,Account=Other,AdapterId="pixelvault.json-file",AdapterVersion="1",Location=Path.Combine(f.Data.App,"other-account.json")});
        Assert.Equal(Approved,Assert.Single(workspace.Repository.SourcesForAccounts(workspace.Manifest!.Accounts)).Binding.Account);
        var state=new ConnectorSyncState(options,workspace);var library=new GameLibrary(options,workspace,new AchievementStore(options,workspace,state));
        var refresh=new LocalRefreshService(options,workspace,library,state,new FakeLocalReader());
        var identities=Path.Combine(f.Data.App,"local-photos","identities.sqlite");File.Move(identities,identities+".unavailable");
        try
        {
            using var first=JsonDocument.Parse(JsonSerializer.Serialize(refresh.Connection()));
            Assert.Contains(GameLibrary.GroupKey("Test journey"),first.RootElement.GetProperty("supported").EnumerateArray().Select(x=>x.GetString()));
            f.Sql("UPDATE game_index SET name='Renamed journey'");
            using var next=JsonDocument.Parse(JsonSerializer.Serialize(refresh.Connection()));
            Assert.Contains(GameLibrary.GroupKey("Renamed journey"),next.RootElement.GetProperty("supported").EnumerateArray().Select(x=>x.GetString()));
            workspace.Repository.RemoveSource(TrackerId,Approved);
            using var removed=JsonDocument.Parse(JsonSerializer.Serialize(refresh.Connection()));Assert.Empty(removed.RootElement.GetProperty("supported").EnumerateArray());
        }
        finally{File.Move(identities+".unavailable",identities);}
    }

    [Fact]
    public void BatchedGroupReadMatchesDetailVersionsAndSeesLegacyNativeEdits()
    {
        using var f=new DirectCase();using var workspace=new TrackerWorkspace(new(){DataRoot=f.State,ApplicationDataRoot=f.App,CacheRoot=f.Cache,StorageMode="direct-local"});var local=workspace.LocalData!;
        Assert.Equal(local.GroupState("steam","42","ONE"),local.GroupStates("steam","42")["ONE"]);
        using(var c=new SqliteConnection("Data Source="+f.Guide+";Pooling=False"))
        {c.Open();using var q=c.CreateCommand();q.CommandText="UPDATE achievement_definition SET group_name='Actions' WHERE achievement_id=1";q.ExecuteNonQuery();}
        var after=local.GroupStates("steam","42")["ONE"];Assert.Equal("Actions",after.Groups.Category);Assert.Equal(local.GroupState("steam","42","ONE"),after);
        Assert.Throws<KeyNotFoundException>(()=>local.GroupStates("unapproved","42"));
    }
}
