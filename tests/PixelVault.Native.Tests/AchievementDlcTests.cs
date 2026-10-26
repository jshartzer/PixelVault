using Microsoft.Data.Sqlite;
using PixelVaultNative;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;
using static PixelVaultNative.Tests.AchievementCollectionAccessTests;

namespace PixelVaultNative.Tests;

public sealed class AchievementDlcTests
{
    [Fact]
    public void Hierarchy_PutsBaseFirstAndStoryFirstInEveryDlc_AndSeparatesProgressAndCollapseKeys()
    {
        var rows = new[] { Row("base-other", "Collectibles"), Row("base-story", "Story", order: 100),
            Row("dlc-other", "Difficulty", "Frozen Wilds"), Row("dlc-story", "Story", "Frozen Wilds", 100), Row("unknown", "", "Second DLC"),
            Row("base-actions", "Actions", order: 1000), Row("dlc-actions", "Actions", "Frozen Wilds", 1000) };
        rows[0].Unlocked = true; rows[4].ProgressKnown = false;
        var tree = AchievementsInfoWindow.BuildAchievementContentGroups(rows);
        Assert.Equal(new[] { "Base Game", "DLC: Frozen Wilds", "DLC: Second DLC" }, tree.Select(g => g.Name));
        Assert.Equal(new[] { "Story", "Actions", "Collectibles" }, tree[0].Categories.Select(c => c.Name));
        Assert.Equal(new[] { "Story", "Actions", "Difficulty" }, tree[1].Categories.Select(c => c.Name));
        Assert.NotEqual(tree[0].Categories[0].Key, tree[1].Categories[0].Key);
        Assert.Equal(33, tree[0].Percent); Assert.Equal(0, tree[1].Percent); Assert.Null(tree[2].Percent);
        Assert.Equal("Uncategorized", tree[2].Categories.Single().Name);
        Assert.Equal(rows.Length, tree.SelectMany(g => g.Categories).Sum(c => c.Total));
    }

    [Fact]
    public void DlcAndCategoryEdits_PersistIndependentlyWithoutChangingGuidePinsOrProviderProgress()
    {
        using var scope = new AchievementPinTests.PinScope();
        var a = Row("A", ""); var b = Row("B", "");
        var entry = scope.Service.SyncDefinitionsAndLoadGuides("fixture", new[] { a, b })[0];
        scope.Service.SaveGuide(new AchievementGuideEdit { AchievementId = entry.AchievementId, GroupName = "Story", GuideText = "Notes", Tags = "missable", IsMissable = true });
        scope.Service.SetAchievementPinned(a, true);
        var before = scope.Service.LoadGuideEntry("steam", "123", "A");
        var assigned = scope.Service.AssignAchievementDlc(a, "Frozen Wilds");
        Assert.Equal(before.GuideUpdatedUtcTicks, assigned.GuideUpdatedUtcTicks);
        Assert.Equal("Story", assigned.GroupName); Assert.Equal("Notes", assigned.GuideText);
        AchievementsInfoWindow.SaveCategory(scope.Service, a, "Collectibles", 2);
        var restart = new AchievementGuideService(scope.Path);
        var fresh = Row("A", ""); restart.LoadSavedAchievementGroups(new[] { fresh });
        Assert.Equal("Frozen Wilds", fresh.DlcName); Assert.Equal("Collectibles", fresh.GroupName);
        Assert.True(fresh.IsMissable); Assert.False(fresh.Unlocked); Assert.True(fresh.ProgressKnown);
        Assert.Equal("Frozen Wilds", Assert.Single(restart.LoadPinnedAchievements("steam", "123")).DlcName);
        restart.AssignAchievementDlc(fresh, "");
        Assert.Equal("Collectibles", restart.LoadGuideEntry("steam", "123", "A").GroupName);
        Assert.Empty(restart.LoadGuideEntry("steam", "123", "B").DlcName);
        Assert.Contains("Frozen Wilds", restart.LoadDlcNames("steam", "123")); // Empty DLC remains reusable.
        Assert.Empty(restart.LoadDlcNames("steam", "another-game"));
        Assert.Empty(restart.LoadDlcNames("retroachievements", "123"));
    }

