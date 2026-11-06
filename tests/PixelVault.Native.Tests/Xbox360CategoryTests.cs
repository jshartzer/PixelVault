using Microsoft.Data.Sqlite;
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class Xbox360CategoryTests
{
    static XboxAchievementService.XboxTitle Title(string id,string name,bool legacy=true)=>new(){Id=id,Name=name,Legacy=legacy};
    [Fact]
    public void CategoriesAndImporterPreserve360WithoutGenericXboxOrOtherFlags()
    {
        Assert.Equal("Xbox 360",MainWindow.NormalizeConsoleLabel("xbox360"));
        Assert.Equal("Xbox 360",MainWindow.DetermineConsoleLabelFromTags(new[]{"Xbox","Xbox 360"}));
        Assert.Equal("Xbox 360",MainWindow.DetermineConsoleLabelFromTags(new[]{"Xbox","Platform:Xbox360"}));
        Assert.Equal("Multiple Tags",MainWindow.DetermineConsoleLabelFromTags(new[]{"Steam","Xbox 360"}));
        var parsed=new FilenameParseResult{PlatformLabel="Xbox 360",PlatformTags=new[]{"Xbox 360"}};
        MainWindow.ApplyFilenameParseResultToManualPlatformFlags(parsed,out var steam,out var pc,out var emulation,out var ps5,out var nintendo,out var xbox,out var xbox360,out var other,out var custom);
        Assert.True(xbox360);Assert.False(steam||pc||emulation||ps5||nintendo||xbox||other);Assert.Empty(custom);
        Assert.Contains("Xbox 360",MainWindow.BuildFilenamePlatformHintTags(parsed));
        var item=new ManualMetadataItem{TagXbox=true,TagOther=true,CustomPlatformTag="Previous"};
        MainWindow.ApplyConsolePlatformToManualMetadataItems(new[]{item},"Xbox 360");
        Assert.True(item.TagXbox360);Assert.False(item.TagXbox||item.TagOther);Assert.Empty(item.CustomPlatformTag);Assert.True(item.ForceTagMetadataWrite);
        MainWindow.ApplyConsolePlatformToManualMetadataItems(new[]{item},"Steam");
        Assert.True(item.TagSteam);Assert.False(item.TagXbox360);
    }
    [Fact]
    public void Intake360RulePopulatesReviewAndManualEditModels()
    {
        var file=Path.GetTempFileName();
        try
        {
            var parsed=new FilenameParseResult{PlatformLabel="Xbox 360",PlatformTags=new[]{"Xbox 360"},GameTitleHint="Fable III",MatchedConvention=true};
            var result=IntakePreparationBuilder.Build(new[]{file},(_,_)=>new(){[file]=new(){FilePath=file,FileName=Path.GetFileName(file),CanUpdateMetadata=true,CaptureTime=DateTime.Now,Parsed=parsed}},true);
            Assert.True(Assert.Single(result.ReviewItems).TagXbox360);
            var item=Assert.Single(result.ImportEditItems);Assert.True(item.TagXbox360);Assert.True(item.OriginalTagXbox360);Assert.False(item.TagXbox||item.TagOther);
        }
        finally{File.Delete(file);}
    }
    [Fact]
    public void MatchingRequires360EvidenceAndRespectsExplicitLinksAndOtherPlatforms()
    {
        var titles=new[]{Title("1","Dragon Age: Origins™"),Title("2","Prey"),Title("3","Prey",false),Title("4","Fable III")};
        Assert.Equal("1",Xbox360LibraryMigration.Match("Dragon Age Origins","Xbox",titles)?.Id);
        Assert.Equal("4",Xbox360LibraryMigration.Match("Fable 3","Xbox",titles)?.Id);
        Assert.Null(Xbox360LibraryMigration.Match("Prey","Xbox",titles));
        Assert.Equal("2",Xbox360LibraryMigration.Match("My renamed game","Xbox",titles,"2")?.Id);
        Assert.Null(Xbox360LibraryMigration.Match("Prey","Xbox",titles,"3"));
        Assert.Null(Xbox360LibraryMigration.Match("Prey","Xbox",titles,"missing"));
        Assert.Null(Xbox360LibraryMigration.Match("Fable III","Steam",titles));
        Assert.Null(Xbox360LibraryMigration.Match("Fable III Remastered","Xbox",titles));
    }
    [Fact]
    public void MigrationBacksUpAndOnlyChangesConfirmedXboxCategoriesInTheActiveLibrary()
    {
        var root=Path.Combine(Path.GetTempPath(),"pv-360-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var database=Path.Combine(root,"index.sqlite");
            using var c=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=database,Pooling=false}.ToString());c.Open();
            using var q=c.CreateCommand();q.CommandText="""
                CREATE TABLE game_index(root TEXT,game_id TEXT,name TEXT,platform_label TEXT,note TEXT);
                INSERT INTO game_index VALUES ('active','a','Fable III','Xbox','keep'),('active','b','Fable III','Steam','steam'),('active','c','Prey','Xbox','ambiguous'),('old','d','Fable III','Xbox','old root');
                """;q.ExecuteNonQuery();
            var titles=new[]{Title("1","Fable III"),Title("2","Prey"),Title("3","Prey",false)};
            var changes=Xbox360LibraryMigration.ApplyDatabase(database,"active",titles,new Dictionary<string,GameProfilePlatformStore.Preference>(),Path.Combine(root,"backup"));
            Assert.Equal("a",Assert.Single(changes).GameId);
            q.CommandText="SELECT group_concat(game_id||':'||platform_label||':'||note,'|') FROM game_index ORDER BY game_id";
            Assert.Equal("a:Xbox 360:keep|b:Steam:steam|c:Xbox:ambiguous|d:Xbox:old root",q.ExecuteScalar());
            Assert.Empty(Xbox360LibraryMigration.ApplyDatabase(database,"active",titles,new Dictionary<string,GameProfilePlatformStore.Preference>(),Path.Combine(root,"backup")));
            using var backup=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=Assert.Single(Directory.GetFiles(Path.Combine(root,"backup"))),Mode=SqliteOpenMode.ReadOnly,Pooling=false}.ToString());backup.Open();
            using var original=backup.CreateCommand();original.CommandText="SELECT platform_label FROM game_index WHERE game_id='a'";Assert.Equal("Xbox",original.ExecuteScalar());
            var choice=Assert.Single(GameProfilePlatformStore.Choices("Fable III",Array.Empty<LibraryFolderInfo>(),titles,new()));
            Assert.Equal("Xbox 360",choice.Label);Assert.Equal("Xbox 360",choice.Folder.PlatformLabel);
            Assert.Equal("Xbox 360",new AchievementHubGame{Provider="xbox",XboxTitle=titles[0]}.PlatformLabel);
        }
        finally{Directory.Delete(root,true);}
    }
    [Fact]
    public void ModernMetadataPrevents360ClassificationEvenWhenOnlyTheOriginalWasPlayed()
    {
        var original=Title("1161889879","Dead Space");
        Assert.Null(Xbox360LibraryMigration.Match("Dead Space","Xbox",new[]{original},platforms:"Xbox Series X|S, PC (Microsoft Windows), PlayStation 5"));
        Assert.Null(Xbox360LibraryMigration.Match("Example","Xbox",new[]{Title("1","Example"),Title("2","Example")}));
        Assert.Equal(original.Id,Xbox360LibraryMigration.Match("Dead Space","Xbox",new[]{original},linkedId:original.Id,platforms:"Xbox Series X|S").Id);
    }

}
