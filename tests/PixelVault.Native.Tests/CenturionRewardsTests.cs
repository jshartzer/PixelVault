using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PixelVault.Tracking;
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public class CenturionRewardsTests
{
    [Fact] public void RailHidesUndefinedGamesAndSelectionKeepsFullDetailsAccess()=>AchievementCollectionAccessTests.OnSta(()=>
    {
        var missing=new GameProfileCenturyShelf("steam","42",()=>throw new Exception("Undefined games must not read account progress"),null,(_,__)=>{});
        Assert.Equal(Visibility.Collapsed,missing.Visibility);Assert.Empty(missing.Rows);
        string? opened=null;var shelf=new GameProfileCenturyShelf("steam","1850570",()=>"",null,(r,_)=>opened=r.CenturyDefinitionId);
        Assert.Equal(Visibility.Visible,shelf.Visibility);
        var buttons=Descendants(shelf).OfType<System.Windows.Controls.Button>().ToArray();
        var cargo=buttons.Single(b=>b.Tag is GameAchievementsFetchService.AchievementRow r&&r.CenturyDefinitionId==CenturyAchievementCatalog.DeathStrandingCargoId);
        cargo.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Assert.Equal(CenturyAchievementCatalog.DeathStrandingCargoId,shelf.SelectedId);shelf.Refresh();Assert.Equal(CenturyAchievementCatalog.DeathStrandingCargoId,shelf.SelectedId);
        buttons.Single(b=>b.Tag==null).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));Assert.Equal(shelf.SelectedId,opened);
        static IEnumerable<DependencyObject> Descendants(DependencyObject parent){for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var child=VisualTreeHelper.GetChild(parent,i);yield return child;foreach(var node in Descendants(child))yield return node;}}
    });
    static CenturionGame Snapshot(int mask,string scope="alice")=>CenturionRewards.Snapshot("steam","1850570",scope,id=>
    {
        var index=CenturyAchievementCatalog.ForGame("steam","1850570").ToList().FindIndex(d=>d.Id==id);
        return (mask&(1<<index))!=0?new CenturyProgress(1,1,10,20,30+index,"t","b","f","s","e"):null;
    });
    [Fact] public void EverySubsetRequiresAllFiveAndBadgeFollowsFinalAchievement()
    {
        for(var mask=0;mask<32;mask++)
        {
            var game=Snapshot(mask);Assert.Equal(mask==31,game.BadgeUnlocked);
            var ledger=new CenturionRewardLedger();Assert.Empty(ledger.Observe(new[]{game}));
            var reward=ledger.Observe(new[]{Snapshot(31)});
            if(mask==31)Assert.Empty(reward);
            else{Assert.True(reward.Last().Badge);Assert.All(reward.SkipLast(1),r=>Assert.False(r.Badge));}
            Assert.Empty(ledger.Observe(new[]{Snapshot(31)}));
            Assert.Empty(ledger.Observe(new[]{Snapshot(0)}));
            Assert.Empty(ledger.Observe(new[]{Snapshot(31)}));
            var reloaded=JsonSerializer.Deserialize<CenturionRewardLedger>(JsonSerializer.Serialize(ledger))!;
            Assert.Empty(reloaded.Observe(new[]{Snapshot(31)}));
            Assert.Empty(reloaded.Observe(new[]{Snapshot(31,"bob")}));
        }
        Assert.NotEmpty(CenturionRewards.BadgeArtwork("steam","1850570"));Assert.Null(CenturionRewards.BadgeArtwork("steam","42"));
        Assert.NotEqual(CenturionRewards.Scope("w","steam","1850570","alice"),CenturionRewards.Scope("w","steam","1850570","bob"));
    }
    [Fact] public void InterviewAwardRequiresCurrentApprovedCompleteCatalogue()
    {
        var root=Path.Combine(Path.GetTempPath(),"pv-centurion-test-"+Guid.NewGuid().ToString("N"));
        try
        {
            var store=new AchievementTrackingStore(root);var definition=DeathStrandingInterviewCatalogueReader.Definition();store.AddDefinition(definition);
            Assert.True(CenturyInterviewCatalogue.Matches(definition));
            const string account="76561198000000001";
            var binding=new TrackerSourceBinding{TrackerId=definition.Id,Account=account,AdapterId=DeathStrandingTrackerSource.AdapterId,AdapterVersion="1",Location=root};store.SaveSource(binding);
            void Apply(int count,long observed,bool bound=true)
            {
                var snapshot=new AchievementTrackerSnapshot{TrackerId=definition.Id,Provider="steam",GameId="1850570",Account=account,ObservedUtcTicks=observed,Source="Validated interview save",Evidence="Verified catalogue",
                    Items=definition.Items.Select((item,i)=>new AchievementTrackerProgress{ItemId=item.Id,Current=i<count?1:0}).ToList()};
                string? attempt=null;if(bound){attempt=store.StartRefresh(binding);store.FinishAttempt(binding,attempt,TrackerRefreshOutcome.AwaitingReview,"Ready");}
                var json=JsonSerializer.Serialize(snapshot,AchievementTrackingCatalog.Json);
                var hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json)));
                store.ApplyRefresh(store.Preview(json,definition.Id,account,bound?binding.AdapterId:"json-import","1",hash,bound?binding.Id:null,attempt),false,[],CancellationToken.None);
            }
            var date=DateTime.UtcNow.AddDays(-1).Ticks;
            foreach(var item in definition.Items)store.SetManualProgress(definition.Id,account,item.Id,1);
            Apply(149,date,false);Assert.Null(store.Century("steam","1850570",CenturyAchievementCatalog.DeathStrandingInterviewsId,account));
            Apply(148,date+1);Assert.False(store.Century("steam","1850570",CenturyAchievementCatalog.DeathStrandingInterviewsId,account)!.Unlocked);
            Apply(149,date+2);var progress=store.Century("steam","1850570",CenturyAchievementCatalog.DeathStrandingInterviewsId,account)!;
            Assert.True(progress.Unlocked);Assert.Equal(149,progress.Target);Assert.Equal(100,progress.EarnedPoints);
            Apply(120,date+3);Assert.Equal(progress.UnlockUtc,store.Century("steam","1850570",CenturyAchievementCatalog.DeathStrandingInterviewsId,account)!.UnlockUtc);
            Assert.Null(store.Century("steam","1850570",CenturyAchievementCatalog.DeathStrandingInterviewsId,"other"));
        }
        finally{Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();if(Directory.Exists(root))Directory.Delete(root,true);}
    }
    [Fact] public void NativeRewardAndShelfRenderAtDesktopAndNarrowWidths()=>AchievementCollectionAccessTests.OnSta(()=>
    {
        var evidence=Environment.GetEnvironmentVariable("PV_CENTURION_EVIDENCE");
        foreach(var badge in new[]{false,true})
        {
            var d=CenturyAchievementCatalog.All.First();var reward=new CenturionReward("a",d.Provider,d.GameId,d.GameTitle,badge?CenturionRewards.BadgeId:d.Id,badge?"Centurion":d.Title,badge?"All five Centurion achievements unlocked.":d.Description,badge?0:100,badge);
            var view=CenturionRewardWindow.Build(reward,false);Render(view,badge?460:550,badge?320:220,badge?"native-badge":"native-achievement");
        }
        foreach(var width in new[]{320,390,768,1100}){var shelf=new GameProfileCenturyShelf("steam","1850570",()=>"",null,(_,__)=>{});Render(shelf,width,400,"native-shelf-"+width);Assert.Equal(5,shelf.Rows.Count);}
        void Render(FrameworkElement element,int width,int height,string name)
        {
            element.Measure(new Size(width,element is GameProfileCenturyShelf?double.PositiveInfinity:height));if(element is GameProfileCenturyShelf)height=(int)Math.Ceiling(element.DesiredSize.Height);element.Arrange(new Rect(0,0,width,height));element.UpdateLayout();
            var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(element);
            if(!string.IsNullOrEmpty(evidence)){Directory.CreateDirectory(evidence);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(Path.Combine(evidence,name+".png"));encoder.Save(stream);}
        }
    });
}
