using System;
using System.Threading.Tasks;
using System.Windows;

namespace PixelVaultNative
{
    static class TrackerDeletionUi
    {
        internal static bool Confirm(Window owner, TrackerDeletionPreview preview)
        {
            var text="Delete \""+preview.Title+"\" from "+preview.GameTitle+"?\n\n"
                +"This removes the custom tracker, its saved progress and notes for all accounts, and its automatic refresh connections. "
                +"Because trackers are shared, it will also disappear from Companion.\n\n"
                +"A recovery copy will be saved in achievement-tracking\\deleted-trackers. Game saves and achievement unlocks stay unchanged.";
            return (owner?.IsVisible==true
                ?MessageBox.Show(owner,text,"Delete tracker",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)
                :MessageBox.Show(text,"Delete tracker",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No))==MessageBoxResult.Yes;
        }
    }

    sealed partial class AchievementTrackerWindow
    {
        internal async Task DeleteTrackerAsync()
        {
            if(selected==null||refreshing||loadingDefinitions)return;
            await PendingEdit;if(HasDraft())return;
            var id=selected.Id;ReadBusy(true);
            try
            {
                var preview=await Task.Run(()=>store.PrepareDeletion(id));
                if(closed||!ConfirmDeletion(preview))return;
                var backup=await Task.Run(()=>store.DeleteCustomTracker(preview));
                if(closed)return;
                if(fixedTrackerId!=null){Close();return;}
                preferredId=null;await LoadDefinitionsAsync();
                status.Text="Tracker deleted. Recovery copy: "+backup;
            }
            catch(Exception ex){if(!closed)Error("Could not delete tracker. "+ex.Message);}
            finally{if(!closed)ReadBusy(false);}
        }
    }
}
