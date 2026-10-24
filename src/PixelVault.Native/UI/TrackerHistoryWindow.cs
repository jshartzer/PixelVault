using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;

namespace PixelVaultNative
{
    sealed class TrackerHistoryWindow : Window
    {
        readonly AchievementTrackingStore store;
        readonly string tracker, account;
        readonly ListBox entries = new() { DisplayMemberPath = "Display", MinHeight = 100 };
        readonly TextBlock detail = AchievementTrackerWindow.Text("", 13);
        readonly TextBlock status = AchievementTrackerWindow.Text("Choose a change to inspect it. Only the fields changed by that operation are undone.", 12);
        readonly Button undo;
        internal Task PendingOperation { get; private set; } = Task.CompletedTask;
        internal TrackerHistoryWindow(Window owner, AchievementTrackingStore store, string tracker, string account)
        {
            this.store = store; this.tracker = tracker; this.account = account;
            if (owner?.IsVisible == true) Owner = owner;
            Title = "Tracker history and undo"; Width = 760; Height = 640; MinWidth = 520; MinHeight = 420;
            WindowStartupLocation = Owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
            Background = AchievementTrackerWindow.Color("#11181D");
            var root = new DockPanel { Margin = new Thickness(22), Background = Background }; Content = root;
            var header = new StackPanel(); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
            header.Children.Add(AchievementTrackerWindow.Text("History and undo", 23, true)); header.Children.Add(status);
            var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right }; DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
            undo = AchievementTrackerWindow.Button("Undo selected change", "Undo the selected change for this tracker and account"); buttons.Children.Add(undo);
            undo.Click += async (_, __) => await (PendingOperation = UndoAsync());
            var close = AchievementTrackerWindow.Button("Close", "Close tracker history"); close.Click += (_, __) => Close(); buttons.Children.Add(close);
            var body = new Grid(); body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(180) }); body.RowDefinitions.Add(new RowDefinition()); root.Children.Add(body);
            AutomationProperties.SetName(entries, "Tracker change history"); body.Children.Add(entries);
            var scroll = new ScrollViewer { Content = detail, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 12, 0, 12) }; Grid.SetRow(scroll, 1); body.Children.Add(scroll);
            entries.SelectionChanged += (_, __) => ShowEntry();
            Reload(); TrackerFormStyle.Apply(this);AchievementCollectionUi.ApplyHighContrast(this);
        }
        void Reload() { entries.ItemsSource = store.History(tracker, account); entries.SelectedIndex = 0; ShowEntry(); }
        void ShowEntry()
        {
            var entry = entries.SelectedItem as TrackerHistoryEntry; undo.IsEnabled = entry != null && entry.UndoneUtc == 0;
            if (entry == null) { detail.Text = "No changes recorded yet. Earlier edits made before history was added cannot be undone here."; return; }
            var names = store.Definitions().First(d => d.Id == tracker).Items.ToDictionary(i => i.Id, i => i.Name);
            var changes = JsonSerializer.Deserialize<System.Collections.Generic.List<TrackerHistoryChange>>(entry.Changes, AchievementTrackingCatalog.Json);
            string Count(int? count) => count?.ToString() ?? "Unknown";
            string Manual(int? count) => count?.ToString() ?? "Use source";
            detail.Text = entry.Display + "\nAdapter: " + entry.AdapterId + " v" + entry.AdapterVersion
                + (entry.ObservedUtc > 0 ? "\nSource observed: " + new DateTime(entry.ObservedUtc, DateTimeKind.Utc).ToLocalTime().ToString("g") : "")
                + (string.IsNullOrEmpty(entry.Fingerprint) ? "" : "\nFingerprint: " + entry.Fingerprint) + "\n\n"
                + string.Join("\n\n", changes.Select(c => (names.TryGetValue(c.ItemId, out var name) ? name : c.ItemId)
                    + (c.Fields.HasFlag(TrackerChangeFields.Reported) ? "\nReported: " + Count(c.Before.Reported) + " → " + Count(c.After.Reported) + " · " + (TrackerAvailability)c.Before.Availability + " → " + (TrackerAvailability)c.After.Availability : "")
                    + (c.Fields.HasFlag(TrackerChangeFields.Manual) ? "\nManual: " + Manual(c.Before.Manual) + " → " + Manual(c.After.Manual) : "")
                    + (c.Fields.HasFlag(TrackerChangeFields.Note) ? "\nNote before: " + c.Before.Note + "\nNote after: " + c.After.Note : "")));
        }
        async Task UndoAsync()
        {
            if (entries.SelectedItem is not TrackerHistoryEntry entry) return;
            undo.IsEnabled = entries.IsEnabled = false;
            try { await Task.Run(() => store.Undo(tracker, account, entry.Id)); Reload(); status.Text = "Undid: " + entry.Label + ". Other fields and accounts were preserved."; }
            catch (Exception ex) { status.Text = ex.Message; }
            finally { entries.IsEnabled = true; ShowEntry(); }
        }
    }
}
