using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PixelVaultNative
{
    // Shared compact presentation; the full tracker remains the place for notes,
    // count editing, source review and other management actions.
    sealed class AchievementInlineTrackers : StackPanel
    {
        sealed class Row
        {
            internal List<AchievementTrackerState> States;
            internal Func<FrameworkElement> Build;
            FrameworkElement view;
            public FrameworkElement View => view ??= Build();
        }
        sealed class Card
        {
            internal AchievementTrackerDefinition Definition;
            internal Expander Expander;
            internal ListBox List;
            internal TextBlock Header, Status;
            internal string Revision;
        }
        readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(5) };
        readonly Dictionary<string, Card> cards = new(StringComparer.Ordinal);
        AchievementTrackingStore store;
        string provider, game, achievement, account, scope;
        internal AchievementInlineTrackers()
        {
            TrackerFormStyle.ApplyScoped(this);
            Background = SystemParameters.HighContrast ? SystemColors.WindowBrush : UiBrushHelper.FromHex("#0D151A");
            Visibility = Visibility.Collapsed; Margin = new Thickness(0, 12, 0, 0);
            timer.Tick += (_, __) => Reload();
            Unloaded += (_, __) => timer.Stop();
            Loaded += (_, __) => { Reload(); UpdateTimer(); };
        }
        internal void Bind(AchievementTrackingStore source, string provider, string game, string achievement, string account)
        {
            string next = provider + "\n" + game + "\n" + achievement + "\n" + account;
            if (scope != next || store != source) { timer.Stop(); cards.Clear(); Children.Clear(); }
            scope = next; store = source; this.provider = provider; this.game = game; this.achievement = achievement;
            this.account = AchievementTrackingCatalog.AccountKey(provider, account);
            Reload();
        }
        static Brush Ink(string hex) => SystemParameters.HighContrast ? SystemColors.WindowTextBrush : UiBrushHelper.FromHex(hex);
        void UpdateTimer()
        {
            if (IsLoaded && cards.Values.Any(c => c.Expander.IsExpanded)) timer.Start(); else timer.Stop();
        }
        internal void Reload()
        {
            if (store == null || string.IsNullOrEmpty(achievement)) { Visibility = Visibility.Collapsed; return; }
            try
            {
                var definitions = store.Definitions().Where(d => d.Provider == provider && d.GameId == game && d.AchievementId == achievement).OrderBy(d => d.Title).ToList();
                foreach (var key in cards.Keys.Except(definitions.Select(d => d.Id)).ToList()) { Children.Remove(cards[key].Expander); cards.Remove(key); }
                foreach (var definition in definitions)
                {
                    if (!cards.TryGetValue(definition.Id, out var card))
                    {
                        card = CreateCard(definition); cards.Add(definition.Id, card); Children.Add(card.Expander);
                    }
                    card.Definition = definition;
                    string revision = store.InlineRevision(definition.Id, account) + "\n" + JsonSerializer.Serialize(definition, AchievementTrackingCatalog.Json);
                    if (card.Revision != revision) { UpdateCard(card); card.Revision = revision; }
                }
                Visibility = cards.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                UpdateTimer();
            }
            catch (Exception ex)
            {
                foreach (var card in cards.Values) { card.List.IsEnabled = false; card.Status.Text = "Tracker unavailable: " + ex.Message; }
            }
        }
        Card CreateCard(AchievementTrackerDefinition definition)
        {
            var card = new Card { Definition = definition };
            card.Header = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Ink("#D9E6ED"), TextWrapping = TextWrapping.Wrap };
            card.Status = new TextBlock { FontSize = 11.5, Foreground = Ink("#9CB1BC"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 7) };
            card.List = new ListBox { MaxHeight = 264, MinHeight = 28, BorderThickness = new Thickness(0), Background = Brushes.Transparent,
                Foreground = Ink("#D9E6ED"), HorizontalContentAlignment = HorizontalAlignment.Stretch, FontSize = 12.5, Padding = new Thickness(0) };
            ScrollViewer.SetHorizontalScrollBarVisibility(card.List, ScrollBarVisibility.Disabled);
            ScrollViewer.SetVerticalScrollBarVisibility(card.List, ScrollBarVisibility.Auto);
            ScrollViewer.SetCanContentScroll(card.List, true);
            VirtualizingPanel.SetIsVirtualizing(card.List, true); VirtualizingPanel.SetVirtualizationMode(card.List, VirtualizationMode.Recycling);
            var style = new Style(typeof(ListBoxItem)); style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(2, 1, 2, 1))); style.Setters.Add(new Setter(Control.MarginProperty, new Thickness(0)));
            card.List.ItemContainerStyle = style;
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter)); presenter.SetBinding(ContentPresenter.ContentProperty, new Binding(nameof(Row.View)));
            card.List.ItemTemplate = new DataTemplate { VisualTree = presenter };
            AutomationProperties.SetName(card.List, definition.Title + " checklist");
            var body = new StackPanel { Margin = new Thickness(10, 0, 10, 8) }; body.Children.Add(card.Status); body.Children.Add(card.List);
            card.Expander = new Expander { Header = card.Header, Content = body, IsExpanded = false, FontSize = 13,
                Foreground = Ink("#D9E6ED"), Padding = new Thickness(10), Margin = new Thickness(0, 0, 0, 8),
                Background = SystemParameters.HighContrast ? SystemColors.WindowBrush : UiBrushHelper.FromHex("#0D151A") };
            AutomationProperties.SetName(card.Expander, "Linked tracker: " + definition.Title);
            card.Expander.Expanded += (_, __) => { card.Revision = null; Reload(); UpdateTimer(); };
            card.Expander.Collapsed += (_, __) => UpdateTimer();
            return card;
        }
        void UpdateCard(Card card)
        {
            var states = store.Load(card.Definition.Id, account).Where(s => !s.Item.Archived).ToList();
            bool connected = !string.IsNullOrEmpty(account);
            var groups = states.GroupBy(s => string.IsNullOrEmpty(s.Item.GroupId) ? s.Item.Id : s.Item.GroupId).ToList();
            var totals = TrackerTotals.Calculate(card.Definition, states);
            long completed = totals.Completed, total = totals.Total;
            if (card.Definition.CategoryTargets.Count > 0)
            {
                var counts = AchievementTrackingCatalog.QualifyingRows(states);
                completed = card.Definition.CategoryTargets.Sum(t => (long)Math.Min(t.Value, counts.GetValueOrDefault(t.Key)));
                total = card.Definition.CategoryTargets.Values.Sum(t => (long)t);
            }
            card.Header.Text = card.Definition.Title + (connected ? " · " + completed + " / " + total : " · Connect account");
            card.Status.Text = connected ? "Game-confirmed checks stay checked. Hover an item for details." : "Connect an account to edit this tracker.";
            card.List.IsEnabled = true;
            if (!card.Expander.IsExpanded) return;
            var native = store.InlineGameCompletions(card.Definition.Id, account, states);
            var selected = (card.List.SelectedItem as Row)?.States[0].Item.Id;
            var focused = card.List.IsKeyboardFocusWithin ? (Keyboard.FocusedElement as CheckBox)?.Tag as string : null;
            double offset = FindScroll(card.List)?.VerticalOffset ?? 0;
            var rows = groups.Select(g =>
            {
                var row = new Row { States = g.ToList() };
                row.Build = () => BuildRow(card, row.States, native, connected);
                return row;
            }).ToList();
            card.List.ItemsSource = rows;
            card.List.SelectedItem = rows.FirstOrDefault(r => r.States[0].Item.Id == selected);
            card.List.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                FindScroll(card.List)?.ScrollToVerticalOffset(offset);
                if (focused != null && card.List.IsVisible)
                {
                    var box = FindBox(card.List, focused);
                    if (box?.IsEnabled == true) box.Focus(); else card.List.Focus();
                }
            }));
        }
        FrameworkElement BuildRow(Card card, List<AchievementTrackerState> states, HashSet<string> native, bool connected)
        {
            var first = states[0];
            var grid = new Grid { MinHeight = 25, Margin = new Thickness(0, 1, 0, 1) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 100 });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var title = new TextBlock { Text = first.Item.Name, FontSize = 12.5, Foreground = Ink("#D9E6ED"), VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0), TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = first.Item.Name + "\n" + first.Item.Detail };
            grid.Children.Add(title);
            var boxes = new WrapPanel { Orientation = Orientation.Horizontal, MaxWidth = 240, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(boxes, 1); grid.Children.Add(boxes);
            if (!string.IsNullOrWhiteSpace(first.Item.Content))
            {
                var content = new Button { Content = "Content", FontSize = 12, Padding = new Thickness(5, 1, 5, 1), Margin = new Thickness(0, 0, 5, 0), ToolTip = "Read saved reference text" };
                content.Click += (_, __) => new TrackerContentWindow(Window.GetWindow(this), first.Item).Show();
                boxes.Children.Add(content);
            }
            foreach (var state in states)
            {
                bool confirmed = native.Contains(state.Item.Id);
                var box = new CheckBox { Content = string.IsNullOrEmpty(state.Item.Milestone) ? null : state.Item.Milestone,
                    FontSize = 12, IsChecked = state.Complete, IsEnabled = connected && !(state.Complete && confirmed),
                    Foreground = Ink("#D9E6ED"), Margin = new Thickness(4, 3, 4, 3), VerticalContentAlignment = VerticalAlignment.Center, Tag = state.Item.Id };
                box.ToolTip = state.Item.Name + (string.IsNullOrEmpty(state.Item.Milestone) ? "" : " · " + state.Item.Milestone)
                    + (confirmed ? "\nConfirmed by game data; cannot be unchecked here." : "\n" + (state.IsKnown ? state.Current + " / " + state.Item.Target : "Not checked")) + "\n" + state.Item.Detail;
                ToolTipService.SetShowOnDisabled(box, true);
                AutomationProperties.SetName(box, state.Item.Name + (string.IsNullOrEmpty(state.Item.Milestone) ? "" : " · " + state.Item.Milestone));
                if (confirmed) AutomationProperties.SetHelpText(box, "Confirmed by game data; cannot be unchecked here.");
                AccessibilityUi.TryApplyFocusVisualStyle(box);
                box.Click += (_, __) =>
                {
                    card.List.SelectedItem = card.List.Items.OfType<Row>().FirstOrDefault(r => r.States.Any(s => s.Item.Id == state.Item.Id));
                    string error = null;
                    try { store.SetInlineCompletion(card.Definition.Id, account, state.Item.Id, box.IsChecked == true); }
                    catch (Exception ex) { error = ex.Message; }
                    finally { card.Revision = null; Reload(); if (error != null) card.Status.Text = error; }
                };
                boxes.Children.Add(box);
            }
            return grid;
        }
        static ScrollViewer FindScroll(DependencyObject node)
        {
            if (node is ScrollViewer scroll) return scroll;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            { var found = FindScroll(VisualTreeHelper.GetChild(node, i)); if (found != null) return found; }
            return null;
        }
        static CheckBox FindBox(DependencyObject node, string id)
        {
            if (node is CheckBox box && Equals(box.Tag, id)) return box;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            { var found = FindBox(VisualTreeHelper.GetChild(node, i), id); if (found != null) return found; }
            return null;
        }
    }
}
