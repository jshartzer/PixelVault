using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PixelVaultNative;
using PixelVault.Tracking;
using Xunit;
using static PixelVaultNative.Tests.AchievementCollectionAccessTests;

namespace PixelVaultNative.Tests;

public sealed class CompletionTreatmentTests
{
    [Theory]
    [InlineData(false,false)]
    [InlineData(true,false)]
    [InlineData(false,true)]
    public void CoverBordersRemainVisibleAcrossResizesAndRepeatedRedraws(bool emerald,bool diamond)=>OnSta(()=>
    {
        var frame=new CompletionFrameElement(new CornerRadius(10),emerald,diamond);
        var scene=new Canvas();scene.Children.Add(frame);Canvas.SetLeft(frame,10);Canvas.SetTop(frame,10);
        foreach(var size in new[]{new Size(920,600),new Size(420,900),new Size(620,550),new Size(920,600)})
        {
            frame.Width=size.Width;frame.Height=size.Height;
            scene.Width=size.Width+20;scene.Height=size.Height+40;
            scene.Measure(new Size(scene.Width,scene.Height));scene.Arrange(new Rect(0,0,scene.Width,scene.Height));scene.UpdateLayout();
            byte[]? previous=null;
            for(var redraw=0;redraw<4;redraw++)
            {
                frame.InvalidateVisual();
                var bitmap=new RenderTargetBitmap((int)scene.Width,(int)scene.Height,96,96,PixelFormats.Pbgra32);bitmap.Render(scene);
                var pixels=new byte[bitmap.PixelWidth*bitmap.PixelHeight*4];bitmap.CopyPixels(pixels,bitmap.PixelWidth*4,0);
                Assert.NotEqual(0,pixels[(100*bitmap.PixelWidth+10)*4+3]);
                Assert.NotEqual(0,pixels[(100*bitmap.PixelWidth+(int)size.Width+9)*4+3]);
                var scale=Math.Clamp(size.Width/220,2d/3,2);
                Assert.NotEqual(0,pixels[((int)(size.Height+10-27*scale)*bitmap.PixelWidth+(int)(size.Width+10-27*scale))*4+3]);
                if(previous!=null)Assert.Equal(previous,pixels);
                previous=pixels;
            }
        }
    });

    [Fact]
    public void CompletionRequiresKnownBaseProgressAndActualDlcForEmerald()
    {
        Assert.Equal(AchievementCompletionTier.None,AchievementCompletion.Classify([]));
        Assert.Equal(AchievementCompletionTier.Base,AchievementCompletion.Classify([],true));
        Assert.Equal(AchievementCompletionTier.None,AchievementCompletion.Classify([new(false,true,false)]));
        Assert.Equal(AchievementCompletionTier.None,AchievementCompletion.Classify([new(true,false,false)],true));
        Assert.Equal(AchievementCompletionTier.None,AchievementCompletion.Classify([new(false,true,false)],true));
        Assert.Equal(AchievementCompletionTier.None,AchievementCompletion.Classify([new(true,true,true)],true));
        Assert.Equal(AchievementCompletionTier.None,AchievementCompletion.Classify([new(true,true,true)]));
        Assert.Equal(AchievementCompletionTier.Base,AchievementCompletion.Classify([new(true,true,false)]));
        Assert.Equal(AchievementCompletionTier.Base,AchievementCompletion.Classify([new(true,true,false),new(false,true,true)]));
        Assert.Equal(AchievementCompletionTier.Base,AchievementCompletion.Classify([new(true,true,false),new(true,false,true)]));
        Assert.Equal(AchievementCompletionTier.BaseAndDlc,AchievementCompletion.Classify([new(true,true,false),new(true,true,true)]));
    }

    [Theory]
    [InlineData("1850570",63,44,0,0)]
    [InlineData("3035570",50,48,11,5)]
    public void SavedCompleteFlagsDoNotOverrideIncompleteCategorizedBaseAchievements(string game,int baseTotal,int baseEarned,int dlcTotal,int dlcEarned)
    {
        var rows=Enumerable.Range(0,baseTotal+dlcTotal).Select(i=>new GameAchievementsFetchService.AchievementRow
        {
            Provider="steam",ProviderGameId=game,ProviderAchievementId="a"+i,
            ProgressKnown=true,Unlocked=i<baseTotal?i<baseEarned:i-baseTotal<dlcEarned,
            GroupName=i<baseEarned?"Base Game":"Progression",DlcName=i>=baseTotal?"Expansion":""
        }).ToList();
        Assert.Equal(AchievementCompletionTier.None,MainWindow.ClassifyLibraryAchievementCompletion(rows,true));
        Assert.False(MainWindow.LibraryGameProfileAchievementsAreFullyUnlocked(rows));
        var indexed=new GameIndexEditorRow{GameId="game",SteamAppId=game,PlatformLabel="Steam",IsCompleted100Percent=true,CompletedUtcTicks=123,IsFavorite=true,CollectionNotes="Keep this note"};
        Assert.Same(indexed,Assert.Single(MainWindow.MarkCompletedAchievementGames(new(){Rows=rows},new[]{indexed})));
        Assert.False(indexed.IsCompleted100Percent);Assert.Equal(0,indexed.CompletedUtcTicks);
        Assert.True(indexed.IsFavorite);Assert.Equal("Keep this note",indexed.CollectionNotes);
        Assert.Empty(MainWindow.MarkCompletedAchievementGames(new(){Rows=rows},new[]{indexed}));
    }

