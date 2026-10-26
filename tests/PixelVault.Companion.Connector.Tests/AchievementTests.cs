using System.Text.Json;
using Microsoft.Data.Sqlite;
using PixelVault.Companion;
using PixelVault.Companion.Connector;
using PixelVault.Companion.Export;
using Xunit;

public sealed class AchievementTests
{
    sealed class NativeFixture:IDisposable
    {
        public string Root=Path.Combine(Path.GetTempPath(),"CompanionAchievementTests",Guid.NewGuid().ToString("N"));
        public string Database=>Path.Combine(Root,"guides","pixelvault-guides.sqlite");
        public NativeFixture()
        {
            Directory.CreateDirectory(Path.Combine(Root,"guides"));Directory.CreateDirectory(Path.Combine(Root,"achievement-hub"));SQLitePCL.Batteries_V2.Init();
            using var c=new SqliteConnection($"Data Source={Database};Pooling=False");c.Open();using var cmd=c.CreateCommand();cmd.CommandText="""
            CREATE TABLE achievement_definition(achievement_id INTEGER PRIMARY KEY,provider TEXT,provider_game_id TEXT,provider_achievement_id TEXT,title TEXT,description TEXT,display_order INTEGER,is_hidden INTEGER,icon_url TEXT,group_name TEXT,dlc_name TEXT,group_sort_order INTEGER,is_active INTEGER);
            CREATE TABLE achievement_guide(achievement_id INTEGER PRIMARY KEY,guide_text TEXT,source_url TEXT,source_title TEXT,tags TEXT,is_missable INTEGER);
            CREATE TABLE achievement_category(name TEXT COLLATE NOCASE PRIMARY KEY);CREATE TABLE achievement_dlc(provider TEXT,provider_game_id TEXT,name TEXT,PRIMARY KEY(provider,provider_game_id,name));
            INSERT INTO achievement_definition VALUES(1,'steam','42','ONE','One','Description',0,0,'','Collectibles','Expansion',3,1);
            INSERT INTO achievement_definition VALUES(2,'steam','42','TWO','Two','Description',1,0,'','Actions','',4,1);
            INSERT INTO achievement_guide VALUES(1,'Original guide text','https://example.com/guide','Original source','Missable',1);
            INSERT INTO achievement_category VALUES('Collectibles');INSERT INTO achievement_category VALUES('Actions');INSERT INTO achievement_dlc VALUES('steam','42','Expansion');
            """;cmd.ExecuteNonQuery();
            File.WriteAllText(Path.Combine(Root,"achievement-hub","steam.json"),JsonSerializer.Serialize(new{Provider="steam",Account="approved",Games=new Dictionary<string,object>{{"42",new{UpdatedUtc=638900000000000000,Rows=new[]{new{Provider="steam",ProviderGameId="42",ProviderAchievementId="ONE",Title="Stale title",Description="Stale description",ProgressKnown=true,Unlocked=true,UnlockUtcTicks=638900000000000000,GroupName="Stale category",DlcName="",GroupSortOrder=0}}}}}}));
        }
        public void Dispose(){SqliteConnection.ClearAllPools();Directory.Delete(Root,true);}
    }
    [Fact]
    public void ExportReadsDurableGroupsAndGuidesInsteadOfStaleCacheWithoutWritingTheDesktopDatabase()
    {
        using var f=new NativeFixture();var before=File.ReadAllBytes(f.Database);var games=AchievementSnapshotExporter.Read(f.Root,"workspace",new(){["steam"]="approved"});var game=Assert.Single(games);var one=game.Rows.Single(r=>r.Id=="ONE");
        Assert.Equal("One",one.Title);Assert.Equal("Collectibles",one.Groups.Category);Assert.Equal("Expansion",one.Groups.Dlc);Assert.Equal("Original guide text",one.Guide);Assert.Equal("https://example.com/guide",one.SourceUrl);Assert.True(one.Unlocked);Assert.False(game.Rows.Single(r=>r.Id=="TWO").ProgressKnown);Assert.Equal(before,File.ReadAllBytes(f.Database));
        var wrong=AchievementSnapshotExporter.Read(f.Root,"workspace",new(){["steam"]="different-account"});Assert.False(wrong.Single().Rows.Single(r=>r.Id=="ONE").ProgressKnown);
    }
    [Fact]
    public void GroupWriteBackIsBackedUpIdempotentAndDoesNotChangeGuidesOrOverwriteConcurrentDesktopEdits()
    {
        using var f=new NativeFixture();var baseline=new AchievementGroups("Collectibles","Expansion",3);var desired=new AchievementGroups("Story","New expansion",3);var edit=new AchievementGroupChange("steam","42","ONE",Guid.NewGuid().ToString(),baseline,desired);
        var result=Assert.Single(DesktopAchievementGroups.Apply(f.Root,Path.Combine(f.Root,"private"),new(){["steam"]="approved"},[edit]));Assert.Equal("applied",result.Status);Assert.Single(Directory.GetFiles(Path.Combine(f.Root,"private","backups","achievement-groups"),"*.sqlite"));
        Assert.Equal("applied",DesktopAchievementGroups.Apply(f.Root,Path.Combine(f.Root,"private"),new(){["steam"]="approved"},[edit]).Single().Status);
        var conflicting=edit with{Id=Guid.NewGuid().ToString(),Desired=new("Difficulty","New expansion",3)};var refused=DesktopAchievementGroups.Apply(f.Root,Path.Combine(f.Root,"private"),new(){["steam"]="approved"},[conflicting]).Single();Assert.Equal("conflict",refused.Status);Assert.Equal(desired,refused.Desktop);
        var row=AchievementSnapshotExporter.Read(f.Root,"workspace",new(){["steam"]="approved"}).Single().Rows.Single(r=>r.Id=="ONE");Assert.Equal(desired,row.Groups);Assert.Equal("Original guide text",row.Guide);Assert.True(row.Unlocked);
        Assert.Throws<InvalidDataException>(()=>DesktopAchievementGroups.Apply(f.Root,Path.Combine(f.Root,"private"),new(){["steam"]="approved"},[edit with{Provider="retroachievements"}]));
    }
}