    [Fact]
    public void LegacyDlcMigration_PreservesGuidesAndMembershipAcrossRestart()
    {
        using var scope = new AchievementPinTests.PinScope();
        var a = Row("A", ""); var b = Row("B", "");
        var entry = scope.Service.SyncDefinitionsAndLoadGuides("fixture", new[] { a, b })[0];
        scope.Service.SaveGuide(new AchievementGuideEdit { AchievementId = entry.AchievementId, GuideText = "Keep note", GroupName = "Story" });
        using (var connection = new SqliteConnection("Data Source=" + scope.Service.DatabasePath))
        {
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = "UPDATE achievement_definition SET group_name='DLC: Frozen Wilds' WHERE provider_achievement_id='A'; ALTER TABLE achievement_definition DROP COLUMN dlc_name;";
            command.ExecuteNonQuery();
        }
        var restart = new AchievementGuideService(scope.Path);
        restart.LoadSavedAchievementGroups(new[] { a, b });
        Assert.Equal("Frozen Wilds", a.DlcName); Assert.Empty(a.GroupName);
        Assert.Empty(b.DlcName); Assert.Equal("Keep note", restart.LoadGuideEntry("steam", "123", "A").GuideText);
        Assert.Contains("Frozen Wilds", restart.LoadDlcNames("steam", "123"));
        restart.LoadSavedAchievementGroups(new[] { a, b });
        Assert.Equal("Frozen Wilds", a.DlcName);
    }

    [Fact]
    public void Imports_SupportSeparateDlcAndCategory_AndOmittedDlcPreservesMembership()
    {
        using var scope = new AchievementPinTests.PinScope();
        var row = Row("A", ""); scope.Service.SyncDefinitionsAndLoadGuides("fixture", new[] { row });
        string Bundle(string fields) => "{\"schemaVersion\":1,\"provider\":\"steam\",\"providerGameId\":\"123\",\"achievements\":[{\"providerAchievementId\":\"A\"," + fields + "}]}";
        scope.Service.ImportGuideBundle(Bundle("\"dlcName\":\"Expansion\",\"groupName\":\"Story\""), "steam", "123");
        scope.Service.ImportGuideBundle(Bundle("\"groupName\":\"Difficulty\""), "steam", "123");
        var entry = scope.Service.LoadGuideEntry("steam", "123", "A");
        Assert.Equal("Expansion", entry.DlcName); Assert.Equal("Difficulty", entry.GroupName);
        scope.Service.ImportGuideBundle(Bundle("\"dlcName\":\"\""), "steam", "123");
        Assert.Empty(scope.Service.LoadGuideEntry("steam", "123", "A").DlcName);
        scope.Service.ImportGuideBundle(Bundle("\"groupName\":\"DLC: Legacy Expansion\""), "steam", "123");
        entry = scope.Service.LoadGuideEntry("steam", "123", "A");
        Assert.Equal("Legacy Expansion", entry.DlcName); Assert.Equal("Difficulty", entry.GroupName);
        Assert.False(scope.Service.PreviewGuideImport(Bundle("\"dlcName\":\"Base Game\""), "steam", "123").IsValid);
    }

    [Fact]
    public void LegacyPlainDlcNames_MigrateOnlyTheKnownProviderGameMappingOnce()
    {
        using var scope = new AchievementPinTests.PinScope();
        var expansion = AchievementPinTests.Row("A", "292030");
        var category = AchievementPinTests.Row("B", "292030");
        var unrelated = AchievementPinTests.Row("C", "other-game");
        scope.Service.SyncDefinitionsAndLoadGuides("fixture", new[] { expansion, category, unrelated });
        using (var connection = new SqliteConnection("Data Source=" + scope.Service.DatabasePath))
        {
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = "UPDATE achievement_definition SET group_name=CASE WHEN provider_achievement_id='B' THEN 'Difficulty' ELSE 'Hearts of Stone' END; DELETE FROM achievement_organization_migration;";
            command.ExecuteNonQuery();
        }
        scope.Service.LoadSavedAchievementGroups(new[] { expansion, category, unrelated });
        Assert.Equal("Hearts of Stone", expansion.DlcName); Assert.Empty(expansion.GroupName);
        Assert.Equal("Difficulty", category.GroupName); Assert.Empty(category.DlcName);
        Assert.Equal("Hearts of Stone", unrelated.GroupName); Assert.Empty(unrelated.DlcName);
        var entry = scope.Service.LoadGuideEntry("steam", "292030", "B");
        scope.Service.SaveGuide(new AchievementGuideEdit { AchievementId = entry.AchievementId, GroupName = "Hearts of Stone" });
        Assert.Empty(scope.Service.LoadGuideEntry("steam", "292030", "B").DlcName); // Authored categories after migration are not reinterpreted.
    }

