using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace PixelVaultNative
{
    /// <summary>Game-independent completion-column authoring and optional workbook mapping.</summary>
    sealed class TrackerMilestoneEditor : StackPanel
    {
        readonly CheckBox enabled = new() { Content="Multiple named completion boxes per item", Margin=new Thickness(0,8,0,8) };
        readonly ComboBox count = new() { Width=80,HorizontalAlignment=HorizontalAlignment.Left };
        readonly StackPanel rows = new();
        readonly StackPanel setup = new();
        readonly List<(TextBox Name,ComboBox Header)> fields=new();
        readonly bool spreadsheet;
        readonly bool fixedLabels;
        string[] headers=Array.Empty<string>();
        internal event Action Changed;
        internal TrackerMilestoneEditor(bool spreadsheet=false,IEnumerable<string> labels=null)
        {
            this.spreadsheet=spreadsheet;var saved=labels?.ToList()??new();fixedLabels=saved.Count>0;
            Children.Add(enabled);Children.Add(setup);
            setup.Children.Add(AchievementTrackerWindow.Text("How many boxes should each item have?",14));
            for(int i=1;i<=TrackerMilestones.Maximum;i++)count.Items.Add(i);
            count.SelectedIndex=1;setup.Children.Add(count);
            setup.Children.Add(AchievementTrackerWindow.Text(spreadsheet?"Name each box and optionally map its completed column. Blank cells remain unconfirmed; unmapped boxes start without progress.":"Give each box a name, such as Discovered, Collected or Upgraded. Each box is saved independently.",14));
            setup.Children.Add(rows);
            AutomationProperties.SetName(enabled,"Use multiple completion boxes");AutomationProperties.SetName(count,"Completion boxes per item");
            enabled.Checked+=(_,__)=>Update();enabled.Unchecked+=(_,__)=>Update();count.SelectionChanged+=(_,__)=>Rebuild();
            if(fixedLabels){enabled.Content="Use a spreadsheet column for each completion box";count.SelectedIndex=saved.Count-1;enabled.IsChecked=true;Rebuild();for(int i=0;i<saved.Count;i++)fields[i].Name.Text=saved[i];count.IsEnabled=false;foreach(var f in fields)f.Name.IsReadOnly=true;}
            else Rebuild();
            Update();
        }
        void Update(){setup.Visibility=enabled.IsChecked==true?Visibility.Visible:Visibility.Collapsed;Changed?.Invoke();}
        void Rebuild()
        {
            var previous=fields.Select(f=>(f.Name.Text,f.Header.SelectedItem as string)).ToList();rows.Children.Clear();fields.Clear();
            for(int i=0;i<count.SelectedIndex+1;i++)
            {
                var row=new Grid { Margin=new Thickness(0,5,0,0) };row.ColumnDefinitions.Add(new ColumnDefinition());if(spreadsheet)row.ColumnDefinitions.Add(new ColumnDefinition());
                var name=new TextBox { Text=i<previous.Count?previous[i].Text:"Completion "+(i+1),MaxLength=80,Margin=new Thickness(0,0,8,0),Padding=new Thickness(8,5,8,5) };AchievementTrackerWindow.DressEditor(name);
                var header=new ComboBox { MinWidth=120 };Fill(header,i<previous.Count?previous[i].Item2:null);
                AutomationProperties.SetName(name,"Completion box "+(i+1)+" name");AutomationProperties.SetName(header,"Completion box "+(i+1)+" spreadsheet column");
                row.Children.Add(name);if(spreadsheet){Grid.SetColumn(header,1);row.Children.Add(header);}rows.Children.Add(row);fields.Add((name,header));
                name.TextChanged+=(_,__)=>Changed?.Invoke();header.SelectionChanged+=(_,__)=>Changed?.Invoke();
            }
            Changed?.Invoke();
        }
        void Fill(ComboBox box,string selected){box.Items.Clear();box.Items.Add("Not mapped");foreach(var h in headers)box.Items.Add(h);box.SelectedIndex=string.IsNullOrEmpty(selected)?0:Array.IndexOf(headers,selected)+1;}
        internal void SetHeaders(string[] value,IReadOnlyList<TrackerMilestoneColumn> saved=null)
        {
            headers=value??Array.Empty<string>();
            if(saved?.Count>0)enabled.IsChecked=true;
            else if(headers.Any(h=>string.Equals(h,"Milestone",StringComparison.OrdinalIgnoreCase)))enabled.IsChecked=false;
            foreach(var f in fields){var match=saved?.FirstOrDefault(c=>c.Label==f.Name.Text)?.Header??headers.FirstOrDefault(h=>string.Equals(h,f.Name.Text.Trim(),StringComparison.OrdinalIgnoreCase));Fill(f.Header,match);}
        }
        internal List<string> Labels => enabled.IsChecked==true?TrackerMilestones.Labels(fields.Select(f=>f.Name.Text)):new();
        internal bool Active => enabled.IsChecked==true;
        internal List<TrackerMilestoneColumn> Columns => enabled.IsChecked==true?fields.Select((f,i)=>new TrackerMilestoneColumn { Label=Labels[i],Header=f.Header.SelectedIndex>0?f.Header.SelectedItem as string:null }).ToList():new();
    }
}
