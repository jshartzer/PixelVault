using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace PixelVaultNative
{
    /// <summary>Shared checklist surface for achievement-linked and game-wide goals.</summary>
    sealed partial class AchievementTrackerWindow : Window
    {
        readonly AchievementTrackingStore store;
        readonly string steamAccount, retroAccount, provider, gameId, achievementId;
        readonly List<AchievementHubGame> games;
        List<GameAchievementsFetchService.AchievementRow> achievements;
        readonly ComboBox trackers = new() { MinWidth = 260, Margin = new Thickness(0, 0, 0, 12) };
        readonly ComboBox filter = new() { Width = 215, Margin = new Thickness(10, 0, 0, 0) };
        readonly ComboBox category = new() { Width = 185, Margin = new Thickness(10, 0, 0, 0) };
        readonly CheckBox groupByCategory = new() { Content = "Group by category", IsChecked = true, Margin = new Thickness(0, 6, 0, 0), VerticalContentAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        readonly TextBox search = new() { MinWidth = 130, Padding = new Thickness(9, 6, 9, 6) };
        readonly TextBlock summary = Text("Loading checklists…", 21, true);
        readonly TextBlock source = Text("", 15);
        readonly TextBlock description = Text("", 16);
        readonly TextBlock linkedAchievement = Text("", 16);
        readonly TextBlock connectionIndicator = Text("",14);
        readonly Button editLink;
        readonly TextBlock status = Text("", 15);
        readonly TextBlock detailTitle = Text("Select an item", 21, true);
        readonly TextBlock itemSource = Text("", 16);
        readonly TextBox notes = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 70, MaxHeight = 130, MaxLength = 20000, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        readonly TextBox current = new() { Width = 80, Padding = new Thickness(6), HorizontalAlignment = HorizontalAlignment.Left };
        readonly StackPanel countEditor = new();
        readonly ProgressBar progress = new() { Height = 7, Minimum = 0, Maximum = 100, Margin = new Thickness(0, 8, 0, 10) };
        readonly ListBox list = new() { Background = Color("#101A21"), BorderThickness = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch };
        readonly Button import, saveNote, saveCount, reset;
        List<AchievementTrackerState> states = new();
        AchievementTrackerDefinition selected;
        AchievementTrackerState selectedItem;
        int loadGeneration;
        bool closed, loadingDefinitions;
        string preferredId;
        readonly bool browseGameTrackers;
        internal Func<AchievementTrackerEditWindow, bool?> ShowEditor = dialog => dialog.ShowDialog();
        internal Task PendingEdit { get; private set; } = Task.CompletedTask;

        internal AchievementTrackerWindow(Window owner, AchievementTrackingStore store, string steamAccount, string retroAccount,
            IEnumerable<AchievementHubGame> games = null, string provider = null, string gameId = null, string achievementId = null,
            IEnumerable<GameAchievementsFetchService.AchievementRow> achievements = null,string trackerId=null,bool browseGameTrackers=false)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.steamAccount = steamAccount; this.retroAccount = retroAccount; this.provider = provider;
            this.gameId = gameId; this.achievementId = achievementId;
            preferredId=trackerId;this.browseGameTrackers=browseGameTrackers;
            this.achievements = (achievements ?? Enumerable.Empty<GameAchievementsFetchService.AchievementRow>()).Where(r => r != null).ToList();
            this.games = (games ?? Enumerable.Empty<AchievementHubGame>()).Where(g => g != null).GroupBy(g => g.Key).Select(g => g.First()).ToList();
            owner=TrackerNavigation.BrowseOwner(owner);if (owner?.IsVisible == true) Owner = owner;
            Title = "Achievement trackers"; Width = 1220; Height = Math.Min(900, SystemParameters.WorkArea.Height - 40); MinWidth = 780; MinHeight = 640; FontSize = 16;
            WindowStartupLocation = Owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
            Background = SystemParameters.HighContrast ? SystemColors.WindowBrush : Color("#11181D"); Foreground = Brushes.White;
            var root = new DockPanel { Margin = new Thickness(24), LastChildFill = true, Background = Background }; Content = root;
            progress.Foreground = UiBrushHelper.FromHex("#72BFC8"); progress.Background = UiBrushHelper.FromHex("#263C48");
            foreach (var editor in new[] { search, notes, current }) DressEditor(editor);
            var header = new StackPanel(); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
            header.Children.Add(Text("Achievement trackers", 26, true));
            var actions = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            definitionActions = actions;
            var create = Button("New checklist", "Create a checklist for a game or achievement");
            create.Click += async (_, __) => await CreateAsync();
            import = Button("Import progress", "Import a game- and account-matched progress snapshot");
            import.Click += async (_, __) => await ImportWithReviewAsync();
            var spreadsheet = Button("Import tracker…", "Import a spreadsheet or portable tracker definition");
            var importMenu=new ContextMenu();spreadsheet.ContextMenu=importMenu;
            var sheetEntry=new MenuItem { Header="Spreadsheet…" };sheetEntry.Click+=async(_,__)=>await CreateAsync(true);importMenu.Items.Add(sheetEntry);
            var definitionEntry=new MenuItem { Header="Definition JSON…" };definitionEntry.Click+=async(_,__)=>await ImportDefinitionAsync();importMenu.Items.Add(definitionEntry);
            var catalogueEntry=new MenuItem { Header="Update current catalogue…" };catalogueEntry.Click+=async(_,__)=>await ManageCatalogueAsync();importMenu.Items.Add(catalogueEntry);
            spreadsheet.Click+=(_,__)=> { importMenu.PlacementTarget=spreadsheet;importMenu.IsOpen=true; };
            editLink = Button("Achievement link…", "Link this tracker to an achievement or make it game-wide");
            editLink.Click += async (_, __) => await EditLinkAsync();
            actions.Children.Add(create); actions.Children.Add(spreadsheet); actions.Children.Add(import); actions.Children.Add(editLink);
            var export = Button("Export…", "Export this tracker's definition or account progress separately");
            export.MinWidth=78;
            var exportMenu = new ContextMenu(); export.ContextMenu = exportMenu;
            foreach (var mode in new[] { "Definition JSON", "Blank Excel template", "Account progress Excel" })
            {
                var entry = new MenuItem { Header = mode }; entry.Click += async (_, __) => await ExportAsync(mode); exportMenu.Items.Add(entry);
            }
            export.Click += (_, __) => { exportMenu.PlacementTarget=export; exportMenu.IsOpen=true; }; actions.Children.Add(export);
            foreach (var action in actions.Children.OfType<Button>()) { action.MinHeight = 38; action.Padding = new Thickness(10, 6, 10, 6); action.Margin = new Thickness(0, 0, 8, 4); }
            var manage=Button("Manage tracker…","Manage achievement link, sources, progress imports and exports");var manageMenu=new ContextMenu();manage.ContextMenu=manageMenu;
            void Manage(Button button)
            {
                var entry=new MenuItem { Header=button.Content,ToolTip=button.ToolTip };
                entry.SetBinding(IsEnabledProperty,new Binding("IsEnabled") { Source=button });
                entry.SetBinding(VisibilityProperty,new Binding("Visibility") { Source=button });
                if(button.ContextMenu!=null)
                { var entries=button.ContextMenu.Items.Cast<object>().ToList();button.ContextMenu.Items.Clear();foreach(var child in entries)entry.Items.Add(child); }
                else entry.Click+=(_,__)=>button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                manageMenu.Items.Add(entry);
            }
            Manage(import);Manage(editLink);Manage(export);actions.Children.Remove(import);actions.Children.Remove(editLink);actions.Children.Remove(export);actions.Children.Add(manage);
            manage.Click+=(_,__)=>{manageMenu.PlacementTarget=manage;manageMenu.IsOpen=true;};
            header.Children.Add(actions);
            description.Visibility=Visibility.Collapsed;
            trackers.Margin = new Thickness(0, 0, 0, 6);
            AutomationProperties.SetName(trackers, "Choose achievement tracker"); header.Children.Add(trackers);
            linkedAchievement.Margin = new Thickness(0, 0, 0, 4); header.Children.Add(linkedAchievement);
            connectionIndicator.Margin=new Thickness(0,0,0,6);header.Children.Add(connectionIndicator);
            header.Children.Add(summary); header.Children.Add(progress); header.Children.Add(source);source.MaxHeight=22;source.TextTrimming=TextTrimming.CharacterEllipsis;source.TextWrapping=TextWrapping.NoWrap;
            var controls = new Grid { Margin = new Thickness(0, 10, 0, 10) };
            controls.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });controls.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            controls.ColumnDefinitions.Add(new ColumnDefinition()); controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            search.ToolTip = "Search item names and notes"; AutomationProperties.SetName(search, "Search tracked items");
            var searchHost = new Grid(); searchHost.Children.Add(search);
            var placeholder = Text("Search items…", 15);placeholder.TextWrapping=TextWrapping.NoWrap;placeholder.TextTrimming=TextTrimming.CharacterEllipsis; placeholder.Margin = new Thickness(10, 0, 8, 0); placeholder.VerticalAlignment = VerticalAlignment.Center;
            placeholder.IsHitTestVisible = false; placeholder.Foreground = UiBrushHelper.FromHex("#9CB1BC"); searchHost.Children.Add(placeholder);
            search.TextChanged += (_, __) => placeholder.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            controls.Children.Add(searchHost); Grid.SetColumn(filter, 1); controls.Children.Add(filter); Grid.SetColumn(category, 2); controls.Children.Add(category);
            foreach (var label in new[] { "All items", "Ready and remaining", "All remaining", "Completed", "Locked", "Not checked" }) filter.Items.Add(label);
            filter.SelectedIndex = 1; category.Items.Add("All categories"); category.SelectedIndex = 0;
            AutomationProperties.SetName(filter, "Filter checklist status"); AutomationProperties.SetName(category, "Filter checklist category");
            header.Children.Add(controls);
            refreshHint.Margin=new Thickness(0,0,0,8);header.Children.Add(refreshHint);
            var footer = new StackPanel { Margin = new Thickness(0, 10, 0, 0) }; DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
            status.Margin = new Thickness(0, 4, 0, 8);status.MaxHeight=22;status.TextWrapping=TextWrapping.NoWrap;status.TextTrimming=TextTrimming.CharacterEllipsis;footer.Children.Add(status);
            var close = Button("Close", "Close trackers"); close.Click += (_, __) => Close(); footer.Children.Add(BuildRefreshActions(close));
            var footerActions=(WrapPanel)footer.Children[footer.Children.Count-1];
            Manage(configureSource);Manage(automationButton);Manage(pauseAutomatic);
            footerActions.Children.Remove(configureSource);footerActions.Children.Remove(automationButton);footerActions.Children.Remove(pauseAutomatic);
            footerActions.Children.Remove(refreshSource);controls.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });Grid.SetColumn(refreshSource,3);refreshSource.Margin=new Thickness(10,0,0,0);controls.Children.Add(refreshSource);
            AutomationProperties.SetName(groupByCategory,"Group tracker items by category");groupByCategory.ToolTip="Show category headings, such as delivery types, while keeping each record's completion boxes together.";
            Grid.SetRow(groupByCategory,1);Grid.SetColumnSpan(groupByCategory,4);controls.Children.Add(groupByCategory);
            groupByCategory.Checked+=(_,__)=>Regroup();groupByCategory.Unchecked+=(_,__)=>Regroup();
            var content = new Grid(); content.ColumnDefinitions.Add(new ColumnDefinition());content.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(6) });content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) }); root.Children.Add(content);
            AutomationProperties.SetName(list, "Tracked items"); VirtualizingPanel.SetIsVirtualizing(list, true); VirtualizingPanel.SetVirtualizationMode(list, VirtualizationMode.Recycling);
            VirtualizingPanel.SetIsVirtualizingWhenGrouping(list,true);
            ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled); content.Children.Add(list);
            var detail = new StackPanel { Margin = new Thickness(18, 0, 0, 0) };
            var detailScroll = new ScrollViewer { Content = detail, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            Grid.SetColumn(detailScroll, 2); content.Children.Add(detailScroll);
            var splitter=new GridSplitter { Width=6,HorizontalAlignment=HorizontalAlignment.Stretch,Background=Color("#263C48"),ResizeDirection=GridResizeDirection.Columns,ResizeBehavior=GridResizeBehavior.PreviousAndNext };Grid.SetColumn(splitter,1);content.Children.Add(splitter);
            var detailsButton=Button("Hide details","Show or hide selected item details and notes");footerActions.Children.Insert(footerActions.Children.Count-1,detailsButton);
            bool? detailChoice=null;
            void DetailLayout(){bool show=detailChoice??content.ActualWidth>=920;detailScroll.Visibility=splitter.Visibility=show?Visibility.Visible:Visibility.Collapsed;content.ColumnDefinitions[1].Width=new GridLength(show?6:0);if(!show)content.ColumnDefinitions[2].Width=new GridLength(0);else if(content.ColumnDefinitions[2].Width.Value<100)content.ColumnDefinitions[2].Width=new GridLength(280);detailsButton.Content=show?"Hide details":"Show details";}
            detailsButton.Click+=(_,__)=>{detailChoice=detailScroll.Visibility!=Visibility.Visible;DetailLayout();};content.SizeChanged+=(_,__)=>DetailLayout();
            detail.Children.Add(detailTitle); itemSource.Margin = new Thickness(0, 8, 0, 14); detail.Children.Add(itemSource);
            detail.Children.Add(countEditor); countEditor.Children.Add(Text("Progress", 16, true)); current.Margin = new Thickness(0, 6, 0, 6); countEditor.Children.Add(current);
            AutomationProperties.SetName(current, "Item progress count");
            saveCount = Button("Save progress", "Save a manual count for the selected item"); countEditor.Children.Add(saveCount);
            saveCount.Click += async (_, __) =>
            {
                if (selectedItem == null) return;
                if (!int.TryParse(current.Text, NumberStyles.None, CultureInfo.CurrentCulture, out var count) || count < 0 || count > 1000000)
                { Error("Enter a count from 0 to 1,000,000."); return; }
                await (PendingEdit = SetProgressAsync(selectedItem, count));
            };
            detail.Children.Add(Text("Notes", 16, true)); notes.Margin = new Thickness(0, 6, 0, 6); detail.Children.Add(notes); AutomationProperties.SetName(notes, "Item notes");
            saveNote = Button("Save note", "Save notes for the selected checklist item"); detail.Children.Add(saveNote);
            saveNote.Click += async (_, __) => await (PendingEdit = SaveNoteAsync());
            reset = Button("Use imported progress", "Remove the manual count and return to the latest imported observation"); detail.Children.Add(reset);
            reset.Click += async (_, __) => { if (selectedItem != null) await (PendingEdit = SetProgressAsync(selectedItem, null)); };
            detail.Children.Add(Text("Checklists help you plan. Provider achievement status is tracked separately.", 15));
            trackers.SelectionChanged += async (_, __) => { if (!loadingDefinitions) { selected = trackers.SelectedItem as AchievementTrackerDefinition; await ReloadAsync(true); } };
            filter.SelectionChanged += (_, __) => RenderList(); category.SelectionChanged += (_, __) => RenderList(); search.TextChanged += (_, __) => RenderList();
            list.SelectionChanged += (_, __) => ShowItem((list.SelectedItem as ListBoxItem)?.Tag as AchievementTrackerState);
            Loaded += async (_, __) => await LoadDefinitionsAsync(); Closed += (_, __) => { closed = true; loadGeneration++; refreshCancellation?.Cancel(); };
            PreviewKeyDown += (_, e) => { if(e.Key==Key.F && Keyboard.Modifiers==ModifierKeys.Control){search.Focus();search.SelectAll();e.Handled=true;}else if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
            ShowItem(null);TrackerFormStyle.Apply(this); AchievementCollectionUi.ApplyHighContrast(this);
        }

        string Account(AchievementTrackerDefinition definition) => definition?.Provider == "steam" ? steamAccount : retroAccount;

        internal async Task LoadDefinitionsAsync()
        {
            try
            {
                var all = await Task.Run(() => store.Definitions()); if (closed) return;
                foreach(var definition in all)
                    if(!games.Any(g=>g.Provider==definition.Provider && g.Id==definition.GameId))
                        games.Add(new AchievementHubGame { Provider=definition.Provider,Id=definition.GameId,Title=definition.GameTitle });
                var savedAchievements = await Task.Run(() => store.AchievementChoices(steamAccount, retroAccount)); if (closed) return;
                achievements = achievements.Concat(savedAchievements).GroupBy(r => (r.Provider, r.ProviderGameId, r.ProviderAchievementId)).Select(g => g.First()).ToList();
                var choices = all.Where(d => (string.IsNullOrEmpty(provider) || d.Provider == provider)
                    && (string.IsNullOrEmpty(gameId) || d.GameId == gameId)
                    && (browseGameTrackers || string.IsNullOrEmpty(achievementId) || d.AchievementId == achievementId || d.Id == preferredId)).ToList();
                loadingDefinitions = true; trackers.ItemsSource = choices;
                selected = choices.FirstOrDefault(d => d.Id == preferredId) ?? choices.FirstOrDefault(); trackers.SelectedItem = selected; loadingDefinitions = false;
                await ReloadAsync(true);
            }
            catch (Exception ex) { loadingDefinitions = false; Error("Could not load checklists. " + ex.Message); }
        }

        internal async Task ReloadAsync(bool changeTracker = false, bool preserveDraft = false)
        {
            var generation = ++loadGeneration; var definition = selected;
            UpdateRefreshControls();
            if (definition == null)
            {
                states.Clear(); summary.Text = "No checklist yet"; description.Text = "Create a checklist to break this achievement into smaller steps.";
                source.Text = ""; linkedAchievement.Text = ""; progress.Value = 0; editLink.IsEnabled = import.IsEnabled = false; activeSource = null; activeSourceStatus = null; UpdateRefreshControls(); RenderList(); ShowItem(null); return;
            }
            try
            {
                var account = Account(definition);
                var loaded = await Task.Run(() =>
                {
                    var warnings = new List<string>(); store.ImportPending(definition.Id, account, warnings.Add);
                    return (States: store.Load(definition.Id, account).ToList(), Warnings: warnings, Binding: store.Source(definition.Id, account), SourceStatus: store.SourceStatus(definition.Id, account), Automatic: string.IsNullOrWhiteSpace(account) ? (new TrackerAutomationSettings(), new TrackerAutomationState()) : store.Automation(definition.Id, account));
                });
                if (closed || generation != loadGeneration) return;
                if (preserveDraft && DraftPending()) return;
                states = loaded.States.Where(s=>!s.Item.Archived).ToList(); description.Text = definition.Description ?? definition.Title; description.ToolTip = description.Text;
                description.MaxHeight = definition.Milestones.Count > 0 ? 66 : 44;
                activeSource = loaded.Binding; activeSourceStatus = loaded.SourceStatus;
                (activeAutomation, activeAutomaticState) = loaded.Automatic;
                if (changeTracker && !string.IsNullOrWhiteSpace(account)) store.AutomationMonitor?.Opened(definition.Id, AchievementTrackingCatalog.AccountKey(definition.Provider, account));
                editLink.IsEnabled = true;
                var achievement = achievements.FirstOrDefault(r => r.Provider == definition.Provider && r.ProviderGameId == definition.GameId && r.ProviderAchievementId == definition.AchievementId);
                linkedAchievement.Text = string.IsNullOrEmpty(definition.AchievementId) ? "Game-wide tracker · No achievement link"
                    : "Linked achievement: " + (achievement?.Title ?? definition.AchievementId);
                var done = states.Count(s => s.Complete); var remaining = states.Count(s => !s.Complete && s.IsKnown && s.Availability == TrackerAvailability.Available);
                var locked = states.Count(s => !s.Complete && s.Availability == TrackerAvailability.Locked); var unknown = states.Count(s => !s.IsKnown);
                summary.Text = done + " / " + states.Count + " " + definition.CompletionLabel.ToLowerInvariant();
                progress.Value = states.Count == 0 ? 0 : 100.0 * done / states.Count;
                if (definition.Milestones.Count > 0 && definition.CategoryTargets.Count > 0)
                {
                    var counts = AchievementTrackingCatalog.QualifyingRows(states);
                    var qualifying = definition.CategoryTargets.Sum(t => Math.Min(t.Value, counts.GetValueOrDefault(t.Key)));
                    var target = definition.CategoryTargets.Values.Sum();
                    summary.Text = qualifying + " / " + target + " " + definition.CompletionLabel.ToLowerInvariant();
                    progress.Value = 100.0 * qualifying / target;
                }
                AutomationProperties.SetName(progress, summary.Text);
                var newest = states.OrderByDescending(s => s.ObservedUtcTicks).FirstOrDefault(s => s.ObservedUtcTicks > 0);
                source.Text = remaining + " ready · " + locked + " locked · " + unknown + " not checked"
                    + (newest == null ? " · No imported progress" : "\n" + newest.Source + " · " + new DateTime(newest.ObservedUtcTicks, DateTimeKind.Utc).ToLocalTime().ToString("g", CultureInfo.CurrentCulture));
                source.ToolTip = newest?.Evidence;
                if (definition.Milestones.Count > 0)
                {
                    var counts = AchievementTrackingCatalog.QualifyingRows(states);
                    source.Text = string.Join(" · ", definition.CategoryTargets.Select(t => t.Key + " " + counts.GetValueOrDefault(t.Key) + "/" + t.Value))
                        + "\n" + string.Join(" · ", definition.Milestones.Select(m => m + ": " + states.Count(s => s.Item.Milestone == m && s.Complete)
                            + " recorded, " + states.Count(s => s.Item.Milestone == m && !s.IsKnown) + " not checked"))
                        + (newest == null ? " · No imported progress" : "\n" + newest.Source + " · " + new DateTime(newest.ObservedUtcTicks, DateTimeKind.Utc).ToLocalTime().ToString("g", CultureInfo.CurrentCulture));
                }
                source.ToolTip=source.Text+"\n"+newest?.Evidence;source.Text=source.Text.Replace("\n"," · ");
                import.IsEnabled = !string.IsNullOrWhiteSpace(account);
                if (changeTracker)
                {
                    category.Items.Clear(); category.Items.Add("All categories");
                    foreach (var name in states.Select(s => CategoryName(s.Item.Category)).Distinct().OrderBy(x => x)) category.Items.Add(name);
                    category.SelectedIndex = 0; filter.SelectedIndex = definition.Milestones.Count > 0 ? 0 : remaining > 0 ? 1 : 2;
                }
                RenderList();
                status.Foreground = Color("#D9E6ED");
                status.Text = string.IsNullOrWhiteSpace(account) ? "Connect this game's account to save or import progress. Unchecked items are unknown." : "Manual check-offs and notes are saved for this account. Imported progress is a dated snapshot.";
                if (activeSource != null) status.Text = "Source: " + Path.GetFileName(activeSource.Location) + " · " + (activeSourceStatus?.Outcome.ToString() ?? "Configured") + " · Last success: " + TrackerSourceWindow.Time(activeSourceStatus?.LastSuccessUtc ?? 0);
                if (activeAutomation.Mode != TrackerAutomationMode.Manual) status.Text += "\nAutomatic: " + (activeAutomation.Paused ? "Paused · " : "") + activeAutomaticState.Message;
                status.ToolTip = TrackerSourceWindow.Describe(activeSource, activeSourceStatus); UpdateRefreshControls();
                if (loaded.Warnings.Count > 0) Error("A saved progress import could not be read. Your saved checklist is still available. Use Import progress to retry with a valid snapshot.");
            }
            catch (Exception ex) { if (!closed && generation == loadGeneration) {Error("Could not load progress. " + ex.Message);UpdateRefreshControls();} }
        }

        void Regroup()
        {
            var id=selectedItem?.Item.Id;var note=notes.Text;var count=current.Text;bool draft=DraftPending();
            RenderList();
            if(draft&&selectedItem?.Item.Id==id){notes.Text=note;current.Text=count;}
        }

        internal void RenderList()
        {
            bool hasCategories=states.Any(s=>!string.IsNullOrWhiteSpace(s.Item.Category));
            groupByCategory.Visibility=hasCategories?Visibility.Visible:Visibility.Collapsed;
            var selectedId = selectedItem?.Item.Id; list.ItemsSource=null; list.Items.Clear(); var cells=new List<ListBoxItem>(); var query = search.Text.Trim();
            double indicatorWidth=selected?.Milestones.Sum(m=>MilestoneWidth(m)+4)??0;
            bool wide=selected?.Milestones.Count>2 || indicatorWidth>300;
            ScrollViewer.SetHorizontalScrollBarVisibility(list,wide?ScrollBarVisibility.Auto:ScrollBarVisibility.Disabled);
            var statusFilter = filter.SelectedIndex; var categoryFilter = category.SelectedItem as string;
            var visible = states.Where(s => (category.SelectedIndex <= 0 || CategoryName(s.Item.Category) == categoryFilter)
                && (query.Length == 0 || (s.Item.Name + " " + s.Note).Contains(query, StringComparison.OrdinalIgnoreCase))
                && (statusFilter == 0 || statusFilter == 1 && !s.Complete && s.IsKnown && s.Availability == TrackerAvailability.Available
                    || statusFilter == 2 && !s.Complete || statusFilter == 3 && s.Complete || statusFilter == 4 && !s.Complete && s.Availability == TrackerAvailability.Locked
                    || statusFilter == 5 && !s.IsKnown)).ToList();
            if (selected?.Milestones.Count > 0)
            {
                foreach (var group in states.GroupBy(s => s.Item.GroupId).Where(g => g.Any(visible.Contains)))
                {
                    var members = group.ToList(); var state = members.FirstOrDefault(s => s.Item.Id == selectedId) ?? members[0];
                    var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                    var title=RecordTitle(state.Item.Name);text.Children.Add(title);
                    var caption = Text(state.Item.Category + (members.Any(s => !string.IsNullOrWhiteSpace(s.Note)) ? " · Has note" : "")
                        + (members.Any(s => s.HasConflict) ? " · Manual conflict" : ""), 15);
                    caption.Margin = new Thickness(0, 3, 0, 0); caption.TextWrapping = TextWrapping.NoWrap; caption.TextTrimming = TextTrimming.CharacterEllipsis;
                    title.ToolTip=state.Item.Name+"\n"+caption.Text;
                    if(members.Any(s=>!string.IsNullOrWhiteSpace(s.Note)))title.Inlines.Add(new Run(" · Note") { FontSize=14 });
                    var ranks = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                    foreach (var milestone in selected.Milestones)
                    {
                        var rank = members.Single(s => s.Item.Milestone == milestone);
                        ranks.Children.Add(ProgressToggle(rank, milestone));
                    }
                    var row = RecordRow(text, ranks);
                    if(wide)row.SetBinding(WidthProperty,new Binding("ActualWidth") { Source=list,Converter=new RecordWidthConverter(),ConverterParameter=150+indicatorWidth });
                    var item = RecordCell(row, state);
                    cells.Add(item);
                }
            }
            else foreach (var state in visible)
            {
                var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; var title=RecordTitle(state.Item.Name);text.Children.Add(title);
                var caption = Text((state.Item.Category ?? "Other") + (state.Item.Target > 1 ? " · " + (state.Current?.ToString() ?? "?") + "/" + state.Item.Target : "")
                    + (state.IsManual ? " · Manual" : "") + (state.HasConflict ? " · Manual conflict" : "") + (string.IsNullOrWhiteSpace(state.Note) ? "" : " · Has note"), 15);
                caption.Margin = new Thickness(0, 3, 0, 0); caption.TextWrapping = TextWrapping.NoWrap; caption.TextTrimming = TextTrimming.CharacterEllipsis;
                title.ToolTip=state.Item.Name+"\n"+caption.Text;
                if(state.Item.Target>1)title.Inlines.Add(new Run(" · "+(state.Current?.ToString()??"?")+"/"+state.Item.Target) { FontSize=14 });
                if(!string.IsNullOrWhiteSpace(state.Note))title.Inlines.Add(new Run(" · Note") { FontSize=14 });
                var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center }; actions.Children.Add(ProgressToggle(state, "Complete"));
                var row = RecordRow(text, actions);
                var item = RecordCell(row, state);
                cells.Add(item);
            }
            list.GroupStyle.Clear();var view=new ListCollectionView(cells);
            if(cells.Count>0 && hasCategories && groupByCategory.IsChecked==true)
            {
                view.GroupDescriptions.Add(new PropertyGroupDescription("Tag.Item.Category",new CategoryGroupConverter()));
                var header=new FrameworkElementFactory(typeof(StackPanel));header.SetValue(StackPanel.OrientationProperty,Orientation.Horizontal);
                header.SetValue(MarginProperty,new Thickness(2,5,0,3));
                var label=new FrameworkElementFactory(typeof(TextBlock));label.SetBinding(TextBlock.TextProperty,new Binding("Name"));label.SetValue(TextBlock.FontSizeProperty,16.0);label.SetValue(TextBlock.FontWeightProperty,FontWeights.SemiBold);label.SetValue(TextBlock.ForegroundProperty,Foreground);header.AppendChild(label);
                var count=new FrameworkElementFactory(typeof(TextBlock));count.SetBinding(TextBlock.TextProperty,new Binding("Name") { Converter=new CategorySubtotalConverter(CategorySubtotals()) });count.SetValue(TextBlock.FontSizeProperty,14.0);count.SetValue(TextBlock.ForegroundProperty,Foreground);count.SetValue(MarginProperty,new Thickness(10,0,0,0));
                count.SetValue(ToolTipProperty,"Completed / total active records in this category. Filters do not change these totals. A category goal is shown separately when configured.");header.AppendChild(count);
                list.GroupStyle.Add(new GroupStyle { HeaderTemplate=new DataTemplate { VisualTree=header } });
            }
            if(cells.Count==0){cells.Add(new ListBoxItem { Content=Text("No items match these filters.",18),IsEnabled=false,Padding=new Thickness(16) });view.Refresh();}
            list.ItemsSource=view;
            list.SelectedItem=cells.FirstOrDefault(i=>i.Tag is AchievementTrackerState state && (state.Item.Id==selectedId || selected?.Milestones.Count>0 && states.Any(s=>s.Item.Id==selectedId && s.Item.GroupId==state.Item.GroupId)));
            if (list.SelectedItem == null)
            {
                var first = list.Items.OfType<ListBoxItem>().FirstOrDefault(i => i.Tag is AchievementTrackerState);
                if (first != null) list.SelectedItem = first; else ShowItem(null);
            }
            AchievementCollectionUi.ApplyHighContrast(list);
        }

        static TextBlock RecordTitle(string name)
        {
            var title = Text(name, 18, true); title.MaxHeight = 24; title.TextWrapping=TextWrapping.NoWrap; title.TextTrimming = TextTrimming.CharacterEllipsis; title.ToolTip = name; return title;
        }
        sealed class CategoryGroupConverter : IValueConverter
        {
            public object Convert(object value,Type targetType,object parameter,CultureInfo culture)=>CategoryName(value as string);
            public object ConvertBack(object value,Type targetType,object parameter,CultureInfo culture)=>throw new NotSupportedException();
        }
        static string CategoryName(string category)=>string.IsNullOrWhiteSpace(category)?"Other":category;
        Dictionary<string,string> CategorySubtotals()
        {
            return states.GroupBy(s=>CategoryName(s.Item.Category)).ToDictionary(g=>g.Key,g=>
            {
                var records=selected?.Milestones.Count>0?g.GroupBy(s=>s.Item.GroupId).Select(r=>r.Any(s=>s.Complete)).ToList():g.Select(s=>s.Complete).ToList();
                int done=records.Count(complete=>complete),total=records.Count;
                string text=done+" / "+total+" complete";
                if(selected!=null&&selected.CategoryTargets.TryGetValue(g.Key,out var target))text+=" · Goal "+Math.Min(done,target)+" / "+target;
                return text;
            });
        }
        sealed class CategorySubtotalConverter : IValueConverter
        {
            readonly Dictionary<string,string> subtotals;
            internal CategorySubtotalConverter(Dictionary<string,string> subtotals)=>this.subtotals=subtotals;
            public object Convert(object value,Type targetType,object parameter,CultureInfo culture)=>subtotals.GetValueOrDefault(CategoryName(value as string),"0 / 0 complete");
            public object ConvertBack(object value,Type targetType,object parameter,CultureInfo culture)=>throw new NotSupportedException();
        }
        sealed class RecordWidthConverter : IValueConverter
        {
            public object Convert(object value,Type targetType,object parameter,CultureInfo culture)=>Math.Max((double)parameter,(double)value-30);
            public object ConvertBack(object value,Type targetType,object parameter,CultureInfo culture)=>throw new NotSupportedException();
        }
        static double MilestoneWidth(string label)=>Math.Max(132,Math.Min(140,(label?.Length??0)*7)+94);

        static Grid RecordRow(FrameworkElement text, FrameworkElement indicators)
        {
            var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            indicators.Margin = new Thickness(8, 0, 0, 0); Grid.SetColumn(indicators, 1); row.Children.Add(text); row.Children.Add(indicators); return row;
        }

        ListBoxItem RecordCell(FrameworkElement content, AchievementTrackerState state)
        {
            var card = new Border { Child = content, Height=34, Padding = new Thickness(10, 3, 10, 3), CornerRadius = new CornerRadius(6),
                Background = SystemParameters.HighContrast ? SystemColors.WindowBrush : Color("#17252D"),
                BorderBrush = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : Color("#385260"), BorderThickness = new Thickness(1) };
            var item = new ListBoxItem { Content = card, Tag = state, Padding = new Thickness(0), Margin = new Thickness(0, 0, 8, 3),
                HorizontalContentAlignment = HorizontalAlignment.Stretch, Foreground = Foreground };
            // The card owns the frame, including its selected appearance.
            item.Template = new ControlTemplate(typeof(ListBoxItem)) { VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)) };
            item.Selected += (_, __) => card.BorderBrush = SystemParameters.HighContrast ? SystemColors.HighlightBrush : Color("#8CD1DF");
            item.Unselected += (_, __) => card.BorderBrush = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : Color("#385260");
            AutomationProperties.SetName(item, state.Item.Name);
            AccessibilityUi.TryApplyFocusVisualStyle(item); return item;
        }

        ToggleButton ProgressToggle(AchievementTrackerState state, string label)
        {
            var button = new ToggleButton { IsChecked = state.Complete, IsThreeState = false, MinWidth = 108, MinHeight = 25,Height=25,
                Padding = new Thickness(4, 0, 4, 0), Margin = new Thickness(4, 0, 0, 0), BorderThickness = new Thickness(1),
                HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
                IsEnabled = selected != null && !string.IsNullOrWhiteSpace(Account(selected)), Tag = state };
            var value = state.Complete ? "Completed" : "Not Complete";
            var ranked = !string.IsNullOrEmpty(state.Item.Milestone);
            var content = new StackPanel { Orientation=Orientation.Horizontal }; var title = Text(ranked ? label : value, ranked ? 13 : 14, true); var statusText = Text(value, 13);
            title.TextWrapping=statusText.TextWrapping=TextWrapping.NoWrap;
            if(ranked){button.Width=MilestoneWidth(label);title.MaxWidth=button.Width-94;title.TextTrimming=TextTrimming.CharacterEllipsis;statusText.Margin=new Thickness(4,0,0,0);}
            title.HorizontalAlignment = statusText.HorizontalAlignment = HorizontalAlignment.Center;
            title.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding("Foreground") { Source = button });
            statusText.SetBinding(TextBlock.ForegroundProperty, new System.Windows.Data.Binding("Foreground") { Source = button });
            content.Children.Add(title); if (ranked) content.Children.Add(statusText); button.Content = content;
            button.ToolTip = state.Item.Name + " · " + label + ": " + value + (state.IsManual ? " · Manual" : "")
                + (state.HasConflict ? " · Manual conflict" : !state.IsKnown ? " · No completion observation yet" : "")
                + "\nClick to " + (state.Complete ? "mark remaining" : "mark complete") + ". Focus this button to view its details and notes.";
            AutomationProperties.SetName(button, state.Item.Name + " · " + label);
            AutomationProperties.SetHelpText(button, value + ". " + (state.Complete ? "Activate to mark remaining." : "Activate to mark complete."));
            var template = new ControlTemplate(typeof(ToggleButton)); var frame = new FrameworkElementFactory(typeof(Border));
            frame.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            frame.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            frame.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            frame.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            frame.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); frame.AppendChild(presenter); template.VisualTree = frame; button.Template = template;
            var style = new Style(typeof(ToggleButton));
            style.Setters.Add(new Setter(Control.BackgroundProperty, SystemParameters.HighContrast ? SystemColors.ControlBrush : Color("#243B48")));
            style.Setters.Add(new Setter(Control.ForegroundProperty, SystemParameters.HighContrast ? SystemColors.ControlTextBrush : Color("#E3EDF2")));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, SystemParameters.HighContrast ? SystemColors.WindowTextBrush : Color("#5B7B8D")));
            var earned = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
            earned.Setters.Add(new Setter(Control.BackgroundProperty, SystemParameters.HighContrast ? SystemColors.HighlightBrush : Color("#215444")));
            earned.Setters.Add(new Setter(Control.ForegroundProperty, SystemParameters.HighContrast ? SystemColors.HighlightTextBrush : Color("#E1F6EA")));
            earned.Setters.Add(new Setter(Control.BorderBrushProperty, SystemParameters.HighContrast ? SystemColors.HighlightTextBrush : Color("#91D9B5"))); style.Triggers.Add(earned);
            var focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
            focus.Setters.Add(new Setter(Control.BorderBrushProperty, SystemParameters.HighContrast ? SystemColors.WindowTextBrush : Color("#F1D395"))); style.Triggers.Add(focus);
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Control.BorderBrushProperty, SystemParameters.HighContrast ? SystemColors.HighlightBrush : Color("#C1EAF3"))); style.Triggers.Add(hover);
            var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.55)); style.Triggers.Add(disabled); button.Style = style;
            AccessibilityUi.TryApplyFocusVisualStyle(button);
            button.GotKeyboardFocus += (_, __) => ShowItem(state);
            button.Click += async (_, e) => { e.Handled = true; button.IsEnabled = false; button.IsChecked = !state.Complete; ShowItem(state); await (PendingEdit = SetProgressAsync(state, state.Complete ? 0 : state.Item.Target)); };
            return button;
        }

        void ShowItem(AchievementTrackerState item)
        {
            selectedItem = item; detailTitle.Text = item == null ? "Select an item" : item.Item.Name + (string.IsNullOrEmpty(item.Item.Milestone) ? "" : " · " + item.Item.Milestone); current.Text = item?.Current?.ToString(CultureInfo.CurrentCulture) ?? ""; notes.Text = item?.Note ?? "";
            var enabled = item != null && selected != null && !string.IsNullOrWhiteSpace(Account(selected));
            saveNote.IsEnabled = saveCount.IsEnabled = notes.IsEnabled = current.IsEnabled = enabled; reset.IsEnabled = enabled && item.IsManual;
            countEditor.Visibility = item?.Item.Target > 1 ? Visibility.Visible : Visibility.Collapsed;
            reset.Visibility = item?.IsManual == true ? Visibility.Visible : Visibility.Collapsed;
            itemSource.Text = item == null ? "Choose a row to view its details or add a note." : item.Status + (item.Item.Target > 1 ? " · Target " + item.Item.Target : "")
                + (item.IsManual ? "\nManual count: " + item.ManualCurrent + " · Source count: " + (item.ReportedCurrent?.ToString() ?? "Unknown")
                    + (item.HasConflict ? "\nThese values differ. Keep manual progress or use the latest source." : "\nManual progress overrides the imported count.")
                    : item.ObservedUtcTicks > 0 ? "\nFrom imported progress." : "\nProgress has not been checked.")
                + (string.IsNullOrWhiteSpace(item.Item.Detail) ? "" : "\n\n" + item.Item.Detail);
        }

        async Task SaveNoteAsync()
        {
            if (selectedItem == null || selected == null) return;
            var id = selected.Id; var account = Account(selected); var itemId = selectedItem.Item.Id; var text = notes.Text;
            try { await Task.Run(() => store.SaveNote(id, account, itemId, text)); await ReloadAsync(); status.Text = "Note saved."; }
            catch (Exception ex) { Error(ex.Message); }
        }

        async Task SetProgressAsync(AchievementTrackerState item, int? count)
        {
            var definition = selected; if (definition == null) return;
            try { await Task.Run(() => store.SetManualProgress(definition.Id, Account(definition), item.Item.Id, count)); await ReloadAsync(); }
            catch (Exception ex) { await ReloadAsync(); Error("Could not save progress. " + ex.Message); }
        }

        async Task CreateAsync(bool spreadsheet = false)
        {
            var options = games.Where(g => string.IsNullOrEmpty(provider) || g.Provider == provider).Where(g => string.IsNullOrEmpty(gameId) || g.Id == gameId).ToList();
            var context = selected ?? new AchievementTrackerDefinition { Provider = provider, GameId = gameId };
            var dialog = new AchievementTrackerEditWindow(this, options, context, achievementId, achievements, spreadsheet);
            if (ShowEditor(dialog) != true || dialog.Definition == null) return;
            try { await Task.Run(() => store.AddSpreadsheetDefinition(dialog.Definition,Account(dialog.Definition),dialog.InitialProgress,dialog.InitialObservedUtc,dialog.InitialFingerprint)); preferredId = dialog.Definition.Id; await LoadDefinitionsAsync(); }
            catch (Exception ex) { Error("Could not create the checklist. " + ex.Message); }
        }

        async Task EditLinkAsync()
        {
            var definition = selected; if (definition == null) return;
            var dialog = new Window { Title = "Tracker achievement link", Width = 560, Height = 260, MinWidth = 420, MinHeight = 240,
                Background = Background, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = IsVisible ? this : null };
            var panel = new StackPanel { Margin = new Thickness(22) }; dialog.Content = panel;
            panel.Children.Add(Text(definition.GameTitle, 18, true));
            panel.Children.Add(Text("Choose an achievement, or keep this tracker game-wide. Saved progress and notes stay with the tracker.", 13));
            var picker = new TrackerAchievementPicker(achievements) { Margin = new Thickness(0, 12, 0, 12) };
            picker.SelectGame(definition.Provider, definition.GameId, definition.AchievementId); panel.Children.Add(picker);
            var buttons = new WrapPanel(); panel.Children.Add(buttons);
            var save = Button("Save link", "Save optional achievement link"); buttons.Children.Add(save);
            save.Click += (_, __) => dialog.DialogResult = true;
            var cancel = Button("Cancel", "Cancel achievement link edit"); buttons.Children.Add(cancel); cancel.Click += (_, __) => dialog.DialogResult = false;
            AchievementCollectionUi.ApplyHighContrast(dialog);
            if (dialog.ShowDialog() != true) return;
            var achievement = picker.Achievement;
            try { await Task.Run(() => store.SetAchievementLink(definition.Id, achievement)); preferredId = definition.Id; await LoadDefinitionsAsync(); }
            catch (Exception ex) { Error("Could not save the achievement link. " + ex.Message); }
        }

        async Task ExportAsync(string mode)
        {
            var definition=selected;if(definition==null)return;
            bool json=mode=="Definition JSON", withProgress=mode=="Account progress Excel";
            if(withProgress && string.IsNullOrWhiteSpace(Account(definition))) { Error("Connect an account before exporting its progress.");return; }
            var dialog=new SaveFileDialog { Title=mode,Filter=json?"Tracker definition (*.json)|*.json":"Excel workbook (*.xlsx)|*.xlsx",FileName=json?"tracker-definition.json":withProgress?"tracker-progress.xlsx":"tracker-template.xlsx",AddExtension=true };
            if(dialog.ShowDialog(this)!=true)return;
            try
            {
                var loaded=withProgress?await Task.Run(()=>store.Load(definition.Id,Account(definition))):null;
                await Task.Run(()=> { if(json)File.WriteAllText(dialog.FileName,TrackerDefinitionTransfer.Serialize(definition));else TrackerSpreadsheetProgress.ExportWorkbook(dialog.FileName,definition,loaded); });
                status.Text=withProgress?"Account progress exported. Definition exports and blank templates contain no personal progress.":"Definition/template exported with stable item IDs and no personal progress.";
            }
            catch(Exception ex){Error("Could not export tracker: "+ex.Message);}
        }

        void Error(string message) { status.Text = message; status.Foreground = Color("#F0B7A6"); }
        internal static Brush Color(string hex) => SystemParameters.HighContrast ? SystemColors.WindowTextBrush : UiBrushHelper.FromHex(hex);
        internal static TextBlock Text(string text, double size, bool bold = false) => new() { Text = text, FontSize = Math.Max(14,size), FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : UiBrushHelper.FromHex("#D9E6ED"), TextWrapping = TextWrapping.Wrap };
        internal static void DressEditor(TextBox editor)
        {
            editor.Background = SystemParameters.HighContrast ? SystemColors.WindowBrush : UiBrushHelper.FromHex("#151F25");
            editor.Foreground = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : Brushes.White;
            editor.CaretBrush = editor.Foreground; editor.BorderBrush = UiBrushHelper.FromHex("#34454F");
        }
        internal static Button Button(string text, string hint) { var b = new Button { Content = text, ToolTip = hint, FontSize = 15, MinHeight = 36, MinWidth = 100, Margin = new Thickness(0, 0, 8, 6), Padding = new Thickness(12, 6, 12, 6),
            Background = SystemParameters.HighContrast ? SystemColors.ControlBrush : UiBrushHelper.FromHex("#243640"), Foreground = SystemParameters.HighContrast ? SystemColors.ControlTextBrush : Brushes.White,
            BorderBrush = SystemParameters.HighContrast ? SystemColors.ControlTextBrush : UiBrushHelper.FromHex("#415561") };
            if (!SystemParameters.HighContrast)
            {
                b.Style = LibraryButtonChrome.LibraryToolbarButtonStyle("#243640", "#415561", "#304955", "#1A2B34");
                var template = LibraryButtonChrome.BuildLibraryToolbarButtonTemplate("#243640", "#415561", "#304955", "#1A2B34");
                template.VisualTree.FirstChild.SetValue(FrameworkElement.MarginProperty, new TemplateBindingExtension(Control.PaddingProperty));
                template.VisualTree.FirstChild.SetValue(FrameworkElement.HorizontalAlignmentProperty,new TemplateBindingExtension(Control.HorizontalContentAlignmentProperty));b.Template = template;
            }
            if(text=="Cancel"||text=="Close")b.IsCancel=true;
            if(!SystemParameters.HighContrast && (text.StartsWith("Save",StringComparison.Ordinal)||text.StartsWith("Create",StringComparison.Ordinal)||text.StartsWith("Apply",StringComparison.Ordinal)||text=="Import definition"||text=="Refresh"))
            {
                b.MinHeight=38;
                b.Style=LibraryButtonChrome.LibraryToolbarButtonStyle("#245967","#79BBC8","#307183","#1C4652");
                var template=LibraryButtonChrome.BuildLibraryToolbarButtonTemplate("#245967","#79BBC8","#307183","#1C4652");template.VisualTree.FirstChild.SetValue(FrameworkElement.MarginProperty,new TemplateBindingExtension(Control.PaddingProperty));template.VisualTree.FirstChild.SetValue(FrameworkElement.HorizontalAlignmentProperty,new TemplateBindingExtension(Control.HorizontalContentAlignmentProperty));b.Template=template;
            }
            AutomationProperties.SetName(b, hint); AccessibilityUi.TryApplyFocusVisualStyle(b); return b; }
    }
}
