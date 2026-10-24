using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using Microsoft.Win32;

namespace PixelVaultNative
{
    sealed class TrackerSpreadsheetForm : StackPanel
    {
        readonly ComboBox sheet = new();
        readonly TextBox checklistTitle;
        readonly ComboBox[] columns = Enumerable.Range(0, 11).Select(_ => new ComboBox { MinWidth = 150, Margin = new Thickness(8, 3, 0, 3) }).ToArray();
        readonly TextBlock feedback = AchievementTrackerWindow.Text("Choose an .xlsx or CSV file. First row is the header. Blank progress is unknown; zero is a recorded zero. Existing notes and manual counts are preserved.", 13);
        readonly DataGrid preview = new() { IsReadOnly = true, AutoGenerateColumns = false, Height = 170, FontSize = 14, HeadersVisibility = DataGridHeadersVisibility.Column, CanUserAddRows = false, CanUserDeleteRows = false, Margin = new Thickness(0, 10, 0, 0) };
        bool mapping;
        bool loading;
        readonly AchievementTrackerDefinition existing;
        readonly bool catalogue;
        readonly TrackerMilestoneEditor completion;
        internal event Action Changed;
        internal string FilePath { get; private set; }
        internal long ObservedUtc { get; private set; }
        internal string Fingerprint { get; private set; }
        internal TrackerSpreadsheetForm(Window owner, TextBox title, AchievementTrackerDefinition existing = null,bool catalogue=false)
        {
            checklistTitle = title; this.existing = existing;this.catalogue=catalogue;
            var file = AchievementTrackerWindow.Button("Choose spreadsheet…", "Choose an Excel workbook or CSV to create a checklist"); Children.Add(file);
            if(catalogue)file.Visibility=Visibility.Collapsed;
            Children.Add(AchievementTrackerWindow.Text("Worksheet", 13)); Children.Add(sheet);
            AutomationProperties.SetName(sheet, "Import worksheet");
            if(!catalogue)
            {
                completion=new TrackerMilestoneEditor(true,existing?.Milestones);Children.Add(completion);
                completion.Changed+=()=> { if(!mapping)Preview(); };
            }
            var optional=new StackPanel();var advanced=new Expander { Header="Optional fields and existing progress",Content=optional,Margin=new Thickness(0,8,0,8) };
            Children.Add(advanced);
            string[] labels = { existing == null ? "Item name (required)" : "Item name (optional)", "Category", "Target count", "Details", "Stable item ID", "Current count", "Completed", "Availability", "Notes","Record ID","Milestone" };
            for (int i = 0; i < columns.Length; i++)
            {
                var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) }); row.ColumnDefinitions.Add(new ColumnDefinition());
                row.Children.Add(AchievementTrackerWindow.Text(labels[i], 14)); Grid.SetColumn(columns[i], 1); row.Children.Add(columns[i]);
                if(i==0)Children.Insert(Children.IndexOf(advanced),row);else optional.Children.Add(row);
                if(catalogue && i>=5 && i<=8)row.Visibility=Visibility.Collapsed;
                AutomationProperties.SetName(columns[i], "Spreadsheet " + labels[i] + " column");
                columns[i].SelectionChanged += (_, __) => { if (!mapping) Preview(); };
            }
            Children.Add(feedback);
            string[] fields = { "Name", "Target", "Current", "Availability", "Note" }; double[] widths = { 160, 70, 100, 110, 240 };
            for (int i = 0; i < fields.Length; i++) preview.Columns.Add(new DataGridTextColumn { Header = fields[i], Binding = new System.Windows.Data.Binding(fields[i]), Width = widths[i] });
            AutomationProperties.SetName(preview, "Spreadsheet checklist preview"); Children.Add(preview);
            if(catalogue)preview.Visibility=Visibility.Collapsed;
            file.Click += async (_, __) =>
            {
                var dialog = new OpenFileDialog { Title = "Create checklist from spreadsheet", Filter = "Excel workbook or CSV (*.xlsx;*.csv)|*.xlsx;*.csv", CheckFileExists = true };
                if (dialog.ShowDialog(owner) != true) return;
                file.IsEnabled = false;
                try
                {
                    await LoadFileAsync(dialog.FileName);
                }
                catch (Exception ex) { sheet.ItemsSource = null; preview.ItemsSource = null; feedback.Text = ex.Message; }
                finally { file.IsEnabled = true; }
            };
            sheet.SelectionChanged += (_, __) =>
            {
                mapping = true;
                try
                {
                    var headers = (sheet.SelectedItem as TrackerImportSheet)?.Headers ?? Array.Empty<string>();
                    string[][] aliases = { new[] { "name", "item", "item name", "title" }, new[] { "category", "group" }, new[] { "target", "target count", "quantity" }, new[] { "detail", "details", "description" }, new[] { "id", "item id", "stable id" }, new[] { "current", "current count", "progress" }, new[] { "completed", "complete", "done" }, new[] { "availability", "available state" }, new[] { "notes", "note" },new[]{"groupid","group id","recordid","record id"},new[]{"milestone"} };
                    for (int i = 0; i < columns.Length; i++)
                    {
                        columns[i].Items.Clear(); columns[i].Items.Add("Not mapped");
                        for (int col = 0; col < headers.Length; col++) columns[i].Items.Add((col + 1) + ": " + (headers[col] ?? "(blank)"));
                        var match = Array.FindIndex(headers, h => aliases[i].Contains((h ?? "").Trim().ToLowerInvariant()));
                        columns[i].SelectedIndex = match + 1;
                        if(catalogue && i>=5 && i<=8)columns[i].SelectedIndex=0;
                    }
                    completion?.SetHeaders(headers);
                }
                finally { mapping = false; }
                Preview();
            };
        }
        internal async Task LoadFileAsync(string path, TrackerSpreadsheetMapping saved = null)
        {
            loading = true;
            sheet.ItemsSource = null; preview.ItemsSource = null; feedback.Text = "Reading spreadsheet…";
            try
            {
                var read = await Task.Run(() => TrackerSpreadsheetProgress.ReadStable(path));
                FilePath = path; ObservedUtc = read.Ticks; Fingerprint = read.Fingerprint;
                sheet.ItemsSource = read.Sheets; sheet.SelectedItem = read.Sheets.FirstOrDefault(s => s.Name == saved?.Worksheet) ?? read.Sheets.First();
                if (saved != null)
                {
                    mapping = true; var headers = ((TrackerImportSheet)sheet.SelectedItem).Headers;
                    try { for (int i=0;i<columns.Length;i++) columns[i].SelectedIndex = string.IsNullOrEmpty(saved.Headers[i]) ? 0 : Array.IndexOf(headers,saved.Headers[i])+1; }
                    finally { mapping = false; }
                    completion?.SetHeaders(headers,saved.CompletionColumns);
                    if (!read.Sheets.Any(s=>s.Name==saved.Worksheet) || saved.Headers.Any(h=>!string.IsNullOrEmpty(h)&&!headers.Contains(h)))
                        throw new InvalidDataException("A saved worksheet/header is missing. Choose the worksheet and map its columns again.");
                }
                if (string.IsNullOrWhiteSpace(checklistTitle.Text)) checklistTitle.Text = Path.GetFileNameWithoutExtension(path);
            }
            finally { loading = false; }
            Preview();
        }
        internal List<AchievementTrackerItem> BuildItems()
            => BuildDraft().Items;
        internal TrackerSpreadsheetMapping BuildMapping()
        {
            if (loading) throw new InvalidDataException("Wait for the spreadsheet to finish loading.");
            var selected = sheet.SelectedItem as TrackerImportSheet ?? throw new InvalidDataException("Choose a spreadsheet and worksheet first.");
            string Header(int i) => completion?.Active==true && (i==5||i==6||i==9||i==10) || columns[i].SelectedIndex <= 0 ? null : selected.Headers[columns[i].SelectedIndex-1];
            return new TrackerSpreadsheetMapping { Worksheet=selected.Name,Name=Header(0),Category=Header(1),Target=Header(2),Detail=Header(3),Id=Header(4),Current=Header(5),Completed=Header(6),Availability=Header(7),Note=Header(8),Group=Header(9),Milestone=Header(10),CompletionColumns=completion?.Columns??new() };
        }
        internal TrackerSpreadsheetDraft BuildDraft()
        {
            var map=BuildMapping();return catalogue?new TrackerSpreadsheetDraft { Items=TrackerCatalogueReconcile.FromSpreadsheet((TrackerImportSheet)sheet.SelectedItem,map,existing).Items }:TrackerSpreadsheetProgress.Build((TrackerImportSheet)sheet.SelectedItem,map,existing);
        }
        void Preview()
        {
            try
            {
                var draft=BuildDraft();var progress=draft.Progress.ToDictionary(p=>p.ItemId);
                var items=existing == null ? draft.Items : draft.Items.Where(i=>progress.ContainsKey(i.Id)).ToList();
                preview.ItemsSource=items.Take(25).Select(i=>new { Name=i.Name+(string.IsNullOrEmpty(i.Milestone)?"":" · "+i.Milestone),i.Target,Current=progress.TryGetValue(i.Id,out var p)?p.Current?.ToString()??"Not confirmed":"Not confirmed",Availability=progress.GetValueOrDefault(i.Id)?.Availability.ToString()??"Unknown",Note=progress.GetValueOrDefault(i.Id)?.Note??"" }).ToList();
                feedback.Text=catalogue?draft.Items.Count+" catalogue rows. Choose changes in the catalogue preview; progress/notes are not imported.":items.Count+" items ready. First 25 shown. Blank counts = unknown; 0 = zero. Existing notes/manual counts stay saved. "
                    +(existing==null?"Review game/link before creating.":"Stable IDs match existing items; missing rows stay unchanged. Save source, then Refresh to review progress.");
            }
            catch (Exception ex) { preview.ItemsSource = null; feedback.Text = ex.Message; }
            Changed?.Invoke();
        }
    }
}
