using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using PixelVaultNative;
using Xunit;
using static PixelVaultNative.Tests.AchievementCollectionAccessTests;

namespace PixelVaultNative.Tests;

public sealed class AchievementPinUiTests
{
    [Fact]
    public void CategoryMenu_HandlesEarnedAndRemainingRowsAndPersistsWithoutLosingGuide()
    {
        OnSta(() =>
        {
            using var scope = new AchievementPinTests.PinScope();
            var a = AchievementPinTests.Row("A");
            var b = AchievementPinTests.Row("B"); b.Unlocked = true;
            var entries = scope.Service.SyncDefinitionsAndLoadGuides("fixture", new[] { a, b });
            scope.Service.SaveGuide(new AchievementGuideEdit { AchievementId = entries[0].AchievementId,
                GuideText = "Keep this note", Tags = "missable", IsMissable = true,
                SourceUrl = "https://example.com/guide", SourceTitle = "Guide" });
            scope.Service.SetAchievementPinned(a, true);
            var window = CreateWindow(() => Task.FromResult(Result(a, b)), scope.Service);
            try
            {
                new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();
                Click(Field<Button>(window, "_refresh"));
                var grid = Field<StackPanel>(window, "_gridContent");
                window.Dispatcher.BeginInvoke(new Action(() =>
                {
                    var dialog = window.OwnedWindows.Cast<Window>().Single();
                    try
                    {
                        Assert.Equal("Name DLC", dialog.Title);
                        var input = Descendants(dialog).OfType<TextBox>().Single();
                        var save = Buttons(dialog).Single(b => Equals(b.Content, "Assign"));
                        Assert.False(save.IsEnabled);
                        input.Text = "   ";
                        Assert.False(save.IsEnabled);
                        input.Text = "  Frozen Wilds  ";
                        Assert.True(save.IsEnabled);
                        Click(save);
                    }
                    finally { dialog.Close(); }
                }));
                AssignDlc(Card(grid, "A"), "Create New DLC…");
                Assert.Equal("Frozen Wilds", a.DlcName);
                Assign(Card(grid, "A"), "Story");
                Assert.Equal("Frozen Wilds", a.DlcName);
                var categories = Card(grid, "A").ContextMenu.Items.OfType<MenuItem>()
                    .Single(i => Equals(i.Header, "Assign category")).Items.OfType<MenuItem>().ToList();
                Assert.True(categories.Single(i => Equals(i.Header, "Story")).IsChecked);
                Assert.DoesNotContain(categories, i => Equals(i.Header, "DLC") || Equals(i.Header, "Base Game"));
                Assert.DoesNotContain(categories, i => Equals(i.Header, "DLC: Frozen Wilds"));
                Click(Field<Button>(window, "_listView"));
                Assign(Card(Field<ListBox>(window, "_list"), "B"), "Difficulty");
                Assert.Equal("Difficulty", b.GroupName);
                var restarted = new AchievementGuideService(scope.Path);
                var fresh = new[] { AchievementPinTests.Row("A"), AchievementPinTests.Row("B") };
                restarted.SyncDefinitionsAndLoadGuides("fixture", fresh);
                Assert.Equal(new[] { "Story", "Difficulty" }, fresh.Select(r => r.GroupName));
                Assert.Equal("Frozen Wilds", fresh[0].DlcName);
                var saved = restarted.LoadGuideEntry("steam", "123", "A");
                Assert.Equal("Keep this note", saved.GuideText);
                Assert.Equal("missable", saved.Tags);
                Assert.True(saved.IsMissable);
                Assert.Equal("https://example.com/guide", saved.SourceUrl);
                Assert.Equal("Guide", saved.SourceTitle);
                Assert.Single(restarted.LoadPinnedAchievements("steam", "123"));
                AssignDlc(Card(Field<ListBox>(window, "_list"), "A"), "Base Game");
                Assert.Empty(scope.Service.LoadGuideEntry("steam", "123", "A").DlcName);
                Assert.Equal("Story", scope.Service.LoadGuideEntry("steam", "123", "A").GroupName);
                Assert.Contains("Frozen Wilds", scope.Service.LoadDlcNames("steam", "123"));
            }
            finally { window.Close(); }
        });

        static void Assign(Border card, string name)
        {
            var menu = card.ContextMenu.Items.OfType<MenuItem>().Single(i => Equals(i.Header, "Assign category"));
            menu.Items.OfType<MenuItem>().Single(i => Equals(i.Header, name))
                .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        }
        static void AssignDlc(Border card, string name)
        {
            var menu = card.ContextMenu.Items.OfType<MenuItem>().Single(i => Equals(i.Header, "Assign DLC"));
            menu.RaiseEvent(new RoutedEventArgs(MenuItem.SubmenuOpenedEvent));
            var item = menu.Items.OfType<MenuItem>().Single(i => Equals(i.Header, name) || i.Header is TextBlock text && text.Text == name);
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        }
    }

