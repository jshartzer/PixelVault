using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;

namespace PixelVaultNative
{
    sealed class TrackerReconcileWindow : Window
    {
        readonly TrackerRefreshPlan plan;
        readonly CheckBox older = new() { Content = "Allow replacing newer observations with this older/equal source", Margin = new Thickness(0, 10, 0, 8) };
        readonly DataGrid grid = new() { AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false, SelectionMode = DataGridSelectionMode.Single,
            EnableRowVirtualization = true, EnableColumnVirtualization = true };
        internal bool AllowOlder => older.IsChecked == true;
        internal IReadOnlyList<string> UseSourceItems { get { grid.CommitEdit(DataGridEditingUnit.Cell, true); grid.CommitEdit(DataGridEditingUnit.Row, true); return plan.Rows.Where(r => r.UseSource).Select(r => r.ItemId).ToList(); } }
        internal TrackerReconcileWindow(Window owner, TrackerRefreshPlan plan)
        {
            this.plan = plan;
            if (owner?.IsVisible == true) Owner = owner;
            Title = "Review tracker refresh"; Width = 930; Height = 650; MinWidth = 700; MinHeight = 440;
            WindowStartupLocation = Owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
            Background = AchievementTrackerWindow.Color("#11181D");
            var root = new DockPanel { Margin = new Thickness(22), Background = Background }; Content = root;
            var header = new StackPanel(); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
            header.Children.Add(AchievementTrackerWindow.Text("Review source observations", 23, true));
            header.Children.Add(AchievementTrackerWindow.Text(plan.Summary, 14, true));
            var source = AchievementTrackerWindow.Text(plan.Source, 13); source.ToolTip = plan.Evidence; header.Children.Add(source);
            header.Children.Add(AchievementTrackerWindow.Text("Source observed: " + new DateTime(plan.ObservedUtc, DateTimeKind.Utc).ToLocalTime().ToString("g"), 12));
            header.Children.Add(AchievementTrackerWindow.Text("Reported counts update by default. Manual counts and authored notes stay saved. Source notes only fill untouched empty notes. Select Use source only for manual counts you want to replace. Missing items stay unchanged.", 13));
            older.Visibility = plan.HasRollback ? Visibility.Visible : Visibility.Collapsed;
            older.Foreground = AchievementTrackerWindow.Color("#F0C497"); AutomationProperties.SetName(older, "Explicitly accept an older or conflicting equal-time source"); header.Children.Add(older);
            var footer = new StackPanel { Margin = new Thickness(0, 12, 0, 0) }; DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
            var bulk = new WrapPanel(); footer.Children.Add(bulk);
            var keep = AchievementTrackerWindow.Button("Keep all manual counts", "Keep every manual count during this refresh");
            keep.Click += (_, __) => { grid.CommitEdit(); foreach (var row in plan.Rows) row.UseSource = false; grid.Items.Refresh(); }; bulk.Children.Add(keep);
            var use = AchievementTrackerWindow.Button("Use source for all manual counts", "Explicitly replace all manual counts observed in this source");
            use.Click += (_, __) => { grid.CommitEdit(); foreach (var row in plan.Rows) row.UseSource = row.Manual.HasValue; grid.Items.Refresh(); }; bulk.Children.Add(use);
            var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right }; footer.Children.Add(buttons);
            var cancel = AchievementTrackerWindow.Button("Cancel", "Cancel review without changing progress"); cancel.Click += (_, __) => DialogResult = false; buttons.Children.Add(cancel);
            var apply = AchievementTrackerWindow.Button("Apply observations", "Apply the reviewed observations and selected conflict resolutions");
            apply.Click += (_, __) => { _ = UseSourceItems; DialogResult = true; }; buttons.Children.Add(apply);
            foreach (var column in new[] { ("Item", "Name", 140.0), ("Previous source", "PreviousText", 100.0), ("New source", "IncomingText", 90.0), ("Manual", "ManualText", 65.0), ("Availability", "Availability", 90.0), ("Review", "Result", 130.0) })
            {
                var text = new Style(typeof(TextBlock)); text.Setters.Add(new Setter(ToolTipProperty, new Binding(column.Item2)));
                grid.Columns.Add(new DataGridTextColumn { Header = column.Item1, Binding = new Binding(column.Item2), IsReadOnly = true,
                    MinWidth = column.Item3, ElementStyle = text, Width = column.Item1 == "Item" ? new DataGridLength(2, DataGridLengthUnitType.Star) : new DataGridLength(1, DataGridLengthUnitType.Star) });
            }
            var checkStyle = new Style(typeof(CheckBox)); checkStyle.Setters.Add(new Setter(IsEnabledProperty, new Binding("HasManual")));
            var noteStyle = new Style(typeof(TextBlock)); noteStyle.Setters.Add(new Setter(ToolTipProperty,new Binding("IncomingNote")));
            grid.Columns.Add(new DataGridTextColumn { Header = "Note", Binding = new Binding("NoteAction"), IsReadOnly = true, Width = 140, ElementStyle = noteStyle });
            grid.Columns.Add(new DataGridCheckBoxColumn { Header = "Use source", Binding = new Binding("UseSource") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }, Width = 85,
                ElementStyle = checkStyle, EditingElementStyle = checkStyle });
            grid.ItemsSource = plan.Rows; AutomationProperties.SetName(grid, "Refresh differences and manual conflict choices");
            grid.Margin = new Thickness(0, 10, 0, 0); root.Children.Add(grid);
            TrackerFormStyle.Apply(this);AchievementCollectionUi.ApplyHighContrast(this);
        }
    }
}
