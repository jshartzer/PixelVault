using System.IO;
using System.Reflection;
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

public sealed class TrackerSpreadsheetProgressTests : IDisposable
{
    readonly string dir=Path.Combine(Path.GetTempPath(),"PV-SheetProgress-"+Guid.NewGuid().ToString("N"));
    public TrackerSpreadsheetProgressTests()=>Directory.CreateDirectory(dir);
    public void Dispose()
    {
        var full=Path.GetFullPath(dir);Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()),full,StringComparison.OrdinalIgnoreCase);
        for(int i=0;Directory.Exists(full);i++){SqliteConnection.ClearAllPools();try{Directory.Delete(full,true);}catch(IOException)when(i<5){Thread.Sleep(50);}}
    }
    AchievementTrackingStore Store()=>new(dir);
    static AchievementTrackerDefinition Definition()=>new(){Id="custom.sheet-progress",Provider="steam",GameId="42",GameTitle="Fixture game",AchievementId="A",Title="Routes",
        Items=new(){new(){Id="one",Name="One",Target=3,Order=0},new(){Id="two",Name="Two",Order=1},new(){Id="three",Name="Three",Target=2,Order=2}}};
    static TrackerImportSheet Sheet(string csv)=>new(){Name="Tracker",Rows=TrackerSpreadsheetImport.Csv(csv)};
    static TrackerSpreadsheetMapping Mapping()=>new(){Worksheet="Tracker",Name="Name",Id="ID",Target="Target",Current="Current",Completed="Completed",Availability="Availability",Note="Notes"};
    [Fact]
    public void ProgressDistinguishesUnknownZeroTargetsAvailabilityAndNotes()
    {
        var draft=TrackerSpreadsheetProgress.Build(Sheet("ID,Name,Target,Current,Completed,Availability,Notes\none,One,3,,,,route\ntwo,Two,1,0,false,Locked,\nthree,Three,2,,true,Available,done"),Mapping());
        Assert.Null(draft.Progress[0].Current);Assert.Equal(0,draft.Progress[1].Current);Assert.Equal(2,draft.Progress[2].Current);
        Assert.Equal("route",draft.Progress[0].Note);Assert.Equal(TrackerAvailability.Locked,draft.Progress[1].Availability);
        var store=Store();var d=Definition();store.AddSpreadsheetDefinition(d,"player",draft.Progress,DateTime.UtcNow.Ticks,"fingerprint");
        Assert.Equal("A",store.Definitions().Single(x=>x.Id==d.Id).AchievementId);
        var rows=store.Load(d.Id,"player");Assert.Null(rows[0].Current);Assert.Equal("route",rows[0].Note);Assert.True(rows[2].Complete);
        store.SetManualProgress(d.Id,"player","one",1);store.Undo(d.Id,"player",store.History(d.Id,"player").Single(h=>h.Kind=="import").Id);
        rows=Store().Load(d.Id,"player");Assert.Equal(1,rows[0].Current);Assert.Equal("",rows[0].Note);Assert.Null(rows[2].Current);
        Assert.All(store.Load(d.Id,"other"),s=>Assert.Null(s.Current));
    }
    [Theory]
    [InlineData("one,One,3,-1,,,note")]
    [InlineData("one,One,3,1.5,,,note")]
    [InlineData("one,One,3,1,true,,note")]
    [InlineData("one,One,3,,maybe,,note")]
    [InlineData("one,One,3,,,invented,note")]
    [InlineData(",One,3,1,,,note")]
    public void InvalidProgressNeverCreatesAStore(string row)
    {
        Assert.Throws<InvalidDataException>(()=>TrackerSpreadsheetProgress.Build(Sheet("ID,Name,Target,Current,Completed,Availability,Notes\n"+row),Mapping()));
        Assert.False(File.Exists(Path.Combine(dir,"achievement-tracking","pixelvault-tracking.sqlite")));
    }
    [Fact]
    public void InvalidInitialObservationAndBlankAccountCannotLeavePartialDefinition()
    {
        var store=Store();var d=Definition();
        Assert.Throws<InvalidDataException>(()=>store.AddSpreadsheetDefinition(d,"player",new[]{new AchievementTrackerProgress{ItemId="missing",Current=1}},DateTime.UtcNow.Ticks,"file"));
        Assert.Throws<InvalidDataException>(()=>store.AddSpreadsheetDefinition(d,"",new[]{new AchievementTrackerProgress{ItemId="one",Current=1}},DateTime.UtcNow.Ticks,"file"));
        Assert.DoesNotContain(store.Definitions(),x=>x.Id==d.Id);
    }
    [Fact]
    public void BlankTemplateNeedsNoAccountAndCreatesNoObservations()
    {
        var draft=TrackerSpreadsheetProgress.Build(Sheet("ID,Name,Target,Current,Completed,Availability,Notes\none,One,3,,,,"),Mapping());
        Assert.Empty(draft.Progress);var d=Definition();d.Items=draft.Items;
        var store=Store();store.AddSpreadsheetDefinition(d,"",draft.Progress,DateTime.UtcNow.Ticks,"file");
        Assert.All(store.Load(d.Id,""),s=>Assert.Null(s.Current));Assert.Empty(store.History(d.Id,"player"));
    }
    string Csv(string text, int minutes=0)
    {
        var path=Path.Combine(dir,"Tracker.csv");File.WriteAllText(path,text);File.SetLastWriteTimeUtc(path,DateTime.UtcNow.AddHours(-3).AddMinutes(minutes));return path;
    }
    static TrackerSpreadsheetMapping SourceMapping()=>new(){Worksheet="Tracker",Id="ID",Current="Current",Note="Notes"};
    [Fact]
    public async Task RefreshSurvivesRowAndColumnReorderingPreservesManualNotesAndMissingItems()
    {
        var d=Definition();var store=Store();store.AddDefinition(d);var service=new TrackerRefreshService(store);
        var path=Csv("ID,Current,Notes\none,3,source note\ntwo,0,");
        service.Configure(d.Id,"player",SpreadsheetTrackerSource.AdapterId,"1",path,spreadsheet:SourceMapping());
        Assert.Equal("ID",Store().Source(d.Id,"player").Spreadsheet.Id);
        var plan=await service.PrepareAsync(d.Id,"player",default);Assert.Single(plan.Rows,r=>r.AddNote);service.Apply(plan,false,null!);
        Assert.Equal("source note",store.Load(d.Id,"player")[0].Note);
        store.SaveNote(d.Id,"player","one","My route");store.SetManualProgress(d.Id,"player","one",2);
        Csv("Notes,Current,ID\nnew,1,two\nreplace,0,one",1);
        plan=await service.PrepareAsync(d.Id,"player",default);Assert.Contains(plan.Rows,r=>r.Conflict);service.Apply(plan,false,null!);
        var rows=Store().Load(d.Id,"player");Assert.Equal(2,rows[0].Current);Assert.Equal(0,rows[0].ReportedCurrent);Assert.Equal("My route",rows[0].Note);Assert.Equal(1,rows[1].Current);
        Csv("ID,Current,Notes\ntwo,0,newer",2);service.Apply(await service.PrepareAsync(d.Id,"player",default),false,null!);
        Assert.Equal(0,store.Load(d.Id,"player")[0].ReportedCurrent);Assert.Equal("new",store.Load(d.Id,"player")[1].Note);
        Csv("ID,Current,Notes\nunknown,1,",3);
        await Assert.ThrowsAsync<InvalidDataException>(()=>service.PrepareAsync(d.Id,"player",default));
        Assert.Equal("My route",store.Load(d.Id,"player")[0].Note);
    }
    [Fact]
    public async Task AuthoredEmptyNotesStayEmptyAndRefreshUndoPreservesIndependentManualCounts()
    {
        var d=Definition();var store=Store();store.AddDefinition(d);var service=new TrackerRefreshService(store);
        var path=Csv("ID,Current,Notes\none,3,imported");service.Configure(d.Id,"player",SpreadsheetTrackerSource.AdapterId,"1",path,spreadsheet:SourceMapping());
        service.Apply(await service.PrepareAsync(d.Id,"player",default),false,null!);store.SetManualProgress(d.Id,"player","one",2);
        store.Undo(d.Id,"player",store.History(d.Id,"player").Single(h=>h.Kind=="refresh").Id);
        Assert.Equal(2,store.Load(d.Id,"player")[0].Current);Assert.Equal("",store.Load(d.Id,"player")[0].Note);
        store.SaveNote(d.Id,"player","one","Authored");store.SaveNote(d.Id,"player","one","");
        Csv("ID,Current,Notes\none,3,reintroduced",1);
        var plan=await service.PrepareAsync(d.Id,"player",default);Assert.False(plan.Rows.Single().AddNote);service.Apply(plan,false,null!);
        Assert.Equal("",store.Load(d.Id,"player")[0].Note);
    }
    [Fact]
    public async Task CancelDuplicateAndStalePreviewCannotSilentlyRewriteProgress()
    {
        var d=Definition();var store=Store();store.AddDefinition(d);var service=new TrackerRefreshService(store);
        var path=Csv("ID,Current,Notes\none,3,initial");service.Configure(d.Id,"player",SpreadsheetTrackerSource.AdapterId,"1",path,spreadsheet:SourceMapping());
        var plan=await service.PrepareAsync(d.Id,"player",default);service.Cancel(plan);Assert.Null(store.Load(d.Id,"player")[0].Current);
        plan=await service.PrepareAsync(d.Id,"player",default);store.SaveNote(d.Id,"player","one","New draft saved");
        Assert.Throws<InvalidOperationException>(()=>service.Apply(plan,false,null!));Assert.Null(store.Load(d.Id,"player")[0].Current);
        service.Apply(await service.PrepareAsync(d.Id,"player",default),false,null!);
        var history=store.History(d.Id,"player").Count;File.SetLastWriteTimeUtc(path,DateTime.UtcNow.AddMinutes(-1));
        Assert.Equal(0,service.Apply(await service.PrepareAsync(d.Id,"player",default),false,null!));Assert.Equal(history,store.History(d.Id,"player").Count);
    }
    [Fact]
    public async Task EqualTimeNoteAdditionNeedsTheSameExplicitReviewAsCountChanges()
    {
        var d=Definition();var store=Store();store.AddDefinition(d);var service=new TrackerRefreshService(store);
        var path=Csv("ID,Current,Notes\none,3,");var stamp=File.GetLastWriteTimeUtc(path);
        service.Configure(d.Id,"player",SpreadsheetTrackerSource.AdapterId,"1",path,spreadsheet:SourceMapping());
        service.Apply(await service.PrepareAsync(d.Id,"player",default),false,null!);
        Csv("ID,Current,Notes\none,3,new note");File.SetLastWriteTimeUtc(path,stamp);
        var plan=await service.PrepareAsync(d.Id,"player",default);Assert.True(plan.Rows.Single().Stale);
        Assert.Equal(0,service.Apply(plan,false,null!));Assert.Equal("",store.Load(d.Id,"player")[0].Note);
        service.Apply(await service.PrepareAsync(d.Id,"player",default),true,null!);Assert.Equal("new note",store.Load(d.Id,"player")[0].Note);
    }
    [Fact]
    public void MissingOrDuplicateHeadersAndMappedFormulasAreRejected()
    {
        var m=SourceMapping();m.Current="Missing";Assert.Throws<InvalidDataException>(()=>TrackerSpreadsheetProgress.Build(Sheet("ID,Current,Notes\none,1,"),m,Definition()));
        Assert.Throws<InvalidDataException>(()=>TrackerSpreadsheetProgress.Build(Sheet("ID,Current,Current,Notes\none,1,2,"),SourceMapping(),Definition()));
        var sheet=Sheet("ID,Current,Notes\none,1,cached");sheet.Formulas.Add((1,2));Assert.Throws<InvalidDataException>(()=>TrackerSpreadsheetProgress.Build(sheet,SourceMapping(),Definition()));
        Assert.Throws<InvalidDataException>(()=>TrackerSpreadsheetProgress.Build(Sheet("ID,Current,Notes\none,1,\none,2,"),SourceMapping(),Definition()));
    }
    [Fact]
    public void WorkbookExportsSeparateTemplateAndAccountProgressAndKeepFormulaLikeTextAsValues()
    {
        var d=Definition();d.Items[0].Name="=SUM(1,2)";var store=Store();store.AddDefinition(d);store.SetManualProgress(d.Id,"player","one",0);store.SaveNote(d.Id,"player","one","=HYPERLINK(\"example\")");
        var blank=Path.Combine(dir,"blank.xlsx");var progress=Path.Combine(dir,"progress.xlsx");
        TrackerSpreadsheetProgress.ExportWorkbook(blank,d);TrackerSpreadsheetProgress.ExportWorkbook(progress,d,store.Load(d.Id,"player"));
        var map=Mapping();var a=TrackerSpreadsheetProgress.Build(TrackerSpreadsheetImport.Read(blank).Single(),map);var b=TrackerSpreadsheetProgress.Build(TrackerSpreadsheetImport.Read(progress).Single(),map);
        Assert.All(a.Progress,p=>{Assert.Null(p.Current);Assert.Equal("",p.Note);});Assert.Equal(0,b.Progress[0].Current);Assert.Contains("HYPERLINK",b.Progress[0].Note);
        Assert.Equal("=SUM(1,2)",b.Items[0].Name);Assert.Empty(TrackerSpreadsheetImport.Read(progress).Single().Formulas);
        var json=JsonSerializer.Serialize(d);Assert.DoesNotContain("HYPERLINK",json);Assert.DoesNotContain("player",json);
    }
    static IEnumerable<DependencyObject> Children(DependencyObject node)
    {yield return node;foreach(var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())foreach(var found in Children(child))yield return found;}
    static void Pump(Task task)
    {var frame=new DispatcherFrame();_=task.ContinueWith(_=>Dispatcher.CurrentDispatcher.BeginInvoke(new Action(()=>frame.Continue=false)),TaskScheduler.FromCurrentSynchronizationContext());if(!task.IsCompleted)Dispatcher.PushFrame(frame);task.GetAwaiter().GetResult();}
    [Fact]
    public void UniversalFormMapsProgressAndSourceUiOffersSpreadsheetWithoutChangingLiveState()
    {
        var path=Csv("ID,Name,Target,Current,Completed,Availability,Notes\none,One,3,0,false,Available,route");
        AchievementCollectionAccessTests.OnSta(()=>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            var window=new Window{Content=new TrackerSpreadsheetForm(null!,new TextBox())};
            var source=new TrackerSourceWindow(null!,null!,null!,Definition(),"player");
            try
            {
                var form=(TrackerSpreadsheetForm)window.Content;Pump(form.LoadFileAsync(path));var draft=form.BuildDraft();Assert.Equal(0,draft.Progress.Single().Current);Assert.Equal("route",draft.Progress.Single().Note);
                var picker=Children((DependencyObject)source.Content).OfType<ComboBox>().Single(c=>AutomationProperties.GetName(c)=="Tracker source type");
                var choice=picker.Items.OfType<ComboBoxItem>().Single(i=>(string)i.Tag==SpreadsheetTrackerSource.AdapterId);picker.SelectedItem=choice;
                var sourceForm=Children((DependencyObject)source.Content).OfType<TrackerSpreadsheetForm>().Single();Assert.Equal(Visibility.Visible,sourceForm.Visibility);
                Pump(sourceForm.LoadFileAsync(path));Assert.Equal("ID",sourceForm.BuildMapping().Id);
                var output=Environment.GetEnvironmentVariable("PIXELVAULT_TRACKING_REVIEW_OUTPUT");if(string.IsNullOrWhiteSpace(output))return;
                Directory.CreateDirectory(output);var root=(FrameworkElement)source.Content;root.Measure(new Size(656,710));root.Arrange(new Rect(0,0,656,710));root.UpdateLayout();
                var bitmap=new RenderTargetBitmap(656,710,96,96,PixelFormats.Pbgra32);bitmap.Render(root);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(output,"spreadsheet-progress-source.png"));png.Save(file);
            }
            finally{window.Close();source.Close();}
        });
    }
}
