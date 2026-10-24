using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;

namespace PixelVaultNative
{
    sealed class TrackerDefinitionImportWindow : Window
    {
        readonly AchievementTrackerDefinition original;
        readonly HashSet<string> existingIds;
        readonly TextBox title;
        readonly CheckBox copy;
        readonly TrackerAchievementPicker link;
        internal AchievementTrackerDefinition Definition { get; private set; }
        internal TrackerDefinitionImportWindow(Window owner, AchievementTrackerDefinition definition, IEnumerable<string> existingIds,
            IEnumerable<GameAchievementsFetchService.AchievementRow> achievements, string provider=null,string gameId=null,string achievementId=null)
        {
            TrackerDefinitionTransfer.ValidateContext(definition,provider,gameId);
            original=definition;this.existingIds=existingIds.ToHashSet(StringComparer.Ordinal);
            if(owner?.IsVisible==true)Owner=owner;
            Title="Import tracker definition";Width=760;Height=740;MinWidth=540;MinHeight=500;
            WindowStartupLocation=Owner==null?WindowStartupLocation.CenterScreen:WindowStartupLocation.CenterOwner;
            Background=AchievementTrackerWindow.Color("#11181D");FontSize=15;
            var root=new DockPanel { Margin=new Thickness(22),Background=Background };Content=root;
            var footer=new StackPanel();DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
            var error=AchievementTrackerWindow.Text("",14);footer.Children.Add(error);
            var buttons=new WrapPanel { HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0) };footer.Children.Add(buttons);
            var cancel=AchievementTrackerWindow.Button("Cancel","Cancel definition import without saving");cancel.Click+=(_,__)=>DialogResult=false;buttons.Children.Add(cancel);
            var save=AchievementTrackerWindow.Button("Import definition","Create this tracker without importing personal progress");buttons.Children.Add(save);
            var body=new StackPanel();root.Children.Add(new ScrollViewer { Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled });
            body.Children.Add(AchievementTrackerWindow.Text("Import tracker definition",24,true));
            body.Children.Add(AchievementTrackerWindow.Text(definition.GameTitle+" · "+definition.Provider+" / "+definition.GameId,16,true));
            body.Children.Add(AchievementTrackerWindow.Text("Creates a tracker with unknown progress. Your existing trackers, notes and progress stay saved.",15));
            body.Children.Add(AchievementTrackerWindow.Text("Tracker title",15));
            title=new TextBox { Text=definition.Title,MaxLength=200,Padding=new Thickness(6),Margin=new Thickness(0,5,0,12) };
            AchievementTrackerWindow.DressEditor(title);AutomationProperties.SetName(title,"Imported tracker title");body.Children.Add(title);
            body.Children.Add(AchievementTrackerWindow.Text("Linked achievement (optional)",15));
            link=new TrackerAchievementPicker(achievements) { Margin=new Thickness(0,5,0,12) };
            link.SelectGame(definition.Provider,definition.GameId,achievementId??definition.AchievementId);body.Children.Add(link);
            copy=new CheckBox { Content="Create an independent copy",Foreground=AchievementTrackerWindow.Color("#D9E6ED"),Margin=new Thickness(0,5,0,8) };
            AutomationProperties.SetName(copy,"Create an independent copy of the imported tracker");body.Children.Add(copy);
            bool duplicate=this.existingIds.Contains(definition.Id);
            body.Children.Add(AchievementTrackerWindow.Text(duplicate?"This tracker already exists. Select independent copy to import it with a new tracker identity. Item IDs, groups and targets are retained.":"The exported tracker identity will be retained. Select independent copy to create a separate tracker instead.",14));
            save.IsEnabled=!duplicate;copy.Checked+=(_,__)=>save.IsEnabled=true;copy.Unchecked+=(_,__)=>save.IsEnabled=!duplicate;
            var groups=definition.Milestones.Count==0?definition.Items.Count:definition.Items.Select(i=>i.GroupId).Distinct().Count();
            body.Children.Add(AchievementTrackerWindow.Text(groups+" records · "+definition.Items.Count+" items"+(definition.Milestones.Count>0?" · "+string.Join(" / ",definition.Milestones):""),17,true));
            if(definition.CategoryTargets.Count>0)body.Children.Add(AchievementTrackerWindow.Text(string.Join(" · ",definition.CategoryTargets.Select(p=>p.Key+": "+p.Value)),15));
            body.Children.Add(AchievementTrackerWindow.Text("Preview: first 25 items. All items are validated before import.",14));
            var preview=new DataGrid { IsReadOnly=true,AutoGenerateColumns=false,CanUserAddRows=false,CanUserDeleteRows=false,HeadersVisibility=DataGridHeadersVisibility.Column,Height=240,FontSize=15,Margin=new Thickness(0,8,0,0),ItemsSource=definition.Items.OrderBy(i=>i.Order).Take(25) };
            foreach(var field in new[]{("Name",230.0),("Category",130.0),("Target",70.0),("Milestone",100.0),("GroupId",130.0)})
                preview.Columns.Add(new DataGridTextColumn { Header=field.Item1,Binding=new Binding(field.Item1),Width=field.Item2 });
            AutomationProperties.SetName(preview,"Imported definition items preview");body.Children.Add(preview);
            save.Click+=(_,__)=> { try { Definition=BuildDefinition();DialogResult=true; }catch(Exception ex){error.Text=ex.Message;} };
            TrackerFormStyle.Apply(this);AchievementCollectionUi.ApplyHighContrast(this);
        }
        internal AchievementTrackerDefinition BuildDefinition()=>TrackerDefinitionTransfer.Prepare(original,existingIds,copy.IsChecked==true,title.Text,link.Achievement?.ProviderAchievementId);
    }
}
