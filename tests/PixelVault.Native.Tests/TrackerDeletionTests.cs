using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Microsoft.Data.Sqlite;
using PixelVaultNative;
using Xunit;
using static PixelVaultNative.Tests.AchievementCollectionAccessTests;

namespace PixelVaultNative.Tests;

public sealed class TrackerDeletionTests : IDisposable
{
    static void Pump(Task task)
    {
        var frame=new DispatcherFrame();task.ContinueWith(_=>Dispatcher.CurrentDispatcher.BeginInvoke(new Action(()=>frame.Continue=false)),TaskScheduler.FromCurrentSynchronizationContext());
        if(!task.IsCompleted)Dispatcher.PushFrame(frame);task.GetAwaiter().GetResult();
    }
    readonly string root=Path.Combine(Path.GetTempPath(),"PV-TrackerDeletion-"+Guid.NewGuid().ToString("N"));
    static AchievementTrackerDefinition Definition(string id="delete.custom")=>new(){Id=id,Provider="steam",GameId="42",GameTitle="Game",Title=id,AchievementId="achievement",Items=new(){new(){Id="one",Name="One",Target=10}}};
    AchievementTrackingStore Create()
    {
        var s=new AchievementTrackingStore(root);s.AddDefinition(Definition());s.AddDefinition(Definition("keep.custom"));
        s.SetManualProgress("delete.custom","player","one",3);s.SaveNote("delete.custom","player","one","Personal note");
        s.SetManualProgress("delete.custom","other","one",5);s.SaveNote("keep.custom","player","one","Keep me");return s;
    }
    [Fact]
    public void DeletionBacksUpAllAccountsAndRemovesOnlySelectedTrackerAndConnections()
    {
        var s=Create();var source=Path.Combine(root,"source.json");File.WriteAllText(source,"source file stays");
        var b=new TrackerSourceBinding{TrackerId="delete.custom",Account="player",AdapterId="pixelvault.json-file",AdapterVersion="1",Location=source};s.SaveSource(b);
        s.SaveAutomation(b.TrackerId,b.Account,b.Id,TrackerAutomationMode.SaveChanges,1,false);s.SetTrackerIcon(b.TrackerId,"trophy");
        var p=s.PrepareDeletion("delete.custom");Assert.Equal(2,p.Accounts);Assert.Equal(2,p.SavedItems);
        var backup=s.DeleteCustomTracker(p);Assert.True(File.Exists(backup));Assert.True(File.Exists(source));
        using var data=JsonDocument.Parse(File.ReadAllText(backup));var tables=data.RootElement.GetProperty("Tables");
        Assert.Equal(2,tables.GetProperty("tracker_item_state").GetArrayLength());Assert.Single(tables.GetProperty("tracker_source").EnumerateArray());
        Assert.Contains(tables.GetProperty("tracker_item_state").EnumerateArray(),r=>r.GetProperty("note").GetString()=="Personal note");
        Assert.DoesNotContain(s.Definitions(),d=>d.Id==p.Id);Assert.DoesNotContain(s.Sources(),b=>b.TrackerId==p.Id);
        Assert.Equal("Keep me",s.Load("keep.custom","player").Single().Note);Assert.Contains(s.Definitions(),d=>d.Id==AchievementTrackingCatalog.DeathStrandingId);
        using var c=new SqliteConnection("Data Source="+s.DatabasePath);c.Open();
        foreach(var table in tables.EnumerateObject())
        {using var q=c.CreateCommand();q.CommandText="SELECT COUNT(*) FROM "+table.Name+" WHERE "+(table.Name=="tracker_definition"?"id":"tracker_id")+"=$id";q.Parameters.AddWithValue("$id",p.Id);Assert.Equal(0L,q.ExecuteScalar());}
        // Explicitly reimporting the same definition cannot resurrect deleted progress or notes.
        s.AddDefinition(Definition());Assert.Null(s.Load(p.Id,"player").Single().Current);Assert.Empty(s.History(p.Id,"player"));
    }
    [Fact]
    public void BuiltinsMissingTrackersAndStaleConfirmationAreRejected()
    {
        var s=Create();Assert.Throws<InvalidDataException>(()=>s.PrepareDeletion(AchievementTrackingCatalog.DeathStrandingId));
        Assert.Throws<InvalidDataException>(()=>s.PrepareDeletion("missing"));
        var preview=s.PrepareDeletion("delete.custom");s.SaveNote(preview.Id,"other","one","New note from another window");
        Assert.Throws<InvalidOperationException>(()=>s.DeleteCustomTracker(preview));Assert.Equal("New note from another window",s.Load(preview.Id,"other").Single().Note);
        Assert.False(Directory.Exists(Path.Combine(root,"achievement-tracking","deleted-trackers")));
    }
    [Fact]
    public void BackupFailureLeavesTrackerProgressAndSourcesIntact()
    {
        var s=Create();File.WriteAllText(Path.Combine(root,"achievement-tracking","deleted-trackers"),"not a directory");
        Assert.ThrowsAny<IOException>(()=>s.DeleteCustomTracker(s.PrepareDeletion("delete.custom")));
        Assert.Equal(3,s.Load("delete.custom","player").Single().Current);Assert.Equal("Personal note",s.Load("delete.custom","player").Single().Note);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void WorkspaceDeleteRequiresConfirmationAndClosesOnlyAfterDeletion(bool confirm)=>OnSta(()=>
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));var s=Create();
        var w=new AchievementTrackerWindow(null!,s,"player","",provider:"steam",gameId:"42",trackerId:"delete.custom");
        try
        {
            Pump(w.LoadDefinitionsAsync());bool asked=false,closed=false;w.Closed+=(_,__)=>closed=true;
            w.ConfirmDeletion=p=>{asked=true;Assert.Equal("delete.custom",p.Id);return confirm;};
            Pump(w.DeleteTrackerAsync());Assert.True(asked);Assert.Equal(confirm,closed);Assert.Equal(!confirm,s.Definitions().Any(d=>d.Id=="delete.custom"));
        }
        finally{w.Close();}
    });
    [Fact]
    public void CardMilestonesShowOnlyLabelsAndExposeAccessibleStatus()=>OnSta(()=>
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));var s=new AchievementTrackingStore(root);
        var d=Definition();d.Milestones=new(){"Collected","In Binder"};d.Items=new(){new(){Id="one.ever",Name="Card",GroupId="one",Milestone="Collected"},new(){Id="one.owned",Name="Card",GroupId="one",Milestone="In Binder"}};s.AddDefinition(d);s.SetManualProgress(d.Id,"player","one.ever",1);
        var w=new AchievementTrackerWindow(null!,s,"player","",provider:"steam",gameId:"42",trackerId:d.Id);
        try
        {
            Pump(w.LoadDefinitionsAsync());var toggles=Descendants((DependencyObject)w.Content).OfType<ToggleButton>().Where(t=>t.Tag is AchievementTrackerState).ToArray();Assert.Equal(2,toggles.Length);
            Assert.Equal(new[]{"Collected","In Binder"},toggles.Select(t=>Assert.IsType<TextBlock>(Assert.Single(Assert.IsType<StackPanel>(t.Content).Children.Cast<UIElement>())).Text));
            Assert.True(toggles[0].IsChecked);Assert.False(toggles[1].IsChecked);
            Assert.Contains("Completed",System.Windows.Automation.AutomationProperties.GetHelpText(toggles[0]));
            Assert.Contains("Not Complete",System.Windows.Automation.AutomationProperties.GetHelpText(toggles[1]));
        }
        finally{w.Close();}
    });
    [Fact]
    public void OpenWorkspaceRefreshesRenamedMilestonesWithoutLosingProgress()=>OnSta(()=>
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));var s=new AchievementTrackingStore(root);
        var d=Definition();d.Milestones=new(){"Collected before","In binder"};d.Items=new(){new(){Id="one.ever",Name="Card",GroupId="one",Milestone=d.Milestones[0]},new(){Id="one.owned",Name="Card",GroupId="one",Milestone=d.Milestones[1]}};
        s.AddDefinition(d);s.SetManualProgress(d.Id,"player","one.ever",1);
        var w=new AchievementTrackerWindow(null!,s,"player","",provider:"steam",gameId:"42",trackerId:d.Id);
        try
        {
            Pump(w.LoadDefinitionsAsync());
            d.Milestones=new(){"Collected","In Binder"};d.Items[0].Milestone=d.Milestones[0];d.Items[1].Milestone=d.Milestones[1];
            using(var c=new SqliteConnection("Data Source="+s.DatabasePath))
            {c.Open();using var q=c.CreateCommand();q.CommandText="UPDATE tracker_definition SET definition=$json WHERE id=$id";q.Parameters.AddWithValue("$id",d.Id);q.Parameters.AddWithValue("$json",JsonSerializer.Serialize(d,AchievementTrackingCatalog.Json));q.ExecuteNonQuery();}
            Pump(w.ReloadAsync());
            var toggles=Descendants((DependencyObject)w.Content).OfType<ToggleButton>().Where(t=>t.Tag is AchievementTrackerState).ToArray();
            Assert.Equal(new[]{"Collected","In Binder"},toggles.Select(t=>Assert.IsType<TextBlock>(Assert.Single(Assert.IsType<StackPanel>(t.Content).Children.Cast<UIElement>())).Text));
            Assert.True(toggles[0].IsChecked);Assert.False(toggles[1].IsChecked);
        }
        finally{w.Close();}
    });
    static IEnumerable<DependencyObject> Descendants(DependencyObject value)
    {
        yield return value;
        if(value is Panel p)foreach(UIElement c in p.Children)foreach(var v in Descendants(c))yield return v;
        else if(value is Decorator d&&d.Child!=null)foreach(var v in Descendants(d.Child))yield return v;
        else if(value is ItemsControl items)foreach(var c in items.Items.OfType<DependencyObject>())foreach(var v in Descendants(c))yield return v;
        else if(value is ContentControl content&&content.Content is DependencyObject child)foreach(var v in Descendants(child))yield return v;
    }
    public void Dispose(){SqliteConnection.ClearAllPools();if(Directory.Exists(root))Directory.Delete(root,true);}
}
