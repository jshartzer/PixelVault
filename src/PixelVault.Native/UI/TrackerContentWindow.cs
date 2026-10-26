using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Microsoft.Win32;

namespace PixelVaultNative
{
    // Plain text only: opening or exporting content never writes tracker/game progress.
    sealed class TrackerContentWindow : Window
    {
        internal TrackerContentWindow(Window owner, AchievementTrackerItem item)
        {
            if (owner?.IsVisible == true) Owner = owner;
            Title = "Content — " + item.Name; Width = 780; Height = 680; MinWidth = 380; MinHeight = 340;
            WindowStartupLocation = Owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
            Background = SystemParameters.HighContrast ? SystemColors.WindowBrush : UiBrushHelper.FromHex("#11181D");
            TrackerFormStyle.Apply(this);
            var root = new DockPanel { Margin = new Thickness(20), Background = Background }; Content = root;
            var footer = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) }; DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
            var save = AchievementTrackerWindow.Button("Save copy…", "Save a UTF-8 text copy of this item's content"); footer.Children.Add(save);
            var close = AchievementTrackerWindow.Button("Close", "Close content"); close.IsCancel = true; close.Click += (_, __) => Close(); footer.Children.Add(close);
            var header = new StackPanel(); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
            header.Children.Add(new TextBlock { Text = item.Name, FontSize = 21, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            header.Children.Add(new TextBlock { Text = "Saved reference text · Viewing here does not mark it read in the game.", FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 12) });
            var body = new TextBox { Text = item.Content ?? "", IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, FontSize = 16, Padding = new Thickness(12), AcceptsReturn = true };
            AchievementTrackerWindow.DressEditor(body); AutomationProperties.SetName(body, "Saved content"); root.Children.Add(body);
            save.Click += (_, __) =>
            {
                var invalid = Path.GetInvalidFileNameChars();
                var name = new string(item.Name.Select(c => invalid.Contains(c) ? '_' : c).Take(120).ToArray());
                var dialog = new SaveFileDialog { Title = "Save content copy", Filter = "Text file (*.txt)|*.txt", FileName = name + ".txt", DefaultExt = ".txt", AddExtension = true };
                if (dialog.ShowDialog(this) != true) return;
                try { File.WriteAllText(dialog.FileName, item.Name + Environment.NewLine + Environment.NewLine + (item.Detail ?? "") + Environment.NewLine + Environment.NewLine + body.Text, new UTF8Encoding(false)); }
                catch (Exception ex) { MessageBox.Show(this, "The content copy could not be saved.\n" + ex.Message, "Save content", MessageBoxButton.OK, MessageBoxImage.Warning); }
            };
        }
    }
}
