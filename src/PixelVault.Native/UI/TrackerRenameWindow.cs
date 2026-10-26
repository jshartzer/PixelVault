using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;

namespace PixelVaultNative
{
    sealed class TrackerRenameWindow : Window
    {
        readonly TextBox name = new() { MaxLength = 200, Padding = new Thickness(9, 7, 9, 7) };
        internal string TrackerTitle => name.Text.Trim();
        internal TrackerRenameWindow(Window owner, string title)
        {
            if (owner?.IsVisible == true) Owner = owner;
            Title = "Rename tracker";Width = 540;Height = 220;MinWidth = 380;MinHeight = 210;
            WindowStartupLocation = Owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
            Background = SystemParameters.HighContrast ? SystemColors.WindowBrush : UiBrushHelper.FromHex("#11181D");TrackerFormStyle.Apply(this);
            var root = new DockPanel { Margin = new Thickness(22), Background = Background };Content = root;
            var buttons = new WrapPanel { Margin = new Thickness(0, 16, 0, 0) };DockPanel.SetDock(buttons, Dock.Bottom);root.Children.Add(buttons);
            var save = AchievementTrackerWindow.Button("Save name", "Save this tracker's display name");save.IsDefault = true;buttons.Children.Add(save);
            var cancel = AchievementTrackerWindow.Button("Cancel", "Keep the current tracker name");cancel.IsCancel = true;buttons.Children.Add(cancel);
            var body = new StackPanel();root.Children.Add(body);
            body.Children.Add(new TextBlock { Text = "Tracker name", FontSize = 16, Margin = new Thickness(0, 0, 0, 8) });
            name.Text = title ?? "";AchievementTrackerWindow.DressEditor(name);body.Children.Add(name);AutomationProperties.SetName(name, "Tracker name");
            bool Valid() => TrackerTitle.Length is > 0 and <= 200 && !TrackerTitle.Any(char.IsControl);
            name.TextChanged += (_, __) => save.IsEnabled = Valid();
            save.IsEnabled = Valid();save.Click += (_, __) => DialogResult = true;
            Loaded += (_, __) => { name.Focus();name.SelectAll(); };
        }
    }
}
