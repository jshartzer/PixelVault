using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Data.Sqlite;
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class TrackerMilestoneAuthoringTests : IDisposable
{
    readonly string dir=Path.Combine(Path.GetTempPath(),"PV-CompletionColumns-"+Guid.NewGuid().ToString("N"));
    public TrackerMilestoneAuthoringTests()=>Directory.CreateDirectory(dir);
    public void Dispose(){SqliteConnection.ClearAllPools();Directory.Delete(dir,true);}
    static TrackerImportSheet Sheet(string csv)=>new(){Name="Tracker",Rows=TrackerSpreadsheetImport.Csv(csv)};
    static TrackerSpreadsheetMapping Mapping()=>new(){Worksheet="Tracker",Id="ID",Name="Name",CompletionColumns=new(){new(){Label="Discovered",Header="Found"},new(){Label="Collected",Header="Taken"}}};
    static AchievementTrackerDefinition Definition(TrackerSpreadsheetDraft draft)=>new(){Id="custom.exploration",Provider="retroachievements",GameId="another-game",GameTitle="Another game",Title="Field journal",Items=draft.Items,Milestones=draft.Milestones};
    static IEnumerable<DependencyObject> Children(DependencyObject root)
    {
        yield return root;foreach(var obj in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())foreach(var child in Children(obj))yield return child;
    }
    static void Pump(Task task)
    {
        var frame=new DispatcherFrame();
        var dispatcher=Dispatcher.CurrentDispatcher;_=task.ContinueWith(_=>dispatcher.BeginInvoke(new Action(()=>frame.Continue=false)),TaskScheduler.Default);
        if(!task.IsCompleted)Dispatcher.PushFrame(frame);task.GetAwaiter().GetResult();
    }
    [Fact]
    public void IndependentBoxesSupportAnyGameUnknownZeroAndDeterministicIds()
    {
        var draft=TrackerSpreadsheetProgress.Build(Sheet("ID,Name,Found,Taken\nchest,Chest,yes,0\nshrine,Shrine,,true"),Mapping());var d=Definition(draft);AchievementTrackingCatalog.Validate(d);
        Assert.Equal(new[]{"Discovered","Collected"},d.Milestones);Assert.Equal(4,d.Items.Count);Assert.Equal(3,draft.Progress.Count);
        Assert.Equal(new int?[]{1,0,1},draft.Progress.Select(p=>p.Current));
        var reordered=TrackerSpreadsheetProgress.Build(Sheet("Taken,Name,ID,Found\ntrue,Shrine,shrine,\n0,Chest,chest,yes"),Mapping());
        Assert.Equal(d.Items.Select(i=>i.Id).Order(),reordered.Items.Select(i=>i.Id).Order());
        var store=new AchievementTrackingStore(dir);store.AddSpreadsheetDefinition(d,"player",draft.Progress,DateTime.UtcNow.Ticks,"initial");
        var states=store.Load(d.Id,"player");Assert.Equal(2,states.Count(s=>s.Complete));Assert.Null(states.Single(s=>s.Item.GroupId=="shrine"&&s.Item.Milestone=="Discovered").Current);
        Assert.All(store.Load(d.Id,"other"),s=>Assert.Null(s.Current));
    }
    [Fact]
    public async Task NamedColumnRefreshSurvivesReorderingPreservesOverridesAndCanBeUndone()
    {
        var draft=TrackerSpreadsheetProgress.Build(Sheet("ID,Name,Found,Taken\nchest,Chest,,\nshrine,Shrine,,"),Mapping());var d=Definition(draft);var store=new AchievementTrackingStore(dir);store.AddDefinition(d);
        var item=d.Items.Single(i=>i.GroupId=="chest"&&i.Milestone=="Discovered");store.SetManualProgress(d.Id,"player",item.Id,0);store.SaveNote(d.Id,"player",item.Id,"Keep my route");
        var path=Path.Combine(dir,"Tracker.csv");File.WriteAllText(path,"Taken,ID,Name,Found\ntrue,shrine,Shrine,yes\nfalse,chest,Chest,true");
        var service=new TrackerRefreshService(store);service.Configure(d.Id,"player",SpreadsheetTrackerSource.AdapterId,"1",path,spreadsheet:Mapping());
        Assert.Equal("Collected",new AchievementTrackingStore(dir).Source(d.Id,"player").Spreadsheet.CompletionColumns[1].Label);
        var plan=await service.PrepareAsync(d.Id,"player",default);service.Apply(plan,false,null!);
        var states=store.Load(d.Id,"player");Assert.Equal(0,states.Single(s=>s.Item.Id==item.Id).Current);Assert.Equal("Keep my route",states.Single(s=>s.Item.Id==item.Id).Note);Assert.Equal(2,states.Count(s=>s.Complete));
        store.Undo(d.Id,"player",store.History(d.Id,"player").First(h=>h.Kind=="refresh").Id);
        Assert.Equal(0,store.Load(d.Id,"player").Count(s=>s.Complete));Assert.Equal("Keep my route",store.Load(d.Id,"player").Single(s=>s.Item.Id==item.Id).Note);
    }
    [Fact]
    public void BlankAndUnmappedBoxesRemainAccountFreeAndSixteenColumnsRoundTrip()
    {
        var mapping=Mapping();mapping.CompletionColumns[1].Header=null;
        var draft=TrackerSpreadsheetProgress.Build(Sheet("ID,Name,Found\nx,Example,"),mapping);Assert.Empty(draft.Progress);
        var d=Definition(draft);new AchievementTrackingStore(dir).AddSpreadsheetDefinition(d,"",draft.Progress,DateTime.UtcNow.Ticks,"blank");
        d.Milestones=Enumerable.Range(1,16).Select(n=>"Step "+n).ToList();d.Items=TrackerMilestones.Expand(new[]{new AchievementTrackerItem{Id="x",Name="Example"}},d.Milestones);
        var copy=TrackerDefinitionTransfer.Parse(TrackerDefinitionTransfer.Serialize(d));Assert.Equal(16,copy.Milestones.Count);Assert.Equal(d.Items.Select(i=>i.Id),copy.Items.Select(i=>i.Id));
    }
    [Theory]
    [InlineData("maybe","Taken")]
    [InlineData("yes","Found")]
    public void InvalidOrDuplicateColumnValuesAreRejected(string value,string secondHeader)
    {
        var map=Mapping();map.CompletionColumns[1].Header=secondHeader;
        Assert.Throws<InvalidDataException>(()=>TrackerSpreadsheetProgress.Build(Sheet("ID,Name,Found,Taken\nx,Example,"+value+",false"),map));
    }
    [Theory]
    [InlineData("Collected","collected")]
    [InlineData("", "Other")]
    public void CompletionNamesMustBePresentAndDistinct(string first,string second)=>Assert.Throws<InvalidDataException>(()=>TrackerMilestones.Labels(new[]{first,second}));
    [Fact]
    public void ExcessColumnsCountsFormulasAndMismatchedSourceNamesAreRejected()
    {
        Assert.Throws<InvalidDataException>(()=>TrackerMilestones.Labels(Enumerable.Range(0,17).Select(n=>"Step "+n)));
        Assert.Throws<InvalidDataException>(()=>TrackerMilestones.Expand(Enumerable.Range(0,626).Select(n=>new AchievementTrackerItem{Id=n.ToString(),Name="Item"}),Enumerable.Range(0,16).Select(n=>"Step "+n)));
        Assert.Throws<InvalidDataException>(()=>TrackerMilestones.Expand(new[]{new AchievementTrackerItem{Id="x",Name="Count",Target=3}},new[]{"One","Two"}));
        var sheet=Sheet("ID,Name,Found,Taken\nx,Example,true,false");sheet.Formulas.Add((1,2));Assert.Throws<InvalidDataException>(()=>TrackerSpreadsheetProgress.Build(sheet,Mapping()));
        var draft=TrackerSpreadsheetProgress.Build(Sheet("ID,Name,Found,Taken\nx,Example,true,false"),Mapping());var map=Mapping();map.CompletionColumns[0].Label="Wrong";
        Assert.Throws<InvalidDataException>(()=>new SpreadsheetTrackerSource().Validate(new(){AdapterId=SpreadsheetTrackerSource.AdapterId,AdapterVersion="1",Location=Path.Combine(dir,"file.csv"),Spreadsheet=map},Definition(draft)));
    }
    [Fact]
    public void ExportedGroupedWorkbookReimportsWithSameRecordAndBoxIdentities()
    {
        var draft=TrackerSpreadsheetProgress.Build(Sheet("ID,Name,Found,Taken\nx,Example,true,false"),Mapping());var d=Definition(draft);var path=Path.Combine(dir,"template.xlsx");TrackerSpreadsheetProgress.ExportWorkbook(path,d);
        var sheet=TrackerSpreadsheetImport.Read(path).Single();
        var map=new TrackerSpreadsheetMapping { Worksheet=sheet.Name,Id="ID",Name="Name",Category="Category",Target="Target",Detail="Details",Current="Current",Completed="Completed",Availability="Availability",Note="Notes",Group="Record ID",Milestone="Milestone" };
        var imported=TrackerSpreadsheetProgress.Build(sheet,map);AchievementTrackingCatalog.Validate(Definition(imported));
        Assert.Equal(d.Milestones,imported.Milestones);Assert.Equal(d.Items.Select(i=>(i.Id,i.GroupId,i.Milestone)),imported.Items.Select(i=>(i.Id,i.GroupId,i.Milestone)));Assert.Empty(imported.Progress);
    }
    [Fact]
    public void SharedAuthoringUiOffersNamedBoxesForManualAndSpreadsheetCreation()
    {
        var path=Path.Combine(dir,"Tracker.csv");File.WriteAllText(path,"ID,Name,Found,Taken\nx,Example,true,false");
        AchievementCollectionAccessTests.OnSta(()=>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            var game=new AchievementHubGame{Provider="retroachievements",Id="another-game",Title="Another game"};
            foreach(bool spreadsheet in new[]{false,true})
            {
                var dialog=new AchievementTrackerEditWindow(null!,new[]{game},null!,null!,spreadsheet:spreadsheet);
                try
                {
                    var controls=Children((DependencyObject)dialog.Content).ToList();var editor=controls.OfType<TrackerMilestoneEditor>().Single();
                    controls.OfType<CheckBox>().Single(c=>AutomationProperties.GetName(c)=="Use multiple completion boxes").IsChecked=true;
                    var names=Children(editor).OfType<TextBox>().ToList();names[0].Text="Discovered";names[1].Text="Collected";Assert.Equal(new[]{"Discovered","Collected"},editor.Labels);
                    if(spreadsheet)
                    {
                        var form=controls.OfType<TrackerSpreadsheetForm>().Single();Pump(form.LoadFileAsync(path));
                        var maps=Children(editor).OfType<ComboBox>().Where(c=>AutomationProperties.GetName(c).EndsWith("spreadsheet column")).ToList();maps[0].SelectedItem="Found";maps[1].SelectedItem="Taken";
                        var draft=form.BuildDraft();Assert.Equal(2,draft.Items.Count);Assert.Equal(new int?[]{1,0},draft.Progress.Select(p=>p.Current));
                    }
                    var root=(FrameworkElement)dialog.Content;root.Measure(new Size(632,850));root.Arrange(new Rect(0,0,632,850));root.UpdateLayout();
                    var output=Environment.GetEnvironmentVariable("PIXELVAULT_TRACKING_REVIEW_OUTPUT");
                    if(!string.IsNullOrEmpty(output)&&spreadsheet){var bitmap=new RenderTargetBitmap(632,850,96,96,PixelFormats.Pbgra32);bitmap.Render(root);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(output,"tracker-named-boxes-import.png"));png.Save(file);}
                    root.Measure(new Size(480,450));root.Arrange(new Rect(0,0,480,450));root.UpdateLayout();
                    var create=controls.OfType<Button>().Single(b=>Equals(b.Content,"Create checklist"));Assert.InRange(create.TranslatePoint(new Point(0,create.ActualHeight),root).Y,0,450);
                }
                finally{dialog.Close();}
            }
        });
    }
    [Fact]
    public void ManualDialogCreatesGenericMultiBoxDefinitionAndKeepsItsAchievementLink()
    {
        AchievementCollectionAccessTests.OnSta(()=>
        {
            var game=new AchievementHubGame{Provider="steam",Id="42",Title="Puzzle game"};
            var context=new AchievementTrackerDefinition{Provider="steam",GameId="42"};
            var dialog=new AchievementTrackerEditWindow(null!,new[]{game},context,"puzzles");
            dialog.Loaded+=(_,__)=>dialog.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle,new Action(()=>
            {
                var fields=Children((DependencyObject)dialog.Content).ToList();
                fields.OfType<TextBox>().Single(c=>AutomationProperties.GetName(c)=="Checklist title").Text="Puzzle mastery";
                fields.OfType<TextBox>().Single(c=>AutomationProperties.GetName(c)=="Checklist items, one per line").Text="Room one\nRoom two";
                fields.OfType<CheckBox>().Single(c=>AutomationProperties.GetName(c)=="Use multiple completion boxes").IsChecked=true;
                var editor=fields.OfType<TrackerMilestoneEditor>().Single();var names=Children(editor).OfType<TextBox>().ToList();names[0].Text="Solved";names[1].Text="Perfect";
                var create=fields.OfType<Button>().Single(b=>Equals(b.Content,"Create checklist"));
                var root=(FrameworkElement)dialog.Content;root.UpdateLayout();Assert.True(create.ActualHeight>=38);Assert.InRange(create.TranslatePoint(new Point(0,create.ActualHeight),root).Y,0,root.ActualHeight);
                var output=Environment.GetEnvironmentVariable("PIXELVAULT_TRACKING_REVIEW_OUTPUT");
                if(!string.IsNullOrEmpty(output)){var bitmap=new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth+root.Margin.Left+root.Margin.Right),(int)Math.Ceiling(root.ActualHeight+root.Margin.Top+root.Margin.Bottom),96,96,PixelFormats.Pbgra32);bitmap.Render(root);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(output,"tracker-named-boxes-manual.png"));png.Save(file);}
                fields.OfType<Button>().Single(b=>Equals(b.Content,"Create checklist")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }));
            try
            {
                Assert.True(dialog.ShowDialog());var d=dialog.Definition;AchievementTrackingCatalog.Validate(d);Assert.Equal("puzzles",d.AchievementId);Assert.Equal("42",d.GameId);Assert.Equal(new[]{"Solved","Perfect"},d.Milestones);Assert.Equal(4,d.Items.Count);
                var store=new AchievementTrackingStore(dir);store.AddDefinition(d);var one=d.Items[0];store.SetManualProgress(d.Id,"player",one.Id,1);
                var state=store.Load(d.Id,"player");Assert.Single(state,s=>s.Complete);Assert.Null(state.Single(s=>s.Item.GroupId==one.GroupId&&s.Item.Id!=one.Id).Current);
            }
            finally{dialog.Close();}
        });
    }
}