    [Theory]
    [InlineData(false,false)]
    [InlineData(false,true)]
    [InlineData(true,false)]
    public void MissingOrUnknownBaseProgressCannotRevokeTheIndexFlag(bool dlcOnly,bool empty)
    {
        var indexed=new GameIndexEditorRow{SteamAppId="42",PlatformLabel="Steam",IsCompleted100Percent=true,CompletedUtcTicks=123};
        var row=new GameAchievementsFetchService.AchievementRow{Provider="steam",ProviderGameId="42",ProviderAchievementId="a",ProgressKnown=dlcOnly,Unlocked=false,DlcName=dlcOnly?"Expansion":""};
        Assert.Empty(MainWindow.MarkCompletedAchievementGames(new(){Rows=empty?new():new(){row}},new[]{indexed}));
        Assert.True(indexed.IsCompleted100Percent);Assert.Equal(123,indexed.CompletedUtcTicks);
    }

    [Fact]
    public void CategoriesDoNotCountAsDlcAndDlcCannotHideLockedBaseProgress()
    {
        var rows=new[]{new GameAchievementsFetchService.AchievementRow{GroupName="Story",ProgressKnown=true,Unlocked=true},
            new GameAchievementsFetchService.AchievementRow{GroupName="Actions",DlcName="Expansion",ProgressKnown=true,Unlocked=true}};
        Assert.Equal(AchievementCompletionTier.BaseAndDlc,MainWindow.ClassifyLibraryAchievementCompletion(rows));
        rows[0].Unlocked=false;
        Assert.Equal(AchievementCompletionTier.None,MainWindow.ClassifyLibraryAchievementCompletion(rows));
        rows[0].Unlocked=true;rows[1].DlcName="";
        Assert.Equal(AchievementCompletionTier.Base,MainWindow.ClassifyLibraryAchievementCompletion(rows));
    }

    [Theory]
    [InlineData(false,false)]
    [InlineData(true,false)]
    [InlineData(false,true)]
    public void HeritageFrameLeavesCoverClearAndTucksClaspIntoCorner(bool emerald,bool diamond)=>OnSta(()=>
    {
        var tier=diamond?AchievementCompletionTier.BaseAndDlcAndPv:emerald?AchievementCompletionTier.BaseAndDlc:AchievementCompletionTier.Base;
        var overlay=MainWindow.BuildLibraryTileCompletionFrameOverlay(210,315,new CornerRadius(12),tier);
        Assert.Null(overlay.Effect);Assert.False(overlay.ClipToBounds);Assert.False(overlay.IsHitTestVisible);
        var container=new Grid{Width=210,Height=315,ClipToBounds=false};
        container.Children.Add(new Button{Width=210,Height=315,Template=LibraryButtonChrome.BuildRoundedTileButtonTemplate(),Content=new Grid{Width=210,Height=315}});
        container.Children.Add(overlay);
        var scene=new Canvas{Width=230,Height=345};Canvas.SetLeft(container,10);Canvas.SetTop(container,10);scene.Children.Add(container);
        scene.Measure(new Size(230,345));scene.Arrange(new Rect(0,0,230,345));scene.UpdateLayout();
        var bitmap=new RenderTargetBitmap(230,345,96,96,PixelFormats.Pbgra32);bitmap.Render(scene);
        var bytes=new byte[230*345*4];bitmap.CopyPixels(bytes,230*4,0);
        // The center remains untouched, and there is no oversized plinth below the card.
        Assert.Equal((byte)0x24,bytes[(150*230+115)*4]);
        Assert.Equal((byte)0x1e,bytes[(150*230+115)*4+1]);
        Assert.Equal((byte)0x15,bytes[(150*230+115)*4+2]);
        Assert.False(Enumerable.Range(330*230,8*230).Any(i=>bytes[i*4+3]>0),"Ornaments must stay close to the perimeter.");
        var drawing=MainWindow.BuildLibraryCompletionFrameDrawing(210,315,new CornerRadius(12),emerald,1,diamond);
        var frameOnly=new DrawingVisual();using(var dc=frameOnly.RenderOpen())dc.DrawDrawing(drawing);
        var mask=new RenderTargetBitmap(210,315,96,96,PixelFormats.Pbgra32);mask.Render(frameOnly);
        var alpha=new byte[210*315*4];mask.CopyPixels(alpha,210*4,0);
        // No ornamental intrusion into the central cover, including the old scroll region.
        for(var y=24;y<250;y++)for(var x=24;x<186;x++)Assert.Equal(0,alpha[(y*210+x)*4+3]);
        Assert.True(alpha[(289*210+184)*4+3]>0,"The clasp remains visible in the lower-right corner.");
        var output=Environment.GetEnvironmentVariable("PIXELVAULT_COMPLETION_REVIEW_OUTPUT");
        if(!string.IsNullOrEmpty(output))
        {
            System.IO.Directory.CreateDirectory(output);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file=System.IO.File.Create(System.IO.Path.Combine(output,diamond?"native-diamond.png":emerald?"native-emerald.png":"native-gold.png"));encoder.Save(file);
        }
    });
}