    [Fact]
    public void ListAndGrid_CollapseStoryIndependentlyWithinEachContentSection()
    {
        OnSta(() =>
        {
            using var scope = new AchievementPinTests.PinScope();
            var rows = new[] { Row("A", ""), Row("B", ""), Row("C", "") };
            var entries = scope.Service.SyncDefinitionsAndLoadGuides("fixture", rows);
            foreach (var entry in entries) scope.Service.SaveGuide(new AchievementGuideEdit { AchievementId = entry.AchievementId, GroupName = entry.ProviderAchievementId == "C" ? "Collectibles" : "Story", DlcName = entry.ProviderAchievementId == "B" ? "Expansion" : "" });
            var window = CreateWindow(() => Task.FromResult(Result(rows)), scope.Service);
            try
            {
                Click(Field<Button>(window, "_refresh"));
                foreach (var mode in new[] { "_listView", "_gridView" })
                {
                    Click(Field<Button>(window, mode));
                    var body = mode == "_listView" ? (DependencyObject)Field<ListBox>(window, "_list") : Field<StackPanel>(window, "_gridContent");
                    var story = Descendants(body).OfType<Button>().Single(button => AutomationProperties.GetName(button) == "Collapse Story category in Base Game");
                    Click(story);
                    Assert.DoesNotContain(Descendants(body).OfType<Border>(), card => card.Tag == rows[0]);
                    Assert.Contains(Descendants(body).OfType<Border>(), card => card.Tag == rows[1]);
                    var expand = Descendants(body).OfType<Button>().Single(button => AutomationProperties.GetName(button) == "Expand Story category in Base Game");
                    Click(expand);
                }
                var root = (FrameworkElement)window.Content;
                if (root is Panel panel) panel.Background = window.Background;
                foreach (var mode in new[] { "_listView", "_gridView" })
                foreach (var width in new[] { 680d, 1040d })
                {
                    Click(Field<Button>(window, mode));
                    var height = mode == "_listView" ? 980 : 1500;
                    root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout();
                    var directory = Environment.GetEnvironmentVariable("PIXELVAULT_DLC_PREVIEW");
                    if (!string.IsNullOrWhiteSpace(directory))
                    {
                        Directory.CreateDirectory(directory);
                        var bitmap = new RenderTargetBitmap((int)width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
                        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        using var file = File.Create(Path.Combine(directory, "dlc" + mode + "-" + width + ".png")); encoder.Save(file);
                    }
                }
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void Categories_AreSharedAcrossGamesAndProviders_AndPersistWithoutAssignedAchievements()
    {
        OnSta(() =>
        {
            using var scope = new AchievementPinTests.PinScope();
            var a = AchievementPinTests.Row("A");
            var b = AchievementPinTests.Row("B", "456");
            var c = AchievementPinTests.Row("C", "789", "retroachievements");
            scope.Service.SyncDefinitionsAndLoadGuides("fixture", new[] { a, b, c });
            Assert.All(AchievementOrganization.DefaultCategories, name => Assert.Contains(name, scope.Service.LoadCategoryNames()));
            AchievementsInfoWindow.SaveCategory(scope.Service, a, "Exploration", 0);
            scope.Service.AssignAchievementDlc(a, "Expansion");
            AchievementsInfoWindow.SaveCategory(scope.Service, a, "Actions", 0);
            var restart = new AchievementGuideService(scope.Path);
            Assert.Contains("Exploration", restart.LoadCategoryNames());
            Assert.DoesNotContain("Expansion", restart.LoadCategoryNames());
            var menu = new ContextMenu();
            AchievementsInfoWindow.AddCategoryMenu(menu, c, new[] { c }, name => AchievementsInfoWindow.SaveCategory(restart, c, name, 0),
                () => true, null!, sharedCategories: () => restart.LoadCategoryNames());
            var category = (MenuItem)menu.Items[0];
            category.RaiseEvent(new RoutedEventArgs(MenuItem.SubmenuOpenedEvent));
            Assert.Equal(new[] { "Story", "Actions" }, category.Items.OfType<MenuItem>().Take(2).Select(item => (string)item.Header));
            category.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Exploration")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Equal("Exploration", restart.LoadGuideEntry("retroachievements", "789", "C").GroupName);
            Assert.Equal("Actions", restart.LoadGuideEntry("steam", "123", "A").GroupName);
            Assert.Empty(restart.LoadGuideEntry("steam", "456", "B").GroupName);
            restart.ImportGuideBundle("""{"schemaVersion":1,"provider":"steam","providerGameId":"456","achievements":[{"providerAchievementId":"B","groupName":"Speedruns"}]}""", "steam", "456");
            Assert.Contains("Speedruns", restart.LoadCategoryNames());
        });
    }

    [Fact]
    public void SavedCategoryOrder_OverridesDefaultsInBaseDlcListGridAndMenus_AndSurvivesRestart()
    {
        OnSta(() =>
        {
            using var scope = new AchievementPinTests.PinScope();
            var rows = Enumerable.Range(0, 6).Select(i => AchievementPinTests.Row(i.ToString())).ToArray();
            var entries = scope.Service.SyncDefinitionsAndLoadGuides("fixture", rows);
            var categories = new[] { "Story", "Actions", "Collectibles" };
            for (var i = 0; i < entries.Count; i++) scope.Service.SaveGuide(new AchievementGuideEdit
            {
                AchievementId = entries[i].AchievementId, GroupName = categories[i % 3], DlcName = i < 3 ? "" : "Expansion", GuideText = "Keep note " + i
            });
            scope.Service.SaveCategoryOrder(new[] { "Collectibles", "Actions", "Story" });
            var restart = new AchievementGuideService(scope.Path);
            Assert.Equal(new[] { "Collectibles", "Actions", "Story" }, restart.LoadCategoryNames().Take(3));
            var window = CreateWindow(() => Task.FromResult(Result(rows)), restart);
            try
            {
                Click(Field<Button>(window, "_refresh"));
                foreach (var mode in new[] { "_listView", "_gridView" })
                {
                    Click(Field<Button>(window, mode));
                    var body = mode == "_listView" ? (DependencyObject)Field<ListBox>(window, "_list") : Field<StackPanel>(window, "_gridContent");
                    var headers = Descendants(body).OfType<Button>().Select(AutomationProperties.GetName).Where(name => name.Contains(" category in ")).ToList();
                    Assert.Equal(new[] { "Base Game", "DLC: Expansion" }.SelectMany(parent => new[] { "Collectibles", "Actions", "Story" }
                        .Select(category => "Collapse " + category + " category in " + parent)), headers);
                    var card = Descendants(body).OfType<Border>().Single(border => border.Tag == rows[0]);
                    var menu = card.ContextMenu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Assign category"));
                    menu.RaiseEvent(new RoutedEventArgs(MenuItem.SubmenuOpenedEvent));
                    Assert.Equal(new[] { "Collectibles", "Actions", "Story" }, menu.Items.OfType<MenuItem>().Take(3).Select(item => (string)item.Header));
                }
                Assert.All(entries, entry => Assert.StartsWith("Keep note ", restart.LoadGuideEntry(entry.Provider, entry.ProviderGameId, entry.ProviderAchievementId).GuideText));
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void CategoryOrder_InvalidChangesAreAtomic_AndNewCategoriesAreAppended()
    {
        using var scope = new AchievementPinTests.PinScope();
        var initial = scope.Service.LoadCategoryNames().ToArray();
        var row = AchievementPinTests.Row("A"); scope.Service.SyncDefinitionsAndLoadGuides("fixture", new[] { row });
        AchievementsInfoWindow.SaveCategory(scope.Service, row, "New category", 0);
        scope.Service.SaveCategoryOrder(initial.Reverse());
        var saved = scope.Service.LoadCategoryNames().ToArray();
        Assert.Equal(initial.Reverse(), saved.Take(initial.Length));
        Assert.Equal("New category", saved.Last());
        Assert.Throws<ArgumentException>(() => scope.Service.SaveCategoryOrder(new[] { "Story", "story" }));
        Assert.Throws<ArgumentException>(() => scope.Service.SaveCategoryOrder(new[] { "Not registered" }));
        Assert.Equal(saved, scope.Service.LoadCategoryNames());
        Assert.Equal("New category", scope.Service.LoadGuideEntry("steam", "123", "A").GroupName);
    }

    static GameAchievementsFetchService.AchievementRow Row(string id, string category, string dlc = "", int order = 0)
    {
        var row = AchievementPinTests.Row(id); row.GroupName = category; row.DlcName = dlc; row.GroupSortOrder = order; return row;
    }
    internal static void SavePreview(Window window, string name, double width, double height)
    {
        var directory = Environment.GetEnvironmentVariable("PIXELVAULT_DLC_PREVIEW");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        var root = (FrameworkElement)window.Content;
        if (root is Panel panel) panel.Background = window.Background;
        root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(directory, name)); encoder.Save(file);
    }
    static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var nested in Descendants(child)) yield return nested;
    }
}
