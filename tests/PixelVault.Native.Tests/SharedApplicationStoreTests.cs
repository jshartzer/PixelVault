using Microsoft.Data.Sqlite;
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class SharedApplicationStoreTests
{
    [Fact]
    public void WebGroupEditRejectsStaleNativeGuideDraftWithoutSavingItsText()
    {
        var root=Path.Combine(Path.GetTempPath(),"SharedGuide-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var service=new AchievementGuideService(root);
            var row=new GameAchievementsFetchService.AchievementRow{Provider="steam",ProviderGameId="42",ProviderAchievementId="ONE",SteamApiName="ONE",Title="Title",Description="Description"};
            var entry=service.SyncDefinitionsAndLoadGuides("G1",[row]).Single();
            entry=service.SaveGuide(new(){AchievementId=entry.AchievementId,GuideText="Original text",GroupName="Story",ExpectedGroupVersion=entry.GroupVersion});
            using(var c=new SqliteConnection("Data Source="+service.DatabasePath+";Pooling=False"))
            {
                c.Open();using var tx=c.BeginTransaction();AchievementGroupCommands.Write(c,tx,entry.AchievementId,new("Collectibles","Expansion",2),entry.GroupVersion,Guid.NewGuid().ToString());tx.Commit();
            }
            Assert.Throws<InvalidOperationException>(()=>service.SaveGuide(new(){AchievementId=entry.AchievementId,GuideText="Stale unsaved draft",GroupName="Story",ExpectedGroupVersion=entry.GroupVersion}));
            var saved=service.LoadGuideEntry("steam","42","ONE");Assert.Equal("Original text",saved.GuideText);Assert.Equal("Collectibles",saved.GroupName);Assert.Equal("Expansion",saved.DlcName);
            Assert.NotEqual(entry.GroupVersion,saved.GroupVersion);
        }
        finally{SqliteConnection.ClearAllPools();Directory.Delete(root,true);}
    }

    [Fact]
    public async Task ProviderSummaryWritersPreserveOtherGamesAccountsAndNewerResponses()
    {
        var root=Path.Combine(Path.GetTempPath(),"SharedSummaries-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var first=new LocalProviderSummaryStore(root);var second=new LocalProviderSummaryStore(root);
            await Task.WhenAll(Task.Run(()=>first.Save(new("steam","42","owner",120,null,2))),Task.Run(()=>second.Save(new("steam","43","owner",30,null,2))),Task.Run(()=>second.Save(new("steam","42","other-account",999,null,2))));
            first.Save(new("steam","42","owner",1,null,1));
            var saved=second.Read();Assert.Equal(3,saved.Count);Assert.Equal(120,saved.Single(s=>s.GameId=="42"&&s.Account=="owner").PlaytimeMinutes);Assert.Equal(999,saved.Single(s=>s.Account=="other-account").PlaytimeMinutes);
        }
        finally{Directory.Delete(root,true);}
    }
}
