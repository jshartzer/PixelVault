using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;

namespace PixelVaultNative
{
    sealed class TrackerCatalogueWindow : Window
    {
        readonly AchievementTrackingStore store;
        readonly AchievementTrackerDefinition current;
        readonly TrackerSpreadsheetForm spreadsheet;
        readonly Expander mapping;
        readonly CheckBox add,update,archive,restore;
        readonly DataGrid rows;
        readonly TextBlock summary,error,source;
        readonly Button apply;
        AchievementTrackerDefinition incoming;
        string sourceName;
        bool reading;
        internal TrackerCataloguePlan Plan { get; private set; }
        internal bool HasChanges { get; private set; }
        internal Task Pending { get; private set; }=Task.CompletedTask;
        internal TrackerCatalogueWindow(Window owner,AchievementTrackingStore store,AchievementTrackerDefinition current)
        {
            this.store=store;this.current=current;
            if(!store.CanEditCatalogue(current.Id))throw new InvalidDataException("Bundled catalogues are read-only. Import an independent definition copy to customize items.");
            if(owner?.IsVisible==true)Owner=owner;
            Title="Update tracker catalogue";Width=920;Height=850;MinWidth=580;MinHeight=500;FontSize=15;
            Background=AchievementTrackerWindow.Color("#11181D");WindowStartupLocation=Owner==null?WindowStartupLocation.CenterScreen:WindowStartupLocation.CenterOwner;
            var root=new DockPanel { Margin=new Thickness(22),Background=Background };Content=root;
            var footer=new StackPanel();DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
            error=AchievementTrackerWindow.Text("",14);footer.Children.Add(error);
            var buttons=new WrapPanel { HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,8,0,0) };footer.Children.Add(buttons);
            var undo=AchievementTrackerWindow.Button("Undo last catalogue update","Undo the latest catalogue change without changing progress or notes");undo.IsEnabled=store.HasCatalogueUndo(current.Id);buttons.Children.Add(undo);
            undo.Click+=async(_,__)=>await(Pending=UndoAsync());
            var cancel=AchievementTrackerWindow.Button("Cancel","Close catalogue review without applying the file");cancel.Click+=(_,__)=>DialogResult=false;buttons.Children.Add(cancel);
            apply=AchievementTrackerWindow.Button("Apply catalogue","Apply the selected catalogue changes to all accounts");apply.IsEnabled=false;apply.Click+=async(_,__)=>await(Pending=ApplyAsync());buttons.Children.Add(apply);
            var body=new StackPanel();root.Children.Add(new ScrollViewer { Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled });
            body.Children.Add(AchievementTrackerWindow.Text("Update tracker catalogue",24,true));body.Children.Add(AchievementTrackerWindow.Text(current.Title+" · "+current.GameTitle,17,true));
            body.Children.Add(AchievementTrackerWindow.Text("Choose item changes for this tracker. Catalogue changes apply to all accounts; saved progress and notes stay attached to the same item IDs. Archived items leave the active list and can be restored.",15));
            var choose=AchievementTrackerWindow.Button("Choose catalogue file…","Choose an XLSX, CSV or tracker definition JSON");body.Children.Add(choose);
            choose.Click+=async(_,__)=>
            {
                var picker=new OpenFileDialog { Title="Choose updated catalogue",Filter="Catalogue (*.xlsx;*.csv;*.json)|*.xlsx;*.csv;*.json",CheckFileExists=true };
                if(picker.ShowDialog(this)!=true)return;choose.IsEnabled=false;
                try{await(Pending=LoadFileAsync(picker.FileName));}catch(Exception ex){error.Text=ex.Message;}finally{choose.IsEnabled=true;}
            };
            source=AchievementTrackerWindow.Text("No file selected",14);body.Children.Add(source);
            spreadsheet=new TrackerSpreadsheetForm(this,new TextBox(),current,catalogue:true) { Visibility=Visibility.Collapsed };
            mapping=new Expander { Header="Worksheet and column mapping",Content=spreadsheet,Visibility=Visibility.Collapsed,Foreground=AchievementTrackerWindow.Color("#D9E6ED"),Margin=new Thickness(0,6,0,8) };body.Children.Add(mapping);
            spreadsheet.Changed+=()=>{if(!reading && spreadsheet.Visibility==Visibility.Visible && spreadsheet.FilePath!=null){sourceName=Path.GetFileName(spreadsheet.FilePath);source.Text=sourceName;}Preview();};
            CheckBox Option(string label,string name)
            {var box=new CheckBox { Content=label,Foreground=AchievementTrackerWindow.Color("#D9E6ED"),Margin=new Thickness(0,5,0,5) };AutomationProperties.SetName(box,name);body.Children.Add(box);box.Checked+=(_,__)=>Preview();box.Unchecked+=(_,__)=>Preview();return box;}
            add=Option("Add new items","Add new catalogue items");
            update=Option("Update existing names, categories, targets and details","Update existing catalogue metadata");
            archive=Option("Archive items missing from the file (keep their progress and notes)","Archive missing catalogue items");
            restore=Option("Restore archived items present as active in the file","Restore present archived catalogue items");
            body.Children.Add(AchievementTrackerWindow.Text("Tracker identity, achievement link, milestone columns and category goals are retained. Only mapped item metadata changes.",14));
            summary=AchievementTrackerWindow.Text("Load a file, then explicitly choose the changes to apply.",16,true);body.Children.Add(summary);
            rows=new DataGrid { IsReadOnly=true,AutoGenerateColumns=false,CanUserAddRows=false,CanUserDeleteRows=false,HeadersVisibility=DataGridHeadersVisibility.Column,RowHeight=32,Height=280,FontSize=15,Margin=new Thickness(0,10,0,0),EnableRowVirtualization=true };
            foreach(var field in new[]{("Id",120.0),("Action",150.0),("Current",240.0),("Incoming",240.0)})
            {var style=new Style(typeof(TextBlock));style.Setters.Add(new Setter(ToolTipProperty,new Binding(field.Item1)));style.Setters.Add(new Setter(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis));rows.Columns.Add(new DataGridTextColumn { Header=field.Item1,Binding=new Binding(field.Item1),Width=field.Item2,ElementStyle=style });}
            AutomationProperties.SetName(rows,"Catalogue changes preview");body.Children.Add(rows);
            TrackerFormStyle.Apply(this);AchievementCollectionUi.ApplyHighContrast(this);
        }
        internal async Task LoadFileAsync(string path)
        {
            reading=true;Plan=null;apply.IsEnabled=false;error.Text="";incoming=null;rows.ItemsSource=null;summary.Text="Reading catalogue…";
            try
            {
                sourceName=Path.GetFileName(path);source.Text=sourceName;
                if(Path.GetExtension(path).Equals(".json",StringComparison.OrdinalIgnoreCase))
                {mapping.Visibility=spreadsheet.Visibility=Visibility.Collapsed;incoming=await Task.Run(()=>TrackerDefinitionTransfer.Read(path));}
                else {mapping.Visibility=spreadsheet.Visibility=Visibility.Visible;await spreadsheet.LoadFileAsync(path);}
            }
            catch{sourceName=null;spreadsheet.Visibility=Visibility.Collapsed;throw;}
            finally{reading=false;Preview();mapping.IsExpanded=Plan==null;}
        }
        internal void Preview()
        {
            if(reading || apply==null)return;Plan=null;apply.IsEnabled=false;
            if(sourceName==null)return;
            try
            {
                var definition=incoming;
                if(definition==null){definition=TrackerCatalogueReconcile.Clone(current);definition.Items=spreadsheet.BuildDraft().Items;}
                Plan=store.PrepareCatalogue(current.Id,definition,new TrackerCatalogueOptions { AddNew=add.IsChecked==true,UpdateMetadata=update.IsChecked==true,ArchiveMissing=archive.IsChecked==true,RestorePresent=restore.IsChecked==true },sourceName);
                summary.Text=Plan.Summary;rows.ItemsSource=Plan.Rows;apply.IsEnabled=Plan.Changes>0;error.Text="";
            }
            catch(Exception ex){rows.ItemsSource=null;error.Text=ex.Message;mapping.IsExpanded=true;}
        }
        internal async Task ApplyAsync()
        {
            var plan=Plan;if(plan==null || plan.Changes==0)return;apply.IsEnabled=false;
            try{await Task.Run(()=>store.ApplyCatalogue(plan));HasChanges=true;if(IsVisible)DialogResult=true;}
            catch(Exception ex){error.Text=ex.Message;}
        }
        internal async Task UndoAsync()
        {
            try{await Task.Run(()=>store.UndoCatalogue(current.Id));HasChanges=true;if(IsVisible)DialogResult=true;}
            catch(Exception ex){error.Text=ex.Message;}
        }
    }
}
