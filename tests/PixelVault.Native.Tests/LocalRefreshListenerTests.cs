using System.Text.Json;
using PixelVault.Companion.Client;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class LocalRefreshListenerTests
{
    [Fact]
    public void DesktopViewDefersPushedRefreshUntilEditingFinishesAndUnsubscribesWhenClosed()
    {
        AchievementCollectionAccessTests.OnSta(()=>
        {
            var root=Path.Combine(Path.GetTempPath(),"PixelVault-RefreshView-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            try
            {
                using var listener=new LocalRefreshListener(root);var window=new System.Windows.Window{Width=100,Height=100,ShowInTaskbar=false};
                bool editing=true;int renders=0;
                PixelVaultNative.SavedRefreshSubscription.Attach(window,listener,(before,after)=>after.Changed(before,"trackers"),()=>{renders++;return Task.CompletedTask;},()=>editing);
                void Pump(int ms)
                {
                    var frame=new System.Windows.Threading.DispatcherFrame();var timer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(ms)};
                    timer.Tick+=(_,__)=>{timer.Stop();frame.Continue=false;};timer.Start();System.Windows.Threading.Dispatcher.PushFrame(frame);
                }
                void Publish(long sequence)
                {
                    var snapshot=new RefreshSnapshot{WorkspaceId="test",Epoch="run",Sequence=sequence,ObservedUtc=DateTime.UtcNow.Ticks,
                        Versions=new(){["trackers"]=sequence.ToString()},Connection=JsonSerializer.SerializeToElement(new{})};
                    File.WriteAllText(Path.Combine(root,LocalRefreshListener.FileName),JsonSerializer.Serialize(snapshot,new JsonSerializerOptions(JsonSerializerDefaults.Web)));
                }
                try
                {
                    window.Show();listener.Start();Publish(1);Pump(850);Assert.Equal(0,renders);
                    Publish(2);Pump(350);editing=false;Pump(850);Assert.Equal(1,renders);
                    window.Close();Publish(3);Pump(850);Assert.Equal(1,renders);
                }
                finally{window.Close();}
            }
            finally{Directory.Delete(root,true);}
        });
    }
    [Fact]
    public async Task LocalPushCatchesUpAfterAtomicReplaceIgnoresDuplicatesAndStopsOnDispose()
    {
        var root=Path.Combine(Path.GetTempPath(),"PixelVault-Refresh-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var file=Path.Combine(root,LocalRefreshListener.FileName);
            var json=new JsonSerializerOptions(JsonSerializerDefaults.Web);
            var snapshot=new RefreshSnapshot{WorkspaceId="test",Epoch="one",Sequence=1,ObservedUtc=DateTime.UtcNow.Ticks,
                Versions=new(){["trackers"]="a"},Connection=JsonSerializer.SerializeToElement(new{})};
            void Publish(RefreshSnapshot value){File.WriteAllText(file+".tmp",JsonSerializer.Serialize(value,json));File.Move(file+".tmp",file,true);}
            Publish(snapshot);
            using var listener=new LocalRefreshListener(root);
            var received=new System.Collections.Concurrent.ConcurrentQueue<RefreshSnapshot>();
            listener.Changed+=(before,after)=>received.Enqueue(after);listener.Start();
            async Task WaitFor(Func<bool> ready){using var timeout=new CancellationTokenSource(10000);while(!ready())await Task.Delay(25,timeout.Token);}
            await WaitFor(()=>received.Count==1);Assert.True(listener.IsFresh);
            Publish(snapshot with{ObservedUtc=DateTime.UtcNow.Ticks});await Task.Delay(300);Assert.Single(received);
            Publish(snapshot with{Sequence=3,Versions=new(){["trackers"]="c"}});await WaitFor(()=>received.Count==2);
            Assert.Equal(3,listener.Current!.Sequence);
            Publish(snapshot with{Sequence=2});await Task.Delay(300);Assert.Equal(3,listener.Current.Sequence);
            Publish(snapshot with{Epoch="restart",Sequence=1});await WaitFor(()=>received.Count==3);
            File.WriteAllText(file,"incomplete");await Task.Delay(300);Assert.Equal("restart",listener.Current.Epoch);
            listener.Dispose();Publish(snapshot with{Sequence=9});await Task.Delay(300);Assert.Equal(3,received.Count);
        }
        finally{Directory.Delete(root,true);}
    }
}
