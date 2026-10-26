using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace PixelVaultNative
{
    /// <summary>The same image library, links and marker controls are used for every game.</summary>
    sealed class GameMapsWindow : Window
    {
        readonly AchievementTrackingStore tracking;
        readonly AchievementHubGame game;
        readonly string steam, retro, achievement, tracker;
        readonly ListBox maps = new() { MaxHeight = 150, DisplayMemberPath = "Title" };
        readonly ListBox pins = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch };
        readonly Canvas canvas = new() { Background = Brushes.Black };
        readonly ScrollViewer viewport = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        readonly Slider zoom = new() { Minimum = 0.025, Maximum = 3, Value = 0.1, Width = 160, VerticalAlignment = VerticalAlignment.Center };
        readonly TextBlock info = new() { TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 90, FontSize = 13 };
        readonly TextBlock attribution = new() { TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 40, FontSize = 12 };
        readonly TextBox search = new() { Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 6, 0, 6) };
        readonly CheckBox remaining = new() { Content = "Remaining only", Margin = new Thickness(0, 4, 0, 8) };
        readonly Button properties, addPin, editPin;
        GameMapDefinition selected;
        BitmapSource image;
        bool placing, loading, showAll;
        Point? dragStart;
        double dragX, dragY;
        readonly Dictionary<(string Tracker, string Item), AchievementTrackerState> progress = new();

        internal GameMapsWindow(Window owner, AchievementTrackingStore tracking, AchievementHubGame game, string steam, string retro, string achievement = null, string tracker = null)
        {
            this.tracking = tracking;this.game = game;this.steam = steam;this.retro = retro;this.achievement = achievement;this.tracker = tracker;
            owner = TrackerNavigation.BrowseOwner(owner);if (owner?.IsVisible == true) Owner = owner;
            Title = "Maps — " + game.Title;Width = 1320;Height = Math.Min(900, SystemParameters.WorkArea.Height - 40);MinWidth = 840;MinHeight = 600;
            WindowStartupLocation = Owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
            Background = UiBrushHelper.FromHex("#11181D");TrackerFormStyle.Apply(this);
            AchievementTrackerWindow.DressEditor(search);
            var root = new DockPanel { Margin = new Thickness(20), Background = Background };Content = root;
            var header = new StackPanel();DockPanel.SetDock(header, Dock.Top);root.Children.Add(header);
            header.Children.Add(Text(game.Title + " · Maps", 24, true));
            var actions = new WrapPanel { Margin = new Thickness(0, 10, 0, 12) };header.Children.Add(actions);
            var import = Button("Add map…", "Save a PNG or JPEG in this game's map library");actions.Children.Add(import);
            import.Click += async (_, __) => await ImportAsync();
            properties = Button("Map details & links…", "Edit the title, source, and achievement or tracker links");actions.Children.Add(properties);
            properties.Click += async (_, __) => await EditAsync();
            if (!string.IsNullOrEmpty(achievement) || !string.IsNullOrEmpty(tracker))
            {
                var all = Button("Show all game maps", "Switch between linked maps and this game's whole map library");actions.Children.Add(all);
                all.Click += (_, __) => { showAll = !showAll;all.Content = showAll ? "Show linked maps" : "Show all game maps";Reload(); };
            }
            var reload = Button("Reload progress", "Read current saved tracker progress for marker colours");actions.Children.Add(reload);
            reload.Click += (_, __) => LoadProgress();
            var close = Button("Close", "Close maps");close.Click += (_, __) => Close();actions.Children.Add(close);
            var bottom = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };DockPanel.SetDock(bottom, Dock.Bottom);root.Children.Add(bottom);
            bottom.Children.Add(info);bottom.Children.Add(attribution);
            var grid = new Grid();grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });grid.ColumnDefinitions.Add(new ColumnDefinition());root.Children.Add(grid);
            var side = new DockPanel { Margin = new Thickness(0, 0, 16, 0) };grid.Children.Add(side);
            var sideTop = new StackPanel();DockPanel.SetDock(sideTop, Dock.Top);side.Children.Add(sideTop);
            sideTop.Children.Add(Text("SAVED MAPS", 12, true));sideTop.Children.Add(maps);
            sideTop.Children.Add(search);search.ToolTip = "Search map markers";AutomationProperties.SetName(search, "Search map markers");
            sideTop.Children.Add(Text("Pins · gold = remaining · green = complete", 11));
            sideTop.Children.Add(remaining);
            var pinActions = new WrapPanel();sideTop.Children.Add(pinActions);
            addPin = Button("Add pin", "Click a location on the map to add a pin");pinActions.Children.Add(addPin);
            addPin.Click += (_, __) => { placing = !placing;addPin.Content = placing ? "Cancel pin" : "Add pin";canvas.Cursor = placing ? Cursors.Cross : Cursors.Arrow; };
            editPin = Button("Edit pin…", "Edit the selected pin or its tracker item link");pinActions.Children.Add(editPin);
            editPin.Click += (_, __) => EditMarker();
            side.Children.Add(pins);
            var mapHost = new DockPanel();Grid.SetColumn(mapHost, 1);grid.Children.Add(mapHost);
            var tools = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };DockPanel.SetDock(tools, Dock.Top);mapHost.Children.Add(tools);
            var fit = Button("Fit", "Fit the full map in the window");fit.Click += (_, __) => Fit();tools.Children.Add(fit);
            tools.Children.Add(Text("Zoom", 13));tools.Children.Add(zoom);
            tools.Children.Add(Text("  Drag to pan · Ctrl + wheel to zoom", 12));
            viewport.Content = canvas;mapHost.Children.Add(viewport);
            AutomationProperties.SetName(maps, "Saved game maps");AutomationProperties.SetName(pins, "Map markers");AutomationProperties.SetName(zoom, "Map zoom");
            ScrollViewer.SetHorizontalScrollBarVisibility(pins, ScrollBarVisibility.Disabled);ScrollViewer.SetHorizontalScrollBarVisibility(maps, ScrollBarVisibility.Disabled);
            maps.SelectionChanged += (_, __) => { if (!loading) SelectMap(maps.SelectedItem as GameMapDefinition); };
            pins.SelectionChanged += (_, __) => { if (pins.SelectedItem is ListBoxItem row && row.Tag is GameMapMarker marker) FocusMarker(marker); };
            search.TextChanged += (_, __) => RenderPins();remaining.Checked += (_, __) => RenderPins();remaining.Unchecked += (_, __) => RenderPins();
            zoom.ValueChanged += (_, __) => Draw();
            viewport.PreviewMouseWheel += (_, e) => { if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;zoom.Value *= e.Delta > 0 ? 1.2 : 1 / 1.2;e.Handled = true; };
            canvas.MouseLeftButtonDown += (_, e) =>
            {
                if (selected == null) return;
                if (placing)
                {
                    var p = e.GetPosition(canvas);var marker = new GameMapMarker { X = p.X / canvas.Width, Y = p.Y / canvas.Height, Label = "New pin" };
                    placing = false;addPin.Content = "Add pin";canvas.Cursor = Cursors.Arrow;EditMarker(marker);e.Handled = true;return;
                }
                dragStart = e.GetPosition(viewport);dragX = viewport.HorizontalOffset;dragY = viewport.VerticalOffset;canvas.CaptureMouse();e.Handled = true;
            };
            canvas.MouseMove += (_, e) => { if (dragStart is not Point start || e.LeftButton != MouseButtonState.Pressed) return;var p = e.GetPosition(viewport);viewport.ScrollToHorizontalOffset(dragX + start.X - p.X);viewport.ScrollToVerticalOffset(dragY + start.Y - p.Y); };
            canvas.MouseLeftButtonUp += (_, __) => { dragStart = null;canvas.ReleaseMouseCapture(); };
            canvas.LostMouseCapture += (_, __) => dragStart = null;
            Activated += (_, __) => { if (selected != null) LoadProgress(); };
            Loaded += (_, __) => Reload();
            Closed += (_, __) => { canvas.Children.Clear();image = null; };
        }
        internal void Reload(string preferred = null)
        {
            try
            {
                var id = preferred ?? selected?.Id;loading = true;
                maps.ItemsSource = tracking.Maps.List(game.Provider, game.Id, showAll ? null : achievement, showAll ? null : tracker);
                maps.SelectedItem = maps.Items.Cast<GameMapDefinition>().FirstOrDefault(m => m.Id == id) ?? maps.Items.Cast<GameMapDefinition>().FirstOrDefault();
                loading = false;SelectMap(maps.SelectedItem as GameMapDefinition);
            }
            catch (Exception ex) { loading = false;info.Text = ex.Message; }
        }
        void SelectMap(GameMapDefinition map)
        {
            selected = map;image = null;canvas.Children.Clear();pins.Items.Clear();
            properties.IsEnabled = addPin.IsEnabled = map != null;editPin.IsEnabled = false;
            if (map == null) { info.Text = "No maps saved here yet. Add a map image to start, or choose Show all game maps.";attribution.Text = "";return; }
            try
            {
                var bitmap = new BitmapImage();bitmap.BeginInit();bitmap.CacheOption = BitmapCacheOption.OnLoad;bitmap.UriSource = new Uri(tracking.Maps.ImagePath(map));bitmap.EndInit();bitmap.Freeze();image = bitmap;
                attribution.Text = (map.Source ?? "Local map") + (string.IsNullOrWhiteSpace(map.Calibration) ? "" : "\n" + map.Calibration);
                attribution.ToolTip = attribution.Text;
                LoadProgress();Dispatcher.BeginInvoke(new Action(Fit));
            }
            catch (Exception ex) { info.Text = "Could not open map: " + ex.Message; }
        }
        void LoadProgress()
        {
            progress.Clear();if (selected == null) return;
            if (string.IsNullOrWhiteSpace(game.Provider == "steam" ? steam : retro)) { RenderPins();return; }
            try
            {
                var allowed = tracking.Definitions().Where(d => d.Provider == game.Provider && d.GameId == game.Id).Select(d => d.Id).ToHashSet();
                foreach (var id in selected.Markers.Select(m => m.TrackerId).Where(id => !string.IsNullOrEmpty(id) && allowed.Contains(id)).Distinct())
                    foreach (var state in tracking.Load(id, game.Provider == "steam" ? steam : retro)) progress[(id, state.Item.Id)] = state;
                RenderPins();
            }
            catch (Exception ex) { info.Text = "Progress unavailable: " + ex.Message;RenderPins(); }
        }
        bool Complete(GameMapMarker marker) => progress.TryGetValue((marker.TrackerId, marker.ItemId), out var state) && state.Complete;
        IEnumerable<GameMapMarker> VisiblePins() => selected?.Markers.Where(m => (remaining.IsChecked != true || !Complete(m)) &&
            (string.IsNullOrWhiteSpace(search.Text) || (m.Label + " " + m.Detail).Contains(search.Text.Trim(), StringComparison.CurrentCultureIgnoreCase))) ?? Enumerable.Empty<GameMapMarker>();
        void RenderPins()
        {
            var old = (pins.SelectedItem as ListBoxItem)?.Tag as GameMapMarker;pins.Items.Clear();
            foreach (var marker in VisiblePins())
            {
                var text = Text((Complete(marker) ? "✓  " : "○  ") + marker.Label + "  " + (marker.Detail ?? "").Split('\n')[0], 13);
                text.TextWrapping = TextWrapping.NoWrap;text.TextTrimming = TextTrimming.CharacterEllipsis;
                var row = new ListBoxItem { Tag = marker, Padding = new Thickness(6), Content = text, ToolTip = marker.Detail };
                pins.Items.Add(row);if (old?.Id == marker.Id) pins.SelectedItem = row;
            }
            editPin.IsEnabled = pins.SelectedItem != null;
            info.Text = selected == null ? "" : selected.Region + " · " + selected.PixelWidth + " × " + selected.PixelHeight + " · " + pins.Items.Count + " / " + selected.Markers.Count + " pins shown · " + selected.Markers.Count(Complete) + " completed";
            Draw();
        }
        void Draw()
        {
            canvas.Children.Clear();if (selected == null || image == null) return;
            canvas.Width = selected.PixelWidth * zoom.Value;canvas.Height = selected.PixelHeight * zoom.Value;
            canvas.Children.Add(new Image { Source = image, Width = canvas.Width, Height = canvas.Height, Stretch = Stretch.Fill, IsHitTestVisible = false });
            foreach (var marker in VisiblePins())
            {
                var pin = Button(marker.Label, marker.Label + "\n" + marker.Detail);pin.Content = new TextBlock { Text = marker.Label, TextTrimming = TextTrimming.CharacterEllipsis };
                pin.FontSize = 11;pin.FontWeight = FontWeights.Bold;pin.MinWidth = 26;pin.MaxWidth = 110;pin.Height = pin.MinHeight = 26;pin.Margin = new Thickness(0);pin.Padding = new Thickness(3, 0, 3, 0);
                pin.Background = UiBrushHelper.FromHex(Complete(marker) ? "#236B54" : "#D6B657");pin.Foreground = Complete(marker) ? Brushes.White : UiBrushHelper.FromHex("#11181D");pin.BorderBrush = Brushes.White;pin.BorderThickness = new Thickness(1);pin.Cursor = Cursors.Hand;
                // Toolbar templates bake in their palette. Pins bind to their own completion colours.
                var frame = new FrameworkElementFactory(typeof(Border));
                frame.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));frame.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
                frame.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));frame.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
                var content = new FrameworkElementFactory(typeof(ContentPresenter));content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);content.SetValue(FrameworkElement.MarginProperty, new TemplateBindingExtension(Control.PaddingProperty));frame.AppendChild(content);
                pin.Template = new ControlTemplate(typeof(Button)) { VisualTree = frame };
                AutomationProperties.SetName(pin, marker.Label + (Complete(marker) ? ", completed" : ", remaining"));
                pin.Measure(new Size(110, 26));Canvas.SetLeft(pin, marker.X * canvas.Width - pin.DesiredSize.Width / 2);Canvas.SetTop(pin, marker.Y * canvas.Height - 13);
                pin.Click += (_, __) => { pins.SelectedItem = pins.Items.Cast<ListBoxItem>().FirstOrDefault(r => ((GameMapMarker)r.Tag).Id == marker.Id);FocusMarker(marker); };
                canvas.Children.Add(pin);
            }
        }
        void Fit() { if (selected != null && image != null && viewport.ViewportWidth > 0 && viewport.ViewportHeight > 0) { zoom.Value = Math.Max(zoom.Minimum, Math.Min(viewport.ViewportWidth / selected.PixelWidth, viewport.ViewportHeight / selected.PixelHeight));Draw(); } }
        void FocusMarker(GameMapMarker marker)
        {
            editPin.IsEnabled = true;info.Text = marker.Label + " · " + (Complete(marker) ? "Completed" : "Not Complete") + "\n" + marker.Detail;
            info.ToolTip = marker.Detail;
            viewport.ScrollToHorizontalOffset(marker.X * canvas.Width - viewport.ViewportWidth / 2);viewport.ScrollToVerticalOffset(marker.Y * canvas.Height - viewport.ViewportHeight / 2);
        }
        async Task ImportAsync()
        {
            var file = new OpenFileDialog { Title = "Save a game map", Filter = "Map images (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg", CheckFileExists = true };
            if (file.ShowDialog(this) != true) return;
            var map = new GameMapDefinition { Provider = game.Provider, GameId = game.Id, GameTitle = game.Title, Title = Path.GetFileNameWithoutExtension(file.FileName) };
            if (!string.IsNullOrEmpty(achievement)) map.AchievementIds.Add(achievement);if (!string.IsNullOrEmpty(tracker)) map.TrackerIds.Add(tracker);
            var editor = new GameMapDetailsWindow(this, tracking, map, steam, retro);
            if (editor.ShowDialog() != true) return;
            try { await Task.Run(() => tracking.Maps.Import(editor.Result, file.FileName));Reload(editor.Result.Id); }
            catch (Exception ex) { info.Text = "Could not save map: " + ex.Message; }
        }
        async Task EditAsync()
        {
            if (selected == null) return;var editor = new GameMapDetailsWindow(this, tracking, selected, steam, retro);
            if (editor.ShowDialog() != true) return;
            try { await Task.Run(() => tracking.Maps.Save(editor.Result));Reload(editor.Result.Id); }
            catch (Exception ex) { info.Text = ex.Message; }
        }
        void EditMarker(GameMapMarker added = null)
        {
            var marker = added ?? (pins.SelectedItem as ListBoxItem)?.Tag as GameMapMarker;if (selected == null || marker == null) return;
            var editor = new GameMapPinWindow(this, tracking, selected, marker);
            if (editor.ShowDialog() != true) return;
            try
            {
                var copy = System.Text.Json.JsonSerializer.Deserialize<GameMapDefinition>(System.Text.Json.JsonSerializer.Serialize(selected));
                copy.Markers.RemoveAll(m => m.Id == marker.Id);if (!editor.Remove) copy.Markers.Add(editor.Result);
                tracking.Maps.Save(copy);Reload(copy.Id);
            }
            catch (Exception ex) { info.Text = ex.Message; }
        }
        internal static TextBlock Text(string text, double size = 14, bool bold = false) => new() { Text = text ?? "", FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        internal static Button Button(string text, string tip)
        {
            var button = AchievementTrackerWindow.Button(text, tip);button.Padding = new Thickness(10, 6, 10, 6);button.Margin = new Thickness(0, 0, 8, 4);button.MinHeight = 32;button.MinWidth = 0;button.FontSize = 13;return button;
        }
    }
}
