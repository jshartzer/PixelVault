using PixelVault.Companion;
using PixelVault.Companion.Connector;
using PixelVaultNative;
using Xunit;

public sealed class TrackerPublicationTests
{
    [Fact]
    public void ContentSurvivesReadOnlyDiscoveryAndRemainsPartOfStructuralSourceValidation()
    {
        var root=Path.Combine(Path.GetTempPath(),"CompanionPublicationTests",Guid.NewGuid().ToString("N"));
        try
        {
            var repo=new TrackerRepository(root,pooling:false);
            var d=new AchievementTrackerDefinition{Id="custom.content",Provider="steam",GameId="42",GameTitle="Journey",Title="Interviews",Items=new(){new(){Id="uuid",Name="Interview",Content="Paragraph one.\n\nLiteral <tag> & text."}}};repo.AddDefinition(d);
            var before=File.ReadAllBytes(repo.DatabasePath);
            var discovered=Assert.Single(DesktopTrackerPublication.Read(root,new(){["steam"]="approved"},[],(_,_)=>{}));
            Assert.Equal(d.Items[0].Content,discovered.Definition.Items[0].Content);Assert.Equal(before,File.ReadAllBytes(repo.DatabasePath));
            discovered.Definition.Title="Different display title";Assert.True(TrackerDefinitionComparison.SameStructure(d,discovered.Definition));
            discovered.Definition.Items[0].Content="Changed content";Assert.False(TrackerDefinitionComparison.SameStructure(d,discovered.Definition));
            d.Items[0].Content=null;
            Assert.DoesNotContain("\"Content\"",System.Text.Json.JsonSerializer.Serialize(d,AchievementTrackingCatalog.Json));
        }
        finally{Directory.Delete(root,true);}
    }
    [Fact]
    public void TitlesReadBundledOverridesAndPortableCustomNamesWithoutWritingEvenOnLegacyDatabases()
    {
        var root=Path.Combine(Path.GetTempPath(),"CompanionPublicationTests",Guid.NewGuid().ToString("N"));
        try
        {
            var repo=new TrackerRepository(root,pooling:false);
            var custom=new AchievementTrackerDefinition{Id="custom.rename",Provider="steam",GameId="42",GameTitle="Journey",Title="Original",Items=new(){new(){Id="count",Name="Count"}}};repo.AddDefinition(custom);
            var builtin=AchievementTrackingCatalog.Builtins().First(d=>d.Provider=="steam");repo.RenameTracker(builtin.Id,"Bundled chosen name");repo.RenameTracker(custom.Id,"Road Building");
            var before=File.ReadAllBytes(repo.DatabasePath);var titles=DesktopTrackerPublication.ReadTitles(root,new(){["steam"]="approved"});
            Assert.Equal("Bundled chosen name",titles.Single(t=>t.Id==builtin.Id).Title);Assert.Equal("Road Building",titles.Single(t=>t.Id==custom.Id).Title);
            Assert.All(titles,t=>Assert.Equal("steam",t.Provider));Assert.Equal(before,File.ReadAllBytes(repo.DatabasePath));
            using(var c=new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder{DataSource=repo.DatabasePath,Pooling=false}.ToString()))
            {c.Open();using var cmd=c.CreateCommand();cmd.CommandText="DROP TABLE tracker_title";cmd.ExecuteNonQuery();}
            var legacy=DesktopTrackerPublication.ReadTitles(root,new(){["steam"]="approved"});
            Assert.Equal(builtin.Title,legacy.Single(t=>t.Id==builtin.Id).Title);Assert.Equal("Road Building",legacy.Single(t=>t.Id==custom.Id).Title);
            var changed=System.Text.Json.JsonSerializer.Deserialize<AchievementTrackerDefinition>(System.Text.Json.JsonSerializer.Serialize(custom))!;changed.Title="New";
            Assert.True(TrackerDefinitionComparison.SameStructure(custom,changed));changed.Items[0].Target=99;Assert.False(TrackerDefinitionComparison.SameStructure(custom,changed));
        }
        finally{Directory.Delete(root,true);}
    }
    [Fact]
    public void DesktopDiscoveryReadsOnlyApprovedDefinitionsAndInheritsOnlyTheExactPriorPermission()
    {
        var root=Path.Combine(Path.GetTempPath(),"CompanionPublicationTests",Guid.NewGuid().ToString("N"));
        try
        {
            var repo=new TrackerRepository(root,pooling:false);
            var d=new AchievementTrackerDefinition{Id="custom.new",Provider="steam",GameId="42",GameTitle="Journey",Title="New tracker",Items=new(){new(){Id="count",Name="Counter",Target=100}}};
            repo.AddDefinition(d);repo.SaveSource(new(){TrackerId=d.Id,Account="approved",AdapterId="death-stranding-dc.fabrication",AdapterVersion="1",Location=@"C:\save",GameDirectory=@"C:\game",KeepSnapshots=false});
            var binding=repo.Source(d.Id,"approved");var allowed=new ConnectorSource(binding,d,"revision");
            binding.KeepSnapshots=true;
            var before=File.ReadAllBytes(repo.DatabasePath);bool validated=false;
            var read=DesktopTrackerPublication.Read(root,new(){["steam"]="approved"},[allowed],(_,_)=>validated=true);
            Assert.Single(read);Assert.NotNull(read[0].Binding);Assert.True(validated);Assert.Equal(before,File.ReadAllBytes(repo.DatabasePath));
            binding.Location=@"C:\different";
            Assert.Null(DesktopTrackerPublication.Read(root,new(){["steam"]="approved"},[allowed],(_,_)=>{})[0].Binding);
            Assert.Empty(DesktopTrackerPublication.Read(root,new(){["retroachievements"]="approved"},[allowed],(_,_)=>{}));
        }
        finally{Directory.Delete(root,true);}
    }
}
