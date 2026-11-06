using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace PixelVaultNative
{
    static class TrackerIconVisual
    {
        internal static readonly (string Id, string Label, string Path)[] Options =
        {
            ("checklist", "Checklist", "M4,5 L6,7 9,3 M12,5 H21 M4,12 L6,14 9,10 M12,12 H21 M4,19 L6,21 9,17 M12,19 H21"),
            ("trophy", "Achievements", "M7,3 H17 V10 Q17,15 12,15 Q7,15 7,10 Z M7,5 H3 V8 Q3,11 7,11 M17,5 H21 V8 Q21,11 17,11 M12,15 V21 M7,21 H17"),
            ("collectibles", "Collectibles", "M7,4 H17 L22,10 12,22 2,10 Z M2,10 H22 M7,4 L9,10 12,22 15,10 17,4"),
            ("exploration", "Exploration", "M12,2 A10,10 0 1 1 11.99,2 M16,8 L14,14 8,16 10,10 Z"),
            ("deliveries", "Deliveries", "M3,7 L12,2 21,7 V18 L12,23 3,18 Z M3,7 L12,12 21,7 M12,12 V23 M7.5,4.5 L16.5,9.5"),
            ("combat", "Combat", "M4,2 L19,17 17,19 2,4 Z M15,20 L20,15 M18,18 L22,22 M20,2 L14,8 M22,4 L16,10 M4,15 L9,20 M6,18 L2,22"),
            ("crafting", "Crafting", "M4,3 H13 L19,9 15,13 9,7 H4 Z M13,11 L3,21 5,23 15,13"),
            ("upgrades", "Upgrades", "M12,2 L22,12 H16 V22 H8 V12 H2 Z"),
            ("races", "Races", "M4,23 V3 Q8,0 13,3 Q17,6 22,3 V15 Q17,18 13,15 Q8,12 4,15 M10,2 V14 M16,4 V16 M4,9 Q8,6 13,9 Q17,12 22,9"),
            ("quests", "Quests", "M6,3 H19 Q23,3 23,7 H18 M19,3 V19 Q19,23 15,23 H4 Q1,23 1,19 H14 Q14,23 17,22 M6,3 V19 M9,8 H15 M9,12 H15"),
            ("building", "Building", "M2,11 L12,2 22,11 M5,9 V22 H19 V9 M10,22 V15 H14 V22"),
            ("relationships", "Relationships", "M12,21 L3,12 C-3,3 7,-2 12,6 C17,-2 27,3 21,12 Z")
        };

        internal static FrameworkElement Create(string iconId, GameAchievementsFetchService.AchievementRow achievement = null, string dataRoot = null)
        {
            var option = Options.FirstOrDefault(o => o.Id == iconId);
            if (option.Id == null) option = Options[0];
            var pen = new Pen(SystemParameters.HighContrast ? SystemColors.WindowTextBrush : AchievementTrackerWindow.Color("#9CD9DE"), 1.6)
                { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            var drawing = new DrawingGroup();
            // A transparent frame gives every symbol the same view box.
            drawing.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(-2, -2, 28, 28))));
            drawing.Children.Add(new GeometryDrawing(null, pen, Geometry.Parse(option.Path)));
            var source = new DrawingImage(drawing);source.Freeze();
            var grid = new Grid { Background = SystemParameters.HighContrast ? SystemColors.WindowBrush : AchievementTrackerWindow.Color("#20353E"), ClipToBounds = true };
            grid.Children.Add(new Image { Source = source, Stretch = Stretch.Uniform, Margin = new Thickness(16) });
            if (iconId == null && achievement != null && !string.IsNullOrWhiteSpace(achievement.IconUrlColor))
            {
                var image = new Image { Stretch = Stretch.UniformToFill };
                grid.Children.Add(image);
                // Tracker artwork stays in color regardless of achievement unlock state.
                AchievementsInfoWindow.StartAchievementBadgeDownload(image, new GameAchievementsFetchService.AchievementRow
                    { CenturyDefinitionId = achievement.CenturyDefinitionId, IconUrlColor = achievement.IconUrlColor, ProgressKnown = true, Unlocked = true }, "PixelVault/Trackers", 256, dataRoot);
            }
            AutomationProperties.SetName(grid, iconId == null && achievement != null ? "Linked achievement image" : option.Label + " icon");
            return grid;
        }

        internal static ContextMenu Menu(Action select)
        {
            var menu = new ContextMenu();var item = new MenuItem { Header = "Select Image..." };
            item.Click += (_, __) => select();menu.Items.Add(item);return menu;
        }
    }

    sealed class TrackerIconPicker : Window
    {
        internal string SelectedIconId { get; private set; }
        internal TrackerIconPicker(Window owner, string iconId, GameAchievementsFetchService.AchievementRow achievement)
        {
            if (owner?.IsVisible == true) Owner = owner;
            Title = "Select Image";Width = Height = 540;ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = Owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
            Background = SystemParameters.HighContrast ? SystemColors.WindowBrush : AchievementTrackerWindow.Color("#11181D");SelectedIconId = iconId;
            var root = new DockPanel { Margin = new Thickness(20) };Content = root;
            var heading = AchievementTrackerWindow.Text("Choose a tracker image", 22, true);heading.Margin = new Thickness(0, 0, 0, 12);DockPanel.SetDock(heading, Dock.Top);root.Children.Add(heading);
            var footer = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };DockPanel.SetDock(footer, Dock.Bottom);root.Children.Add(footer);
            var cancel = AchievementTrackerWindow.Button("Cancel", "Keep the current tracker image");DockPanel.SetDock(cancel, Dock.Right);footer.Children.Add(cancel);cancel.Click += (_, __) => DialogResult = false;
            var automatic = AchievementTrackerWindow.Button(achievement == null ? "Automatic (checklist)" : "Use achievement image", "Use linked achievement artwork, or the default checklist icon");
            automatic.MinWidth = 0;automatic.Click += (_, __) => { SelectedIconId = null;DialogResult = true; };footer.Children.Add(automatic);
            var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 4, Rows = 3 };
            root.Children.Add(grid);
            foreach (var option in TrackerIconVisual.Options)
            {
                var content = new Grid();content.RowDefinitions.Add(new RowDefinition());content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                content.Children.Add(TrackerIconVisual.Create(option.Id));var label = AchievementTrackerWindow.Text(option.Label, 14);label.HorizontalAlignment = HorizontalAlignment.Center;label.Margin = new Thickness(0, 5, 0, 4);Grid.SetRow(label, 1);content.Children.Add(label);
                var button = AchievementTrackerWindow.Button(option.Label, "Select " + option.Label + " icon");button.Content = content;button.MinWidth = 0;button.Margin = new Thickness(3);button.Padding = new Thickness(3);button.HorizontalContentAlignment = HorizontalAlignment.Stretch;button.VerticalContentAlignment = VerticalAlignment.Stretch;button.Tag = option.Id;
                if (iconId == option.Id) button.BorderBrush = AchievementTrackerWindow.Color("#9CD9DE");
                button.Click += (_, __) => { SelectedIconId = option.Id;DialogResult = true; };grid.Children.Add(button);
            }
            TrackerFormStyle.Apply(this);AchievementCollectionUi.ApplyHighContrast(this);
        }
    }
}
