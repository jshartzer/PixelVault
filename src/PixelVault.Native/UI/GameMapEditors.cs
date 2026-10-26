using System;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace PixelVaultNative
{
    sealed class GameMapDetailsWindow : Window
    {
        internal GameMapDefinition Result { get; private set; }
        internal GameMapDetailsWindow(Window owner, AchievementTrackingStore store, GameMapDefinition map, string steam, string retro)
        {
            Owner = owner;Title = "Map details & links";Width = 590;Height = 700;MinWidth = 450;MinHeight = 500;WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = UiBrushHelper.FromHex("#11181D");TrackerFormStyle.Apply(this);
            Result = JsonSerializer.Deserialize<GameMapDefinition>(JsonSerializer.Serialize(map));
            var root = new DockPanel { Margin = new Thickness(22) };Content = root;
            var buttons = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };DockPanel.SetDock(buttons, Dock.Bottom);root.Children.Add(buttons);
            var panel = new StackPanel();root.Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            panel.Children.Add(GameMapsWindow.Text("Map details", 22, true));
            TextBox Field(string label, string value)
            {
                panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 12, 0, 4) });
                var box = new TextBox { Text = value ?? "", Padding = new Thickness(8), MaxLength = 2000 };AchievementTrackerWindow.DressEditor(box);panel.Children.Add(box);return box;
            }
            var title = Field("Map title", map.Title);title.MaxLength = 300;
            var region = Field("Region or floor (optional)", map.Region);
            var source = Field("Source / attribution (optional)", map.Source);
            panel.Children.Add(new TextBlock { Text = "Linked trackers (optional)", Margin = new Thickness(0, 16, 0, 6) });
            var trackers = new StackPanel();panel.Children.Add(trackers);
            foreach (var def in store.Definitions().Where(d => d.Provider == map.Provider && d.GameId == map.GameId))
                trackers.Children.Add(new CheckBox { Content = def.Title, Tag = def.Id, IsChecked = map.TrackerIds.Contains(def.Id), Margin = new Thickness(0, 4, 0, 4) });
            if (trackers.Children.Count == 0) trackers.Children.Add(GameMapsWindow.Text("No trackers created for this game yet.", 13));
            panel.Children.Add(new TextBlock { Text = "Linked achievements (optional)", Margin = new Thickness(0, 16, 0, 6) });
            var achievements = new StackPanel();
            var choices = store.AchievementChoices(steam, retro).Where(a => a.Provider == map.Provider && a.ProviderGameId == map.GameId).GroupBy(a => a.ProviderAchievementId).Select(g => g.First()).ToList();
            foreach (var id in choices.Select(a => a.ProviderAchievementId).Concat(map.AchievementIds).Distinct())
                achievements.Children.Add(new CheckBox { Content = choices.FirstOrDefault(a => a.ProviderAchievementId == id)?.Title ?? id, Tag = id, IsChecked = map.AchievementIds.Contains(id), Margin = new Thickness(0, 4, 0, 4) });
            panel.Children.Add(new ScrollViewer { Content = achievements, MaxHeight = 185, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            if (achievements.Children.Count == 0) panel.Children.Add(GameMapsWindow.Text("Open the game's achievements to load the available links.", 13));
            var error = GameMapsWindow.Text("", 13);panel.Children.Add(error);
            var save = GameMapsWindow.Button("Save map", "Save map details and links");save.IsDefault = true;buttons.Children.Add(save);
            save.Click += (_, __) =>
            {
                Result.Title = title.Text.Trim();Result.Region = region.Text.Trim();Result.Source = source.Text.Trim();
                // Retain links whose tracker is temporarily unavailable, unless a visible checkbox changed it.
                var visible = trackers.Children.OfType<CheckBox>().ToList();
                Result.TrackerIds = map.TrackerIds.Where(id => !visible.Any(c => (string)c.Tag == id)).Concat(visible.Where(c => c.IsChecked == true).Select(c => (string)c.Tag)).Distinct().ToList();
                Result.AchievementIds = achievements.Children.OfType<CheckBox>().Where(c => c.IsChecked == true).Select(c => (string)c.Tag).ToList();
                try { GameMapStore.Validate(Result);DialogResult = true; } catch (Exception ex) { error.Text = ex.Message; }
            };
            var cancel = GameMapsWindow.Button("Cancel", "Keep existing map details");cancel.IsCancel = true;buttons.Children.Add(cancel);
        }
    }

    sealed class GameMapPinWindow : Window
    {
        sealed record TrackerChoice(AchievementTrackerDefinition Definition)
        {
            public override string ToString() => Definition.Title;
        }
        sealed record ItemChoice(AchievementTrackerItem Item)
        {
            public override string ToString() => Item.Name + (string.IsNullOrEmpty(Item.Milestone) ? "" : " · " + Item.Milestone);
        }
        internal GameMapMarker Result { get; private set; }
        internal bool Remove { get; private set; }
        internal GameMapPinWindow(Window owner, AchievementTrackingStore store, GameMapDefinition map, GameMapMarker marker)
        {
            Owner = owner;Title = "Map pin";Width = 560;Height = Math.Min(620, SystemParameters.WorkArea.Height - 50);MinWidth = 440;MinHeight = 430;WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = UiBrushHelper.FromHex("#11181D");TrackerFormStyle.Apply(this);
            Result = JsonSerializer.Deserialize<GameMapMarker>(JsonSerializer.Serialize(marker));
            var root = new DockPanel { Margin = new Thickness(22) };Content = root;
            var buttons = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };DockPanel.SetDock(buttons, Dock.Bottom);root.Children.Add(buttons);
            var panel = new StackPanel();root.Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            TextBox Field(string label, string text, bool multiline = false)
            {
                panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 10, 0, 5) });var box = new TextBox { Text = text ?? "", Padding = new Thickness(8), AcceptsReturn = multiline, TextWrapping = TextWrapping.Wrap, MinHeight = multiline ? 85 : 32, MaxLength = multiline ? 10000 : 300 };AchievementTrackerWindow.DressEditor(box);panel.Children.Add(box);return box;
            }
            var name = Field("Pin name", marker.Label);var detail = Field("Description", marker.Detail, true);
            panel.Children.Add(new TextBlock { Text = "Tracker item for completion colour (optional)", Margin = new Thickness(0, 12, 0, 5) });
            var trackers = new ComboBox();trackers.Items.Add(new TrackerChoice(new AchievementTrackerDefinition { Id = "", Title = "No linked tracker" }));
            foreach (var d in store.Definitions().Where(d => d.Provider == map.Provider && d.GameId == map.GameId)) trackers.Items.Add(new TrackerChoice(d));
            if (!string.IsNullOrEmpty(marker.TrackerId) && !trackers.Items.Cast<TrackerChoice>().Any(d => d.Definition.Id == marker.TrackerId))
                trackers.Items.Add(new TrackerChoice(new AchievementTrackerDefinition { Id = marker.TrackerId, Title = "Unavailable tracker (keep link)", Items = new() { new() { Id = marker.ItemId, Name = "Existing item link" } } }));
            trackers.SelectedItem = trackers.Items.Cast<TrackerChoice>().FirstOrDefault(d => d.Definition.Id == marker.TrackerId) ?? trackers.Items[0];panel.Children.Add(trackers);
            var items = new ComboBox { Margin = new Thickness(0, 8, 0, 0) };panel.Children.Add(items);
            void Populate()
            {
                var definition = (trackers.SelectedItem as TrackerChoice)?.Definition;
                var choices = definition?.Items.Where(i => !i.Archived || (definition.Id == marker.TrackerId && i.Id == marker.ItemId)).ToList();
                if (definition?.Id == marker.TrackerId && !string.IsNullOrEmpty(marker.ItemId) && choices != null && !choices.Any(i => i.Id == marker.ItemId))
                    choices.Add(new AchievementTrackerItem { Id = marker.ItemId, Name = "Unavailable item (keep link)" });
                items.ItemsSource = choices?.Select(i => new ItemChoice(i)).ToList();
                items.SelectedItem = items.Items.Cast<ItemChoice>().FirstOrDefault(i => i.Item.Id == marker.ItemId);
            }
            trackers.SelectionChanged += (_, __) => Populate();Populate();
            var error = GameMapsWindow.Text("", 13);panel.Children.Add(error);
            var save = GameMapsWindow.Button("Save pin", "Save this map marker");save.IsDefault = true;buttons.Children.Add(save);
            save.Click += (_, __) =>
            {
                if (string.IsNullOrWhiteSpace(name.Text)) { error.Text = "Enter a pin name.";return; }
                var id = (trackers.SelectedItem as TrackerChoice)?.Definition.Id;
                if (!string.IsNullOrEmpty(id) && items.SelectedItem == null) { error.Text = "Choose a tracker item, or No linked tracker.";return; }
                Result.Label = name.Text.Trim();Result.Detail = detail.Text;Result.TrackerId = id;Result.ItemId = (items.SelectedItem as ItemChoice)?.Item.Id;DialogResult = true;
            };
            if (map.Markers.Any(m => m.Id == marker.Id))
            {
                var remove = GameMapsWindow.Button("Remove pin", "Remove this map marker");buttons.Children.Add(remove);
                remove.Click += (_, __) => { Remove = true;DialogResult = true; };
            }
            var cancel = GameMapsWindow.Button("Cancel", "Keep existing pin");cancel.IsCancel = true;buttons.Children.Add(cancel);
        }
    }

    /// <summary>Context-prefilled access to the universal map library.</summary>
    sealed class GameMapLinks : StackPanel
    {
        internal void Bind(AchievementTrackingStore store, string provider, string gameId, string gameTitle, string steam, string retro, string achievement = null, string tracker = null)
        {
            Children.Clear();Visibility = Visibility.Collapsed;
            if (store == null || string.IsNullOrEmpty(provider) || string.IsNullOrEmpty(gameId)) return;
            var count = store.Maps.List(provider, gameId, achievement, tracker).Count;
            var button = GameMapsWindow.Button(count == 0 ? "Link or add map…" : "Open linked maps (" + count + ")", "Open maps linked to this achievement or tracker, or add another map");
            button.HorizontalAlignment = HorizontalAlignment.Left;button.FontSize = 13;
            button.Click += (_, __) => new GameMapsWindow(Window.GetWindow(this), store, new AchievementHubGame { Provider = provider, Id = gameId, Title = gameTitle }, steam, retro, achievement, tracker).Show();
            Children.Add(button);Visibility = Visibility.Visible;
        }
    }
}
