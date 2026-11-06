using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using PixelVault.Tracking;
using PixelVaultNative;
using Xunit;
using static PixelVaultNative.Tests.AchievementCollectionAccessTests;

namespace PixelVaultNative.Tests;

public sealed class CompletionShowcaseTests
{
    static IEnumerable<DependencyObject> Children(DependencyObject parent)
    {
        foreach(var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {yield return child;foreach(var nested in Children(child))yield return nested;}
    }
    [Fact]
    public void RealCompletedGamesUseCasesAndExactCoverSizes()=>OnSta(()=>
    {
        var widths=new List<double>();CompletionShowcaseEntry? opened=null;
        var window=new CompletionShowcaseWindow((entry,width)=>{widths.Add(width);return new Border{Width=width,Height=width*1.5,Background=Brushes.DarkSlateBlue};},(_,entry)=>opened=entry);
        var entries=new[]{new CompletionShowcaseEntry("1","Incomplete","Steam","",AchievementCompletionTier.None,1,10),
            new CompletionShowcaseEntry("2","Gold game","Steam","",AchievementCompletionTier.Base,8,10),
            new CompletionShowcaseEntry("3","Emerald game","Steam","",AchievementCompletionTier.BaseAndDlc,20,20)};
        try
        {
            Assert.Equal(Math.Min(1779,SystemParameters.WorkArea.Width),window.Width);
            Assert.Equal(Math.Min(1249,SystemParameters.WorkArea.Height),window.Height);
            window.SetEntries(entries);
            var size=Children((DependencyObject)window.Content).OfType<ComboBox>().Single(c=>AutomationProperties.GetName(c)=="Cover size");
            Assert.Equal(2,size.SelectedIndex);
            foreach(var index in new[]{0,1,2})
            {
                widths.Clear();size.SelectedIndex=index;
                Assert.Equal(2,widths.Count);Assert.All(widths,w=>Assert.Equal(CompletionShowcaseWindow.CoverWidths[index],w));
                var text=Children((DependencyObject)window.Content).OfType<TextBlock>().Select(t=>t.Text).ToList();
                Assert.Contains("8 / 10 achievements",text);Assert.Contains("20 / 20 achievements",text);Assert.DoesNotContain("Incomplete",text);
            }
            var root=(FrameworkElement)window.Content;root.Measure(new Size(1484,1107));root.Arrange(new Rect(0,0,1484,1107));root.UpdateLayout();
            window.Dispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.Render);
            var cases=Children((DependencyObject)window.Content).OfType<CompletionCasePanel>().ToArray();
            Assert.Equal(3,cases.Length);
            Assert.All(cases.SelectMany(c=>c.Children.Cast<UIElement>()),item=>
            {
                var finish=Assert.IsType<HolofoilCover>(item);
                Assert.Null(finish.Clip); // The rounded source must not crop the projected cover.
                Assert.IsType<Border>(finish.ReflectionVisual); // Reflections exclude hover captions.
            });
            Assert.Contains("Diamond",AutomationProperties.GetName(cases[2]));
            Assert.Contains("Gold",AutomationProperties.GetName(cases[0]));
            Assert.Contains("Emerald",AutomationProperties.GetName(cases[1]));
            Assert.Empty(Children((DependencyObject)window.Content).OfType<CompletionFrameElement>());
            window.SetEntries(entries.Select(entry=>entry with{}).ToArray());
            Assert.Equal(cases,Children((DependencyObject)window.Content).OfType<CompletionCasePanel>().ToArray());
            var card=Children((DependencyObject)window.Content).OfType<Grid>().Single(c=>AutomationProperties.GetName(c).StartsWith("Gold game,"));
            var caption=Assert.Single(card.Children.OfType<Border>(),b=>b.Child is StackPanel);
            var roundedClip=Assert.IsType<RectangleGeometry>(card.Clip);Assert.Equal(12,roundedClip.RadiusX);Assert.Equal(12,roundedClip.RadiusY);
            Assert.Equal(0,caption.Opacity);Assert.Equal(300,card.Height);
            card.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,0){RoutedEvent=Mouse.MouseEnterEvent});Assert.Equal(1,caption.Opacity);
            card.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,0){RoutedEvent=Mouse.MouseLeaveEvent});Assert.Equal(0,caption.Opacity);
            using var source=new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("Completion collection test"){Width=1,Height=1,WindowStyle=0});
            card.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,source,0,Key.Enter){RoutedEvent=Keyboard.KeyDownEvent});
            Assert.Same(entries[1],opened);Assert.Equal(AchievementCompletionTier.None,entries[0].Tier);
        }
        finally{window.Close();}
    });
    [Fact]
    public void EmptyLibraryHasNoSyntheticAwards()=>OnSta(()=>
    {
        var calls=0;var window=new CompletionShowcaseWindow((entry,width)=>{calls++;return new Border();});
        try{window.SetEntries(Array.Empty<CompletionShowcaseEntry>());Assert.Equal(0,calls);}
        finally{window.Close();}
    });
    [Fact]
    public void CaseWrappingAddsFloorsWhileKeepingTheSameCabinetAndCards()=>OnSta(()=>
    {
        var panel=new CompletionCasePanel(false,200);
        for(var i=0;i<14;i++)panel.Children.Add(new Border { Width=200,Height=300,Background=Brushes.DarkSlateGray });
        void Layout(double width)
        {
            panel.Measure(new Size(width,double.PositiveInfinity));panel.Arrange(new Rect(0,0,width,panel.DesiredSize.Height));panel.UpdateLayout();
            var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)panel.ActualWidth,(int)panel.ActualHeight,96,96,PixelFormats.Pbgra32);
            bitmap.Render(panel);
            var pixel=new byte[4];bitmap.CopyPixels(new Int32Rect(20,(int)panel.ActualHeight-42,1,1),pixel,4,0);
            Assert.NotEqual(0,pixel[3]);
            bitmap.CopyPixels(new Int32Rect(0,(int)(98+panel.ShelfPitch-7),1,1),pixel,4,0);
            Assert.Equal(0,pixel[3]); // Shelf ends stay inside the continuous perimeter.
            for(var row=0;row<panel.ShelfCount;row++)
            {
                var card=panel.Children[row*panel.Columns];var offset=VisualTreeHelper.GetOffset(card);var interior=panel.ShelfInterior(row,width);
                Assert.Equal(44,offset.X);
                Assert.InRange(Math.Abs((offset.Y-interior.Top)-(interior.Bottom-offset.Y-card.RenderSize.Height)),0,1);
            }
            var bounds=panel.Children.Cast<UIElement>().Select(c=>new Rect(VisualTreeHelper.GetOffset(c).X,VisualTreeHelper.GetOffset(c).Y,c.RenderSize.Width,c.RenderSize.Height)).ToArray();
            for(var i=0;i<bounds.Length;i++)for(var j=i+1;j<bounds.Length;j++)Assert.False(bounds[i].IntersectsWith(bounds[j]),"Desktop covers must occupy separate shelf positions.");
            for(var i=0;i<bounds.Length;i++)Assert.True(bounds[i].Bottom<panel.ActualHeight,"Desktop covers must fit within the cabinet.");
        }
        Layout(1400);Assert.Equal(6,panel.Columns);Assert.Equal(3,panel.ShelfCount);var originalHeight=panel.ActualHeight;
        var originalCards=panel.Children.Cast<UIElement>().ToArray();
        Layout(680);Assert.Equal(2,panel.Columns);Assert.Equal(7,panel.ShelfCount);Assert.True(panel.ActualHeight>originalHeight);
        Assert.Equal(originalCards,panel.Children.Cast<UIElement>().ToArray());
        Layout(1400);Assert.Equal(3,panel.ShelfCount);Assert.Equal(originalHeight,panel.ActualHeight);
    });

    [Theory]
    [InlineData(400)]
    [InlineData(680)]
    [InlineData(1400)]
    public void EmeraldCrownFitsInsideTheDrawingAndRetainsItsTip(double width)=>OnSta(()=>
    {
        var panel=new CompletionCasePanel(true,120);
        panel.Children.Add(new Border{Width=120,Height=180,Background=Brushes.DarkSlateBlue});
        panel.Measure(new Size(width,double.PositiveInfinity));panel.Arrange(new Rect(0,0,width,panel.DesiredSize.Height));panel.UpdateLayout();
        byte[] Capture()
        {
            var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)width,(int)panel.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(panel);
            var pixels=new byte[bitmap.PixelWidth*bitmap.PixelHeight*4];bitmap.CopyPixels(pixels,bitmap.PixelWidth*4,0);return pixels;
        }
        var first=Capture();Assert.True(panel.OrnamentBounds.Top>=1,"Reserve actual drawing space above the emerald gem and bezel.");
        Assert.True(panel.OrnamentBounds.Bottom<=panel.ActualHeight);
        var hasCrown=false;for(var y=8;y<22;y++)for(var x=(int)width/2-5;x<(int)width/2+5;x++)if(first[(y*(int)width+x)*4+3]>0)hasCrown=true;
        Assert.True(hasCrown,"The upper crown tip must be rendered, not clipped off.");
        for(var redraw=0;redraw<3;redraw++){panel.InvalidateVisual();Assert.Equal(first,Capture());}
    });

    [Fact]
    public void CaseCornerOrnamentsPaintInFrontOfOverlappingArtwork()=>OnSta(()=>
    {
        var panel=new CompletionCasePanel(true,200);
        for(var i=0;i<4;i++)panel.Children.Add(new Border { Width=200,Height=300,Background=Brushes.White,RenderTransform=new TranslateTransform(-20,20) });
        panel.Measure(new Size(680,double.PositiveInfinity));panel.Arrange(new Rect(0,0,680,panel.DesiredSize.Height));panel.UpdateLayout();
        byte[] Capture()
        {
            var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap(680,(int)panel.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(panel);
            var pixels=new byte[680*bitmap.PixelHeight*4];bitmap.CopyPixels(pixels,680*4,0);return pixels;
        }
        var artwork=Capture();foreach(UIElement card in panel.Children)card.Opacity=0;var decorations=Capture();
        var offset=VisualTreeHelper.GetOffset(panel.Children[2]);var paintedOverCover=false;
        for(var y=(int)offset.Y+20;y<Math.Min((int)panel.ActualHeight,(int)offset.Y+320)&&!paintedOverCover;y++)
            for(var x=22;x<222;x++)
            {
                var index=(y*680+x)*4;
                if(decorations[index+1]>150&&decorations[index+2]<220&&Enumerable.Range(0,4).All(c=>artwork[index+c]==decorations[index+c])){paintedOverCover=true;break;}
            }
        Assert.True(paintedOverCover,"Foreground emerald corner details must remain visible over a cover that overlaps them.");
    });

}

