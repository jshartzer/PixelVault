using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace PixelVaultNative
{
    sealed class TrackerAutomationWindow : Window
    {
        internal TrackerAutomationMode Mode { get; private set; }
        internal int IntervalMinutes { get; private set; }
        internal bool Paused { get; private set; }
        internal TrackerAutomationWindow(Window owner, TrackerAutomationSettings settings, TrackerAutomationState state, IEnumerable<string> history = null)
        {
            if (owner?.IsVisible == true) Owner = owner;
            Title = "Automatic tracker refresh"; Width = 580; Height = 530; MinWidth = 460; MinHeight = 360;
            WindowStartupLocation = Owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
            Background = AchievementTrackerWindow.Color("#11181D");
            var root = new DockPanel { Margin = new Thickness(22), Background = Background }; Content = root;
            var footer = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right }; DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
            var body = new StackPanel(); root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            body.Children.Add(AchievementTrackerWindow.Text("Automatic refresh", 23, true));
            body.Children.Add(AchievementTrackerWindow.Text("Runs only while PixelVault is open, for the connected account. Routine source updates apply automatically. Manual conflicts, older saves and undone refreshes need review through Refresh. Notes and manual progress are preserved.", 13));
            var modes = new ComboBox { Margin = new Thickness(0, 14, 0, 8) };
            foreach (var label in new[] { "Manual only (off)", "When this tracker opens", "After save files change", "Periodically while PixelVault runs" }) modes.Items.Add(label);
            modes.SelectedIndex = (int)settings.Mode; AutomationProperties.SetName(modes, "Automatic refresh mode"); body.Children.Add(modes);
            body.Children.Add(AchievementTrackerWindow.Text("Periodic interval in minutes (1–1440)", 13));
            var interval = new TextBox { Text = settings.IntervalMinutes.ToString(), Padding = new Thickness(6), Margin = new Thickness(0, 6, 0, 10), MaxLength = 4 };
            AchievementTrackerWindow.DressEditor(interval); AutomationProperties.SetName(interval, "Automatic refresh interval in minutes"); body.Children.Add(interval);
            void ModeChanged() => interval.IsEnabled = modes.SelectedIndex == (int)TrackerAutomationMode.Periodic;
            modes.SelectionChanged += (_, __) => ModeChanged(); ModeChanged();
            var pause = new CheckBox { Content = "Pause automatic refresh", IsChecked = settings.Paused, Foreground = AchievementTrackerWindow.Color("#D9E6ED"), Margin = new Thickness(0, 6, 0, 12) };
            body.Children.Add(pause);
            body.Children.Add(AchievementTrackerWindow.Text("Save changes are checked every 5 seconds and must settle before reading. Errors retry with a delay. Unchanged results stay quiet. Changing or disconnecting the source turns automation off until you enable it again.", 12));
            body.Children.Add(AchievementTrackerWindow.Text("Last automatic check: " + TrackerSourceWindow.Time(state.LastCheckUtc) + "\n" + state.Message, 12));
            if (history != null)
            {
                body.Children.Add(AchievementTrackerWindow.Text("Recent automatic checks", 15, true));
                foreach (var row in history) body.Children.Add(AchievementTrackerWindow.Text(row, 12));
            }
            var error = AchievementTrackerWindow.Text("", 12); body.Children.Add(error);
            var cancel = AchievementTrackerWindow.Button("Cancel", "Keep existing automatic refresh settings"); cancel.Click += (_, __) => DialogResult = false; footer.Children.Add(cancel);
            var save = AchievementTrackerWindow.Button("Save", "Save automatic refresh for this source and account"); footer.Children.Add(save);
            save.Click += (_, __) =>
            {
                if (!int.TryParse(interval.Text, out var minutes) || minutes < 1 || minutes > 1440) { error.Text = "Enter an interval from 1 to 1440 minutes."; return; }
                Mode = (TrackerAutomationMode)modes.SelectedIndex; IntervalMinutes = minutes; Paused = pause.IsChecked == true; DialogResult = true;
            };
            TrackerFormStyle.Apply(this);AchievementCollectionUi.ApplyHighContrast(this);
        }
    }
}
