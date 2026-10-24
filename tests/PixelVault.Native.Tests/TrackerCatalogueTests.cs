using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Data.Sqlite;
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class TrackerCatalogueTests : IDisposable
{
    readonly string root=Path.Combine(Path.GetTempPath(),"PV-Catalogue-"+Guid.NewGuid().ToString("N"));
    public TrackerCatalogueTests()=>Directory.CreateDirectory(root);
    public void Dispose()
    {
        Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()),Path.GetFullPath(root),StringComparison.OrdinalIgnoreCase);
        for(int i=0;Directory.Exists(root);i++){SqliteConnection.ClearAllPools();try{Directory.Delete(root,true);}catch(IOException)when(i<5){Thread.Sleep(50);}}
    }
    AchievementTrackingStore Store()=>new(root);
    static AchievementTrackerDefinition Definition(string id="custom.fantasy",string provider="steam",string game="rpg-42")=>new(){Id=id,Provider=provider,GameId=game,GameTitle="Fantasy exploration",Title="Field journal",AchievementId="explore",
        Items=new(){new(){Id="herb",Name="Gather herbs",Category="Supplies",Target=5,Order=0},new(){Id="chest",Name="Open chest",Category="Exploration",Order=1},new(){Id="shrine",Name="Visit shrine",Category=null!,Order=2}}};
    static AchievementTrackerDefinition Change(AchievementTrackerDefinition original)
    {
        var d=TrackerCatalogueReconcile.Clone(original);d.Title="Incoming title is not applied";d.AchievementId="incoming-link";
        d.Items.RemoveAll(i=>i.Id=="shrine");d.Items[0].Name="Gather healing herbs";d.Items[0].Target=8;d.Items[0].Detail="Northern forest";d.Items[0].Category="Gathering";
        d.Items.Add(new(){Id="ore",Name="Collect ore",Category="Supplies",Target=3,Order=3});return d;
    }
    string SavedState(AchievementTrackingStore store)
    {
        using var db=new SqliteConnection("Data Source="+store.DatabasePath);db.Open();using var cmd=db.CreateCommand();cmd.CommandText="SELECT tracker_id,account,item_id,reported_current,manual_current,availability,note,source,evidence,observed_utc,manual_utc FROM tracker_item_state ORDER BY tracker_id,account,item_id";
        using var read=cmd.ExecuteReader();var rows=new List<object[]>();while(read.Read()){var row=new object[read.FieldCount];read.GetValues(row);rows.Add(row);}return JsonSerializer.Serialize(rows);
    }
    [Fact]
    public void ExplicitCatalogueChoicesPreserveEveryAccountAndUndoIndependentlyOfProgress()
    {
        var store=Store();var d=Definition();store.AddDefinition(d);store.SetManualProgress(d.Id,"one","herb",5);store.SaveNote(d.Id,"one","shrine","Private route");store.SetManualProgress(d.Id,"two","shrine",1);
        var before=SavedState(store);var incoming=Change(d);var plan=store.PrepareCatalogue(d.Id,incoming,new(){AddNew=true,UpdateMetadata=true,ArchiveMissing=true},"catalogue.csv");
        Assert.Equal(3,plan.Changes);Assert.Contains(plan.Rows,r=>r.Action=="Archive item");Assert.Equal(before,SavedState(store));store.ApplyCatalogue(plan);
        var saved=store.Definitions().Single(x=>x.Id==d.Id);Assert.Equal(d.Title,saved.Title);Assert.Equal("explore",saved.AchievementId);Assert.Equal(8,saved.Items[0].Target);Assert.True(saved.Items.Single(i=>i.Id=="shrine").Archived);
        Assert.Equal(before,SavedState(store));Assert.Null(store.Load(d.Id,"one").Single(s=>s.Item.Id=="ore").Current);
        store.SetManualProgress(d.Id,"one","ore",2);store.SaveNote(d.Id,"two","herb","Later note");var afterProgress=SavedState(store);store.UndoCatalogue(d.Id);
        Assert.Equal(afterProgress,SavedState(store));Assert.Equal(3,store.Definitions().Single(x=>x.Id==d.Id).Items.Count);Assert.False(store.HasCatalogueUndo(d.Id));
        store.ApplyCatalogue(store.PrepareCatalogue(d.Id,incoming,new(){AddNew=true},"again"));Assert.Equal(2,store.Load(d.Id,"one").Single(s=>s.Item.Id=="ore").Current);
    }
    [Fact]
    public void NoSelectionMakesNoWriteAndMissingRowsAreRetainedByDefault()
    {
        var store=Store();var d=Definition();store.AddDefinition(d);var plan=store.PrepareCatalogue(d.Id,Change(d),new(),"file");Assert.Equal(0,plan.Changes);Assert.Equal(0,store.ApplyCatalogue(plan));Assert.False(store.HasCatalogueUndo(d.Id));
        store.ApplyCatalogue(store.PrepareCatalogue(d.Id,Change(d),new(){UpdateMetadata=true},"file"));var saved=store.Definitions().Single(x=>x.Id==d.Id);
        Assert.Contains(saved.Items,i=>i.Id=="shrine"&&!i.Archived);Assert.DoesNotContain(saved.Items,i=>i.Id=="ore");
    }
    [Fact]
    public void RestoreUsesStableIdsAndRetainsArchivedNotesAndCounts()
    {
        var d=Definition();d.Items[1].Archived=true;var store=Store();store.AddDefinition(d);store.SaveNote(d.Id,"one","chest","Old note");store.SetManualProgress(d.Id,"one","chest",1);
        var incoming=TrackerCatalogueReconcile.FromSpreadsheet(Sheet("ID\nchest"),new(){Worksheet="Items",Id="ID"},d);var plan=store.PrepareCatalogue(d.Id,incoming,new(){RestorePresent=true},"restore.csv");Assert.Single(plan.Rows,r=>r.Action=="Restore item");store.ApplyCatalogue(plan);
        var item=store.Load(d.Id,"one").Single(s=>s.Item.Id=="chest");Assert.False(item.Item.Archived);Assert.True(item.Complete);Assert.Equal("Old note",item.Note);
    }
    [Fact]
    public void StaleCatalogueAndProgressPreviewsCannotApplyAfterMetadataChanges()
    {
        var d=Definition();var store=Store();store.AddDefinition(d);var a=store.PrepareCatalogue(d.Id,Change(d),new(){UpdateMetadata=true},"one");var b=store.PrepareCatalogue(d.Id,Change(d),new(){AddNew=true},"two");
        var snapshot=new AchievementTrackerSnapshot { TrackerId=d.Id,Provider=d.Provider,GameId=d.GameId,Account="one",ObservedUtcTicks=DateTime.UtcNow.Ticks,Source="Source",Items=new(){new(){ItemId="herb",Current=5}} };
        var progress=store.Preview(JsonSerializer.Serialize(snapshot),d.Id,"one","json-import","1","fingerprint");store.ApplyCatalogue(a);
        Assert.Throws<InvalidOperationException>(()=>store.ApplyCatalogue(b));Assert.Throws<InvalidOperationException>(()=>store.ApplyRefresh(progress,false,null!,default));Assert.Null(store.Load(d.Id,"one")[0].Current);
    }
    [Fact]
    public void CatalogueWriteFailureRollsBackMetadataAndHistory()
    {
        var store=Store();var d=Definition();store.AddDefinition(d);var before=TrackerCatalogueReconcile.Json(store.Definitions().Single(x=>x.Id==d.Id));var plan=store.PrepareCatalogue(d.Id,Change(d),new(){AddNew=true},"file");
        using(var db=new SqliteConnection("Data Source="+store.DatabasePath)){db.Open();using var cmd=db.CreateCommand();cmd.CommandText="CREATE TRIGGER reject_catalogue BEFORE INSERT ON tracker_catalogue_history BEGIN SELECT RAISE(ABORT,'fixture'); END;";cmd.ExecuteNonQuery();}
        Assert.Throws<SqliteException>(()=>store.ApplyCatalogue(plan));Assert.Equal(before,TrackerCatalogueReconcile.Json(store.Definitions().Single(x=>x.Id==d.Id)));Assert.False(store.HasCatalogueUndo(d.Id));
    }
    [Fact]
    public void CatalogueUndoRefusesAnUnrelatedLaterDefinitionEdit()
    {
        var store=Store();var d=Definition();store.AddDefinition(d);store.ApplyCatalogue(store.PrepareCatalogue(d.Id,Change(d),new(){UpdateMetadata=true},"file"));
        var changed=store.Definitions().Single(x=>x.Id==d.Id);changed.Description="Later metadata";
        using(var db=new SqliteConnection("Data Source="+store.DatabasePath)){db.Open();using var cmd=db.CreateCommand();cmd.CommandText="UPDATE tracker_definition SET definition=$d WHERE id=$i";cmd.Parameters.AddWithValue("$d",TrackerCatalogueReconcile.Json(changed));cmd.Parameters.AddWithValue("$i",d.Id);cmd.ExecuteNonQuery();}
        Assert.Throws<InvalidOperationException>(()=>store.UndoCatalogue(d.Id));Assert.Equal("Later metadata",store.Definitions().Single(x=>x.Id==d.Id).Description);
    }
    [Fact]
    public void BundledDefinitionsAreProtectedAndIndependentCopiesAreEditable()
    {
        var store=Store();var bundled=store.Definitions().First();Assert.False(store.CanEditCatalogue(bundled.Id));
        Assert.Throws<InvalidDataException>(()=>store.PrepareCatalogue(bundled.Id,bundled,new(),"file"));
        var copy=TrackerDefinitionTransfer.Prepare(bundled,store.Definitions().Select(x=>x.Id),true,"My catalogue",null!);store.AddDefinition(copy);Assert.True(store.CanEditCatalogue(copy.Id));
    }
    [Theory]
    [InlineData("game")]
    [InlineData("provider")]
    [InlineData("duplicate")]
    [InlineData("target")]
    [InlineData("milestones")]
    public void InvalidCatalogueCannotChangeTheSavedDefinition(string change)
    {
        var d=Definition();var store=Store();store.AddDefinition(d);var incoming=Change(d);
        switch(change){case "game":incoming.GameId="different";break;case "provider":incoming.Provider="retroachievements";break;case "duplicate":incoming.Items.Add(incoming.Items[0]);break;case "target":incoming.Items[0].Target=0;break;case "milestones":incoming.Milestones.Add("Collected");break;}
        Assert.Throws<InvalidDataException>(()=>store.PrepareCatalogue(d.Id,incoming,new(){AddNew=true,UpdateMetadata=true},"file"));Assert.False(store.HasCatalogueUndo(d.Id));Assert.Equal(d.Title,store.Definitions().Single(x=>x.Id==d.Id).Title);
    }
    static TrackerImportSheet Sheet(string csv)=>new(){Name="Items",Rows=TrackerSpreadsheetImport.Csv(csv)};
    [Fact]
    public void SpreadsheetReorderingPreservesUnmappedFieldsAndIgnoresPersonalColumns()
    {
        var d=Definition();d.Items[0].Identifiers.Add("Resource","herb-uuid");var sheet=Sheet("Name,ID,Target,Current,Notes\nOpen chest,chest,2,1,do not import\nGather herbs,herb,9,5,private");
        var incoming=TrackerCatalogueReconcile.FromSpreadsheet(sheet,new(){Worksheet="Items",Id="ID",Name="Name",Target="Target"},d);
        Assert.Equal(new[]{"chest","herb"},incoming.Items.Select(i=>i.Id));Assert.Equal(0,incoming.Items[1].Order);Assert.Equal("Supplies",incoming.Items[1].Category);Assert.Equal("herb-uuid",incoming.Items[1].Identifiers["Resource"]);
        var store=Store();store.AddDefinition(d);store.ApplyCatalogue(store.PrepareCatalogue(d.Id,incoming,new(){UpdateMetadata=true},"file"));Assert.All(store.Load(d.Id,"one"),s=>{Assert.Null(s.Current);Assert.Equal("",s.Note);});
        var bad=Sheet("ID,Target\nherb,3\nherb,4");Assert.Throws<InvalidDataException>(()=>TrackerCatalogueReconcile.FromSpreadsheet(bad,new(){Worksheet="Items",Id="ID",Target="Target"},d));
        sheet.Formulas.Add((1,2));Assert.Throws<InvalidDataException>(()=>TrackerCatalogueReconcile.FromSpreadsheet(sheet,new(){Worksheet="Items",Id="ID",Target="Target"},d));
    }
    static AchievementTrackerDefinition Matrix()
    {
        var d=Definition("custom.racing","retroachievements","racing-7");d.GameTitle="Arcade racing";d.Milestones=new(){"Found","Collected","Used","Mastered"};d.Items=d.Milestones.Select((m,n)=>new AchievementTrackerItem { Id="lap:"+m,GroupId="lap",Milestone=m,Name="First course",Category="Courses",Order=n }).ToList();return d;
    }
    [Fact]
    public void AnyMilestoneMatrixCanBeExtendedAndPartialRecordArchivesAreRejected()
    {
        var d=Matrix();var sheet=Sheet("ID,Name,Record ID,Milestone,Category\nlap:Found,First course,lap,Found,Courses\nlap:Collected,First course,lap,Collected,Courses\nlap:Used,First course,lap,Used,Courses\nlap:Mastered,First course,lap,Mastered,Courses\nnew:Found,Second course,new,Found,Courses\nnew:Collected,Second course,new,Collected,Courses\nnew:Used,Second course,new,Used,Courses\nnew:Mastered,Second course,new,Mastered,Courses");
        var incoming=TrackerCatalogueReconcile.FromSpreadsheet(sheet,new(){Worksheet="Items",Id="ID",Name="Name",Category="Category",Group="Record ID",Milestone="Milestone"},d);
        var store=Store();store.AddDefinition(d);store.ApplyCatalogue(store.PrepareCatalogue(d.Id,incoming,new(){AddNew=true},"matrix"));Assert.Equal(8,store.Load(d.Id,"player").Count);
        incoming=TrackerCatalogueReconcile.Clone(d);incoming.Items.RemoveAt(0);Assert.Throws<InvalidDataException>(()=>store.PrepareCatalogue(d.Id,incoming,new(){ArchiveMissing=true},"partial"));
        d.Items[0].Archived=true;Assert.Throws<InvalidDataException>(()=>TrackerDefinitionTransfer.Serialize(d));
    }
    static T Field<T>(object o,string name)=>(T)o.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(o)!;
    static IEnumerable<DependencyObject> Children(DependencyObject root)
    {yield return root;foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())foreach(var found in Children(child))yield return found;}
    static void Pump(Task task){var frame=new DispatcherFrame();var dispatcher=Dispatcher.CurrentDispatcher;_=task.ContinueWith(_=>dispatcher.BeginInvoke(new Action(()=>frame.Continue=false)),TaskScheduler.Default);if(!task.IsCompleted)Dispatcher.PushFrame(frame);task.GetAwaiter().GetResult();}
    static void Render(Window window,string name,int width,int height)
    {
        var root=(FrameworkElement)window.Content;root.Measure(new Size(width,height));root.Arrange(new Rect(0,0,width,height));root.UpdateLayout();
        var output=Environment.GetEnvironmentVariable("PIXELVAULT_TRACKING_REVIEW_OUTPUT");if(string.IsNullOrWhiteSpace(output))return;Directory.CreateDirectory(output);
        var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(root);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(output,name+".png"));png.Save(file);
    }
    [Fact]
    public void CompactCategoryGroupsPreserveCountsFilteringAndUnnamedTrackersStayUngrouped()
    {
        var store=Store();var d=Definition();d.Items[1].Archived=true;store.AddDefinition(d);
        AchievementCollectionAccessTests.OnSta(()=>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));var window=new AchievementTrackerWindow(null!,store,"player","",provider:d.Provider,gameId:d.GameId);
            try
            {
                Pump(window.LoadDefinitionsAsync());var list=Field<ListBox>(window,"list");Assert.Equal(2,list.Items.Count);Assert.Contains("0 / 2",Field<TextBlock>(window,"summary").Text);
                var view=Assert.IsType<ListCollectionView>(list.ItemsSource);Assert.Equal(new[]{"Supplies","Other"},view.Groups!.Cast<CollectionViewGroup>().Select(g=>(string)g.Name));
                Render(window,"tracker-generic-grouped",732,690);foreach(var item in list.Items.OfType<ListBoxItem>()){var border=Assert.IsType<Border>(item.Content);Assert.Equal(34,border.ActualHeight);var row=(Grid)border.Child;Assert.Equal(1,Grid.GetColumn(row.Children[1]));}
                var categories=Field<ComboBox>(window,"category");categories.SelectedItem="Other";Assert.Single(list.Items);Assert.Contains("0 / 2",Field<TextBlock>(window,"summary").Text);categories.SelectedIndex=0;
                Field<TextBox>(window,"search").Text="does not exist";Assert.Single(list.Items);Assert.False(((ListBoxItem)list.Items[0]).IsEnabled);
            }
            finally{window.Close();}
            var plain=Definition("custom.plain","retroachievements","other-9");plain.Items.ForEach(i=>i.Category=null!);store.AddDefinition(plain);
            var other=new AchievementTrackerWindow(null!,store,"","player",provider:plain.Provider,gameId:plain.GameId);
            try{Pump(other.LoadDefinitionsAsync());Assert.Empty(((ListCollectionView)Field<ListBox>(other,"list").ItemsSource).Groups??new System.Collections.ObjectModel.ReadOnlyObservableCollection<object>(new()));Render(other,"tracker-generic-ungrouped",732,690);}
            finally{other.Close();}
        });
    }
    [Fact]
    public void FourArbitraryMilestonesFitCompactRowsWithHorizontalAccessAndGenericGameChoices()
    {
        var d=Matrix();var store=Store();store.AddDefinition(d);
        AchievementCollectionAccessTests.OnSta(()=>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));var window=new AchievementTrackerWindow(null!,store,"","player",provider:d.Provider,gameId:d.GameId);
            try
            {
                Pump(window.LoadDefinitionsAsync());var list=Field<ListBox>(window,"list");Render(window,"tracker-generic-four-milestones",732,690);Assert.Single(list.Items);Assert.Equal(ScrollBarVisibility.Auto,ScrollViewer.GetHorizontalScrollBarVisibility(list));
                var border=(Border)((ListBoxItem)list.Items[0]).Content;Assert.Equal(34,border.ActualHeight);var row=(Grid)border.Child;var buttons=((StackPanel)row.Children[1]).Children.OfType<ToggleButton>().ToList();Assert.Equal(4,buttons.Count);Assert.All(buttons,b=>Assert.Equal(25,b.ActualHeight));
                Assert.Contains(Field<List<AchievementHubGame>>(window,"games"),g=>g.Provider==d.Provider && g.Id==d.GameId && g.Title==d.GameTitle);
            }
            finally{window.Close();}
        });
    }
    [Fact]
    public void SharedCatalogueUiPreviewsCancelsAppliesAndUndoesWithoutPersonalWrites()
    {
        var d=Definition();var store=Store();store.AddDefinition(d);store.SaveNote(d.Id,"player","herb","Keep this");var personal=SavedState(store);
        var path=Path.Combine(root,"catalogue.csv");File.WriteAllText(path,"ID,Name,Category,Target\nherb,Gather healing herbs,Gathering,8\nchest,Open chest,Exploration,1\nore,Collect ore,Supplies,3");
        AchievementCollectionAccessTests.OnSta(()=>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));var window=new AchievementTrackerWindow(null!,store,"player","",provider:d.Provider,gameId:d.GameId);
            try
            {
                Pump(window.LoadDefinitionsAsync());window.ShowCatalogue=dialog=>{Pump(dialog.LoadFileAsync(path));Assert.Equal(0,dialog.Plan.Changes);return false;};Pump(window.ManageCatalogueAsync());Assert.False(store.HasCatalogueUndo(d.Id));
                window.ShowCatalogue=dialog=>
                {
                    Pump(dialog.LoadFileAsync(path));foreach(var box in Children((DependencyObject)dialog.Content).OfType<CheckBox>())if(AutomationProperties.GetName(box)!="Restore present archived catalogue items")box.IsChecked=true;
                    Assert.Equal(3,dialog.Plan.Changes);Render(dialog,"tracker-catalogue-review",872,750);
                    var apply=Children((DependencyObject)dialog.Content).OfType<Button>().Single(b=>Equals(b.Content,"Apply catalogue"));Assert.True(apply.ActualHeight>=38);Assert.InRange(apply.TranslatePoint(new Point(0,apply.ActualHeight),(UIElement)dialog.Content).Y,0,706);
                    Render(dialog,"tracker-catalogue-review-narrow",532,450);Assert.InRange(apply.TranslatePoint(new Point(0,apply.ActualHeight),(UIElement)dialog.Content).Y,0,406);Pump(dialog.ApplyAsync());Assert.True(dialog.HasChanges);return true;
                };
                Pump(window.ManageCatalogueAsync());Assert.Equal(personal,SavedState(store));Assert.True(store.HasCatalogueUndo(d.Id));Assert.DoesNotContain(Field<List<AchievementTrackerState>>(window,"states"),s=>s.Item.Id=="shrine");
                window.ShowCatalogue=dialog=>{Pump(dialog.UndoAsync());return true;};Pump(window.ManageCatalogueAsync());Assert.Equal(personal,SavedState(store));Assert.False(store.HasCatalogueUndo(d.Id));
            }
            finally{window.Close();}
        });
    }
    [Fact]
    public void JsonCatalogueWorksForASecondGameWithoutAnAccountAndKeepsItsLink()
    {
        var d=Definition("custom.arcade","retroachievements","arcade-77");var store=Store();store.AddDefinition(d);var path=Path.Combine(root,"catalogue.json");File.WriteAllText(path,TrackerDefinitionTransfer.Serialize(Change(d)));
        AchievementCollectionAccessTests.OnSta(()=>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));var dialog=new TrackerCatalogueWindow(null!,store,d);
            try
            {
                Pump(dialog.LoadFileAsync(path));var choices=Children((DependencyObject)dialog.Content).OfType<CheckBox>().ToList();choices.Single(c=>AutomationProperties.GetName(c)=="Add new catalogue items").IsChecked=true;
                Assert.Equal(1,dialog.Plan.Changes);Render(dialog,"tracker-catalogue-json",872,750);Pump(dialog.ApplyAsync());Assert.True(dialog.HasChanges);
                var saved=store.Definitions().Single(x=>x.Id==d.Id);Assert.Equal(d.Title,saved.Title);Assert.Equal(d.AchievementId,saved.AchievementId);Assert.All(store.Load(d.Id,""),s=>Assert.Null(s.Current));
            }
            finally{dialog.Close();}
        });
    }
}
