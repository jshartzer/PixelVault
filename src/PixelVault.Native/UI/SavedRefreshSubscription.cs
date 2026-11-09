using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using PixelVault.Companion.Client;

namespace PixelVaultNative
{
    // One pending refresh per open view; no remote reads and no overlapping renders.
    internal static class SavedRefreshSubscription
    {
        internal static void Attach(Window window,LocalRefreshListener listener,
            Func<RefreshSnapshot,RefreshSnapshot,bool> relevant,Func<Task> refresh,Func<bool> busy=null)
        {
            if(listener==null)return;
            bool pending=false,running=false,closed=false;long revision=0;
            var timer=new DispatcherTimer(DispatcherPriority.Background,window.Dispatcher){Interval=TimeSpan.FromMilliseconds(500)};
            async void Tick(object sender,EventArgs args)
            {
                if(closed||running||!pending||!window.IsLoaded||busy?.Invoke()==true)return;
                running=true;var version=revision;
                try{await refresh();if(version==revision)pending=false;}
                catch{/* A later tick retries; the current view stays intact. */}
                finally{running=false;if(!pending)timer.Stop();}
            }
            void Changed(RefreshSnapshot before,RefreshSnapshot after)
            {
                if(!relevant(before,after))return;
                window.Dispatcher.BeginInvoke(new Action(()=>{if(closed)return;pending=true;revision++;timer.Start();}));
            }
            listener.Changed+=Changed;timer.Tick+=Tick;
            window.Closed+=(_,__)=>{closed=true;listener.Changed-=Changed;timer.Stop();timer.Tick-=Tick;};
        }
    }
}
