using System;
using System.Threading.Tasks;

namespace PixelVaultNative
{
    sealed partial class AchievementTrackerWindow
    {
        internal Func<TrackerCatalogueWindow,bool?> ShowCatalogue=dialog=>dialog.ShowDialog();
        internal async Task ManageCatalogueAsync()
        {
            if(refreshing || selected==null)return;await PendingEdit;if(HasDraft())return;
            if(!store.CanEditCatalogue(selected.Id)){Error("Bundled catalogues are read-only. Export the definition and import an independent copy to customize its items.");return;}
            ReadBusy(true);
            try
            {
                var dialog=new TrackerCatalogueWindow(this,store,selected);
                try{ShowCatalogue(dialog);if(dialog.HasChanges){preferredId=selected.Id;await LoadDefinitionsAsync();status.Text="Catalogue updated. Saved counts and notes were retained for every account.";}}
                finally{dialog.Close();}
            }
            catch(Exception ex){Error("Could not update catalogue. "+ex.Message);}
            finally{if(!closed)ReadBusy(false);}
        }
    }
}
