using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace PixelVaultNative
{
    sealed partial class AchievementTrackerWindow
    {
        internal Func<TrackerDefinitionImportWindow,bool?> ShowDefinitionImport=dialog=>dialog.ShowDialog();
        internal async Task ImportDefinitionAsync(string path=null)
        {
            if(refreshing)return;
            await PendingEdit;if(HasDraft())return;
            if(path==null)
            {
                var file=new OpenFileDialog { Title="Import tracker definition",Filter="Tracker definition (*.json)|*.json",CheckFileExists=true };
                if(file.ShowDialog(this)!=true)return;path=file.FileName;
            }
            ReadBusy(true);
            try
            {
                var data=await Task.Run(()=> (Definition:TrackerDefinitionTransfer.Read(path),Existing:store.Definitions().Select(d=>d.Id).ToList()));
                if(closed)return;
                var dialog=new TrackerDefinitionImportWindow(this,data.Definition,data.Existing,achievements,provider,gameId,achievementId);
                try
                {
                    if(ShowDefinitionImport(dialog)!=true){status.Text="Definition import cancelled. Saved trackers were preserved.";return;}
                    var definition=dialog.Definition??dialog.BuildDefinition();
                    await Task.Run(()=>store.AddDefinition(definition));
                    if(closed)return;
                    preferredId=definition.Id;await LoadDefinitionsAsync();
                    status.Text="Tracker definition imported. Progress starts unknown; groups, targets and the selected achievement link were retained.";
                }
                finally { dialog.Close(); }
            }
            catch(Exception ex){if(!closed)Error("Could not import tracker definition. "+ex.Message);}
            finally { if(!closed)ReadBusy(false); }
        }
    }
}
