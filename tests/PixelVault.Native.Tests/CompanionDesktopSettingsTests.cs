using System.Text.Json;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using PixelVault.Companion.Client;
using PixelVaultNative;
using Xunit;

namespace PixelVault.Native.Tests;

public sealed class CompanionDesktopSettingsTests
{
    static T Field<T>(object owner,string name)=>(T)owner.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(owner)!;
    static void Set(object owner,string name,object value)=>owner.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(owner,value);
    static IEnumerable<DependencyObject> Descendants(DependencyObject owner)
    {
        yield return owner;foreach(var child in LogicalTreeHelper.GetChildren(owner).OfType<DependencyObject>())foreach(var nested in Descendants(child))yield return nested;
    }
    static Button Button(CompanionWindow window,string label)=>Descendants(window).OfType<Button>().Single(b=>Equals(b.Content,label));
    static void Click(CompanionWindow window,string label)
    {
        Button(window,label).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));Pump(window.PendingRequest);
    }
    static void Pump(Task task)
    {
        var frame=new DispatcherFrame();
        // Capture the STA dispatcher, since the continuation runs on a worker.
        var dispatcher=Dispatcher.CurrentDispatcher;
        if(!task.IsCompleted){_=task.ContinueWith(_=>dispatcher.BeginInvoke(new Action(()=>frame.Continue=false)),TaskScheduler.Default);Dispatcher.PushFrame(frame);}
        task.GetAwaiter().GetResult();
    }

    [Fact]
    public void StandaloneConnectionOpensNoLocalStoreAndItsCloseButtonClosesTheWindow()
    {
        var root=Path.Combine(Path.GetTempPath(),"CompanionWindow-"+Guid.NewGuid().ToString("N"));
        PixelVaultNative.Tests.AchievementCollectionAccessTests.OnSta(()=>
        {
            var window=new CompanionWindow(null,root);window.Show();
            try{Assert.True(window.IsVisible);Assert.False(Field<TextBox>(window,"note").IsEnabled);Click(window,"Close");Assert.False(window.IsVisible);Assert.False(Directory.Exists(root));}
            finally{window.Close();}
        });
    }

    sealed class ViewServer:HttpMessageHandler
    {
        internal bool Offline;internal int Posts;
        readonly AchievementTrackerDefinition definition=new(){Id="fixture",Provider="demo",GameId="42",GameTitle="A test journey",Title="Desktop view",Items=new(){new(){Id="count",Name="Collect five",Target=5}}};
        int current=2;string note="Saved note";
        CompanionTrackerDocument Document()=>new(){Definition=definition,States=new(){new(){Item=definition.Items[0],ReportedCurrent=2,ManualCurrent=current==2?null:current,Note=note}},Versions=new(){["count"]=new("progress-version","note-version")}};
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation)
        {
            if(Offline)throw new HttpRequestException("Fixture connection unavailable.");
            if(request.Method==HttpMethod.Post)
            {
                Posts++;var command=(await request.Content!.ReadFromJsonAsync<TrackerEdit>(cancellation))!;
                if(command.Field=="note")note=command.Note;else current=command.Current??2;
                return new(HttpStatusCode.OK){Content=JsonContent.Create(new CompanionSaveResult(new(Posts,false),Document()))};
            }
            return new(HttpStatusCode.OK){Content=request.RequestUri!.AbsolutePath.EndsWith("/workspace")?JsonContent.Create(new CompanionWorkspace("fixture-workspace","isolated-copy",false,false,new(){["demo"]="test-account"})):JsonContent.Create(Document())};
        }
    }
    [Fact]
    public void DesktopControlsRetainANoteDuringCountSaveAndDisableOfflineEditingUntilRetry()
    {
        var root=Path.Combine(Path.GetTempPath(),"CompanionWindow-"+Guid.NewGuid().ToString("N"));
        PixelVaultNative.Tests.AchievementCollectionAccessTests.OnSta(()=>
        {
            using var server=new ViewServer();using var http=new HttpClient(server){BaseAddress=new("http://localhost")};var client=new CompanionClient(http);
            Pump(client.WorkspaceAsync());var window=new CompanionWindow(null,root);
            try
            {
                Set(window,"client",client);Set(window,"http",http);Set(window,"connected",true);Set(window,"connectedWorkspace",client.WorkspaceAsync().GetAwaiter().GetResult());
                Field<ComboBox>(window,"trackers").Items.Add(new CompanionTrackerSummary("fixture","demo","42","A test journey","Desktop view",new(0,1,0,1,0)));
                Field<ComboBox>(window,"trackers").SelectedIndex=0;Pump(window.PendingRequest);
                var note=Field<TextBox>(window,"note");note.Text="A retained draft";Field<TextBox>(window,"count").Text="4";Click(window,"Save progress");
                Assert.Equal("A retained draft",note.Text);Assert.True(Button(window,"Save note").IsEnabled);Assert.Equal(1,server.Posts);
                Assert.Equal(Visibility.Collapsed,Field<WrapPanel>(window,"loginPanel").Visibility);Click(window,"Sign in again");Assert.Equal(Visibility.Visible,Field<WrapPanel>(window,"loginPanel").Visibility);Assert.Equal("A retained draft",note.Text);
                server.Offline=true;Click(window,"Save note");Assert.False(note.IsEnabled);Assert.Equal("A retained draft",note.Text);Assert.Equal(1,server.Posts);
                Assert.Contains("Disconnected",Field<TextBlock>(window,"workspace").Text);Assert.True(Field<CompanionEditor>(window,"editor").HasUncertainSave);
                server.Offline=false;Click(window,"Reload");Click(window,"Retry unconfirmed save");Assert.Equal(2,server.Posts);Assert.Equal("A retained draft",note.Text);Assert.False(Button(window,"Save note").IsEnabled);
                Assert.False(Field<CompanionEditor>(window,"editor").HasUncertainSave);Assert.False(Directory.Exists(Path.Combine(root,"achievement-tracking")));
            }
            finally{window.Close();}
        });
    }
    [Fact]
    public void CompanionRemembersOnlyItsExplicitDestinationWithoutChangingDesktopSettingsOrCreatingATrackerDatabase()
    {
        var root=Path.Combine(Path.GetTempPath(),"CompanionDestination-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root,"settings.json"),"Desktop settings sentinel");
            Assert.Equal("http://127.0.0.1:8787",CompanionDesktopSettings.Load(root).Server);
            new CompanionDesktopSettings("https://companion.home").Save(root);
            Assert.Equal("https://companion.home/",CompanionDesktopSettings.Load(root).Server);
            var saved=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"companion-connection.json")));
            Assert.Equal(new[]{"Server","AllowHttp"},saved.RootElement.EnumerateObject().Select(p=>p.Name));
            Assert.Equal("Desktop settings sentinel",File.ReadAllText(Path.Combine(root,"settings.json")));
            Assert.False(Directory.Exists(Path.Combine(root,"achievement-tracking")));
            Assert.Equal(2,Directory.GetFiles(root).Length);
        }
        finally{Directory.Delete(root,true);}
    }
}