    [Fact]
    public void TileAndListPinActions_UpdateStripIndependentlyOfFiltersAndFailedRefresh()
    {
        OnSta(() =>
        {
            using var scope = new AchievementPinTests.PinScope();
            var a = AchievementPinTests.Row("A"); var b = AchievementPinTests.Row("B");
            a.GlobalUnlockPercent = 0;
            a.Meta = "0% global unlocks · Locked";
            var queue = new Queue<GameAchievementsFetchService.FetchResult>(new[] { Result(a, b), new GameAchievementsFetchService.FetchResult { ErrorMessage = "Offline" } });
            var window = CreateWindow(() => Task.FromResult(queue.Dequeue()), scope.Service);
            try
            {
                Click(Field<Button>(window, "_refresh"));
                var tile = Field<StackPanel>(window, "_gridContent");
                Assert.DoesNotContain(Buttons(tile), button => AutomationProperties.GetName(button).StartsWith("Pin to Next Up:"));
                var card = Card(tile, "A");
                Assert.Contains("Achieved by 0% of players globally.", (string)card.ToolTip);
                Assert.DoesNotContain("0% global unlocks", (string)card.ToolTip);
                Assert.Contains("Global unlock percentage unavailable.", (string)Card(tile, "B").ToolTip);
                PinMenu(card, "Pin to Next Up");
                Assert.Single(scope.Service.LoadPinnedAchievements("steam", "123"));
                PinMenu(Card(tile, "A"), "Unpin from Next Up");
                Assert.Empty(scope.Service.LoadPinnedAchievements("steam", "123"));
                PinMenu(Card(tile, "A"), "Pin to Next Up");
                var strip = Field<StackPanel>(window, "_nextUpHost");
                Assert.Equal(Visibility.Visible, strip.Visibility);
                Field<CheckBox>(window, "_remainingOnly").IsChecked = true;
                Field<HashSet<string>>(window, "_collapsedGroups").Add("Base Game");
                Click(Field<Button>(window, "_listView"));
                Assert.Single(Buttons(strip), button => AutomationProperties.GetName(button).StartsWith("Next Up: open"));
                Click(Field<Button>(window, "_refresh"));
                Assert.Single(scope.Service.LoadPinnedAchievements("steam", "123"));
                Assert.Equal(Visibility.Visible, strip.Visibility);
                Click(Buttons(strip).Single(button => AutomationProperties.GetName(button) == "Unpin from Next Up: Target A"));
                Assert.Empty(scope.Service.LoadPinnedAchievements("steam", "123"));
                Assert.Equal(Visibility.Collapsed, strip.Visibility);
                Field<HashSet<string>>(window, "_collapsedGroups").Clear();
                // Re-render List, then exercise its context menu and hover text.
                Field<CheckBox>(window, "_remainingOnly").RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
                var list = Field<ListBox>(window, "_list");
                Assert.Contains("Achieved by 0% of players globally.", (string)Card(list, "A").ToolTip);
                PinMenu(Card(list, "B"), "Pin to Next Up");
                Assert.Equal("B", Assert.Single(scope.Service.LoadPinnedAchievements("steam", "123")).ProviderAchievementId);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void SuccessfulRefresh_ReconcilesEarnedWhileMissingTargetStillHasSavedGuideAction()
    {
        OnSta(() =>
        {
            using var scope = new AchievementPinTests.PinScope();
            var a = AchievementPinTests.Row("A"); var b = AchievementPinTests.Row("B");
            scope.Service.SyncDefinitionsAndLoadGuides("fixture", new[] { a, b });
            scope.Service.SetAchievementPinned(a, true); scope.Service.SetAchievementPinned(b, true);
            var earned = AchievementPinTests.Row("A"); earned.Unlocked = true;
            var queue = new Queue<GameAchievementsFetchService.FetchResult>(new[] { Result(a, b), Result(earned) });
            var window = CreateWindow(() => Task.FromResult(queue.Dequeue()), scope.Service);
            try
            {
                Click(Field<Button>(window, "_refresh"));
                Click(Field<Button>(window, "_refresh"));
                var pin = Assert.Single(scope.Service.LoadPinnedAchievements("steam", "123"));
                Assert.Equal("B", pin.ProviderAchievementId);
                Assert.False(pin.IsActive);
                var open = Buttons(Field<StackPanel>(window, "_nextUpHost")).Single(button => AutomationProperties.GetName(button).StartsWith("Next Up: open"));
                Assert.Contains("saved guide", (string)open.ToolTip);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void GuidePin_DoesNotSaveOrDiscardDraftAndStoredReadingDoesNotReactivateDefinition()
    {
        OnSta(() =>
        {
            using var scope = new AchievementPinTests.PinScope();
            var rows = new[] { AchievementPinTests.Row("A"), AchievementPinTests.Row("B") };
            var window = (AchievementGuideWindow)Activator.CreateInstance(typeof(AchievementGuideWindow), BindingFlags.Instance | BindingFlags.NonPublic,
                null, new object?[] { null, scope.Service, "Fixture", "fixture", rows, "B", null }, null)!;
            try
            {
                Assert.Equal("Target B", Field<TextBlock>(window, "_officialTitle").Text);
                Click(Field<Button>(window, "_edit"));
                Field<TextBox>(window, "_guideText").Text = "Unsaved draft.";
                Click(Buttons(Field<StackPanel>(window, "_pinActionHost")).Single());
                Assert.Equal("Unsaved draft.", Field<TextBox>(window, "_guideText").Text);
                Assert.Equal(Visibility.Visible, Field<StackPanel>(window, "_editorFields").Visibility);
                Assert.Equal("", Assert.Single(scope.Service.LoadPinnedAchievements("steam", "123")).GuideText);
                Click(Field<Button>(window, "_cancelEdit"));
                scope.Service.SyncDefinitionsAndLoadGuides("fixture", rows.Take(1));
                var pin = Assert.Single(scope.Service.LoadPinnedAchievements("steam", "123"));
                typeof(AchievementGuideWindow).GetField("_storedEntry", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, pin);
                typeof(AchievementGuideWindow).GetMethod("ReloadEntries", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, new object?[] { pin.AchievementId, null });
                Assert.False(Assert.Single(scope.Service.LoadPinnedAchievements("steam", "123")).IsActive);
                Assert.False(Field<Button>(window, "_importFile").IsEnabled);
            }
            finally { window.Close(); }
        });
    }

    static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    static void PinMenu(Border card, string header)
    {
        var action = Assert.Single(card.ContextMenu.Items.OfType<MenuItem>(), item => Equals(item.Header, header));
        Assert.Equal(header, action.Header);
        action.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
    }

    static Border Card(DependencyObject root, string id) => Descendants(root).OfType<Border>()
        .Single(border => border.Tag is GameAchievementsFetchService.AchievementRow row && row.ProviderAchievementId == id);

    static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var nested in Descendants(child)) yield return nested;
    }
    static IEnumerable<Button> Buttons(DependencyObject root)
    {
        if (root is Button button) yield return button;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var nested in Buttons(child)) yield return nested;
    }
}
