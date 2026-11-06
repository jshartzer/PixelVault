using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace PixelVaultNative
{
    sealed class TrackerWebSyncWindow:Window
    {
        readonly AchievementTrackingStore store;readonly AchievementTrackerDefinition definition;readonly string account;
        readonly StackPanel rows=new();readonly TextBlock status=AchievementTrackerWindow.Text("",14);
        internal TrackerWebSyncWindow(Window owner,AchievementTrackingStore store,AchievementTrackerDefinition definition,string account)
        {
            this.store=store;this.definition=definition;this.account=account;if(owner?.IsVisible==true)Owner=owner;
            Title=definition.Title+" — Review web edits";Width=720;Height=620;MinWidth=500;MinHeight=360;WindowStartupLocation=Owner==null?WindowStartupLocation.CenterScreen:WindowStartupLocation.CenterOwner;
            Background=SystemParameters.HighContrast?SystemColors.WindowBrush:AchievementTrackerWindow.Color("#11181D");
            var root=new DockPanel{Margin=new Thickness(22)};Content=root;
            var heading=new StackPanel();DockPanel.SetDock(heading,Dock.Top);root.Children.Add(heading);heading.Children.Add(AchievementTrackerWindow.Text("Review web edits",25,true));heading.Children.Add(AchievementTrackerWindow.Text("The same field changed independently on the PC. Choose which PC value to keep; the web's saved value is retained separately.",15));heading.Children.Add(status);
            var close=AchievementTrackerWindow.Button("Close","Close web edit review");DockPanel.SetDock(close,Dock.Bottom);root.Children.Add(close);close.Click+=(_,__)=>Close();
            root.Children.Add(new ScrollViewer{Content=rows,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
            Loaded+=async(_,__)=>await ReloadAsync();TrackerFormStyle.Apply(this);AchievementCollectionUi.ApplyHighContrast(this);
        }
        async Task ReloadAsync()
        {
            var pending=await Task.Run(()=>store.WebSyncConflicts(definition.Id,account));rows.Children.Clear();
            foreach(var conflict in pending)
            {
                var field=conflict.Incoming;var panel=new StackPanel{Margin=new Thickness(0,14,0,8)};rows.Children.Add(panel);
                panel.Children.Add(AchievementTrackerWindow.Text(field.Name+" · "+field.Field,18,true));
                string Value(int? value)=>value?.ToString()??"Use source progress";
                panel.Children.Add(AchievementTrackerWindow.Text("PC: "+(field.Field=="note"?conflict.LocalNote:Value(conflict.LocalCurrent)),15));panel.Children.Add(AchievementTrackerWindow.Text("Web: "+(field.Field=="note"?field.Note:Value(field.Current)),15));
                var buttons=new WrapPanel{Margin=new Thickness(0,8,0,0)};panel.Children.Add(buttons);
                foreach(var useWeb in new[]{false,true})
                {
                    var button=AchievementTrackerWindow.Button(useWeb?"Use web value on PC":"Keep PC value",useWeb?"Apply this web edit to the PC":"Keep this PC field and acknowledge this web edit");buttons.Children.Add(button);
                    button.Click+=async(_,__)=>{buttons.IsEnabled=false;try{await Task.Run(()=>store.ResolveWebSync(conflict,useWeb));status.Text="Choice saved.";}catch(Exception e){status.Text=e.Message;}await ReloadAsync();};
                }
            }
            if(pending.Count==0)rows.Children.Add(AchievementTrackerWindow.Text("No web edits need review for this tracker.",18));
        }
    }
}
