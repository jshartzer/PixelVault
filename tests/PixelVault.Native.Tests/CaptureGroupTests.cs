using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class CaptureGroupTests
{
    [Theory]
    [InlineData("Roblox_Sell Lemons 🍋_1788648631989.png", "Sell Lemons 🍋")]
    [InlineData("Roblox_My_Game_Roblox_1788648631989_2.png", "My_Game_Roblox")]
    [InlineData("Sell Lemons 🍋_Roblox_1788648631989 (2).png", "Sell Lemons 🍋")]
    [InlineData("1869456976_79268393072444_1788648631989.png", "")]
    [InlineData("RobloxScreenShot20260525_200505425.png", "")]
    [InlineData("Roblox_unknown.png", "")]
    public void ExperienceExtraction_PreservesTheWholeTitle_AndRejectsUnresolvedNames(string file, string expected)
        => Assert.Equal(expected, RobloxCaptureNaming.GetExperienceName(file));

    [Fact]
    public void ExistingAndFutureImports_GroupWithoutRewritingFiles_AndPreserveWithinGroupOrder()
    {
        using var scope = new Scope();
        var a = scope.File("Roblox_Zoo_1788648631989.png");
        var b = scope.File("Roblox_Lemons_1788648631988.png");
        var c = scope.File("Roblox_Zoo_1788648631987.png");
        var unknown = scope.File("unknown.png");
        var snapshot = scope.Store.Load(scope.Root, "roblox");
        var groups = snapshot.Project(new[] { a, b, c, unknown }, true);
        Assert.Equal(new[] { "Lemons", "Zoo", "Ungrouped" }, groups.Select(g => g.HeaderText));
        Assert.Equal(new[] { a, c }, groups[1].Files);
        Assert.Equal(4, groups.SelectMany(g => g.Files).Distinct().Count());
        Assert.Equal("capture", System.IO.File.ReadAllText(a));
        var otherGameGroup = Assert.Single(snapshot.Project(new[] { a, b }, false));
        Assert.Equal("Ungrouped", otherGameGroup.HeaderText);
        Assert.Equal(new[] { a, b }, otherGameGroup.Files);
        Assert.Equal("New Subgame", snapshot.Resolve(scope.File("Roblox_New Subgame_1788648631990.png"), true));
    }

    [Fact]
    public void ManualAssignmentAndRemoval_SurviveRestart_AndOverrideRobloxGrouping()
    {
        using var scope = new Scope();
        var files = new[] { scope.File("Roblox_Zoo_1788648631989.png"), scope.File("Roblox_Lemons_1788648631988.png") };
        scope.Store.Assign(scope.Root, "roblox", files, "Shared Moments");
        scope.Store.Assign(scope.Root, "roblox", new[] { files[0] }, "");
        var reopened = new CaptureGroupStore(scope.Database).Load(scope.Root, "roblox");
        Assert.Equal("", reopened.Resolve(files[0], true));
        Assert.Equal("Shared Moments", reopened.Resolve(files[1], true));
        Assert.Contains("Shared Moments", reopened.Names);
        Assert.Null(reopened.Grouped);
        scope.Store.SetGrouped(scope.Root, "roblox", false);
        Assert.False(new CaptureGroupStore(scope.Database).Load(scope.Root, "roblox").Grouped);
        scope.Store.SetGrouped(scope.Root, "roblox", true);
        Assert.True(scope.Store.Load(scope.Root, "roblox").Grouped);
    }

    [Fact]
    public void GroupsAndViewPreferences_AreIsolatedByLibraryAndGame_AndCaseInsensitive()
    {
        using var scope = new Scope();
        var file = scope.File("capture.png");
        scope.Store.Assign(scope.Root, "game-one", new[] { file }, "Highlights");
        scope.Store.Assign(scope.Root, "GAME-ONE", new[] { file }, "highlights");
        scope.Store.SetGrouped(scope.Root, "game-one", true);
        Assert.Single(scope.Store.Load(scope.Root, "game-one").Names);
        var otherGame = scope.Store.Load(scope.Root, "game-two");
        Assert.Empty(otherGame.Names);
        Assert.Empty(otherGame.Resolve(file, false));
        Assert.Null(otherGame.Grouped);
        var otherLibrary = scope.Store.Load(Path.Combine(scope.Root, "other-library"), "game-one");
        Assert.Empty(otherLibrary.Names);
        Assert.Null(otherLibrary.Grouped);
        Assert.Empty(otherLibrary.Resolve(file, false));
    }

    [Fact]
    public void Assignment_ChangesOnlySelectedFiles_AndFailedBatchRollsBack()
    {
        using var scope = new Scope();
        var a = scope.File("a.png"); var b = scope.File("b.png");
        scope.Store.Assign(scope.Root, "game", new[] { a }, "Campaign");
        Assert.Empty(scope.Store.Load(scope.Root, "game").Resolve(b, false));
        Assert.ThrowsAny<ArgumentException>(() => scope.Store.Assign(scope.Root, "game", new[] { a, "" }, "New group"));
        var snapshot = scope.Store.Load(scope.Root, "game");
        Assert.Equal("Campaign", snapshot.Resolve(a, false));
        Assert.DoesNotContain("New group", snapshot.Names);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ungrouped")]
    [InlineData("name\nline")]
    public void InvalidNames_AreRejected(string name) => Assert.Throws<ArgumentException>(() => CaptureGroupStore.ValidateName(name));

    [Fact]
    public void FolderAlignment_PreservesManualMembershipAndExplicitRemoval()
    {
        using var scope = new Scope();
        var a = scope.File("a.png"); var b = scope.File("b.png");
        scope.Store.Assign(scope.Root, "game", new[] { a }, "Highlights");
        scope.Store.Assign(scope.Root, "game", new[] { b }, "");
        var afterA = Path.Combine(scope.Root, "renamed-game", "a.png");
        var afterB = Path.Combine(scope.Root, "renamed-game", "b.png");
        scope.Store.MoveFile(scope.Root, a, afterA);
        scope.Store.MoveFile(scope.Root, b, afterB);
        var snapshot = scope.Store.Load(scope.Root, "game");
        Assert.Equal("Highlights", snapshot.Resolve(afterA, false));
        Assert.Empty(snapshot.Resolve(afterB, false));
        Assert.Empty(snapshot.Resolve(a, false));
        Assert.Equal(2, snapshot.Assignments.Count);
    }

    [Fact]
    public void ContextMenu_AssignsTheWholeSelection_AndToggleKeepsSelectionAndFilter()
    {
        AchievementCollectionAccessTests.OnSta(() =>
        {
            using var scope = new Scope();
            var a = scope.File("Roblox_Zoo_1788648631989.png");
            var b = scope.File("Roblox_Zoo_1788648631988.png");
            var c = scope.File("Roblox_Lemons_1788648631987.png");
            var host = (MainWindow)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            typeof(MainWindow).GetField("dataRoot", flags)!.SetValue(host, scope.Root);
            typeof(MainWindow).GetField("libraryRoot", flags)!.SetValue(host, scope.Root);
            var folder = new LibraryBrowserFolderView { GameId = "roblox", Name = "Roblox", FilePaths = new[] { a, b, c } };
            var panes = new MainWindow.LibraryBrowserPaneRefs();
            MainWindow.BuildLibraryCaptureControls(panes);
            var ws = new MainWindow.LibraryBrowserWorkingSet { Current = folder, Panes = panes, CaptureMediaFilter = MainWindow.LibraryCaptureMediaFilter.Screenshots };
            ws.DetailFilesDisplayOrder.AddRange(new[] { a, b, c });
            ws.SelectedDetailFiles.UnionWith(new[] { a, b });
            var tile = new System.Windows.Controls.Border { ContextMenu = new System.Windows.Controls.ContextMenu() };
            var renders = 0;
            Action redraw = () => renders++;
            typeof(MainWindow).GetMethod("AttachCaptureGroupMenu", flags)!.Invoke(host, new object[] { tile, ws, folder, a, redraw });
            tile.ContextMenu.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.ContextMenu.OpenedEvent));
            var add = (System.Windows.Controls.MenuItem)tile.ContextMenu.Items[0];
            Assert.Equal("Create New Group…", ((System.Windows.Controls.MenuItem)add.Items[add.Items.Count - 1]).Header);
            var lemons = add.Items.OfType<System.Windows.Controls.MenuItem>().Single(item => item.Header is System.Windows.Controls.TextBlock text && text.Text == "Lemons");
            lemons.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
            var store = new CaptureGroupStore(Path.Combine(scope.Root, "capture-groups.sqlite"));
            var snapshot = store.Load(scope.Root, "roblox");
            Assert.Equal("Lemons", snapshot.Resolve(a, true)); Assert.Equal("Lemons", snapshot.Resolve(b, true));
            Assert.Equal(2, snapshot.Assignments.Count); // Unselected c remains automatic.
            Assert.True(snapshot.Grouped);
            panes.CaptureGroupsToggle.IsChecked = false;
            typeof(MainWindow).GetMethod("ToggleCaptureGroupView", flags)!.Invoke(host, new object[] { ws, redraw });
            Assert.False(store.Load(scope.Root, "roblox").Grouped);
            Assert.Equal(new[] { a, b }, ws.SelectedDetailFiles);
            Assert.Equal(MainWindow.LibraryCaptureMediaFilter.Screenshots, ws.CaptureMediaFilter);
            Assert.Equal(2, renders);
            ((System.Windows.Controls.MenuItem)tile.ContextMenu.Items[1]).RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
            Assert.Empty(store.Load(scope.Root, "roblox").Resolve(a, true));
            Assert.Empty(store.Load(scope.Root, "roblox").Resolve(b, true));
        });
    }

    [Fact]
    public void GroupedMasonry_AddsHeadersAndKeepsEveryPhotoInDisplayOrder()
    {
        AchievementCollectionAccessTests.OnSta(() =>
        {
            var groups = new List<LibraryDetailRenderGroup> { new() { HeaderText = "Lemons", Files = new() { "a.png", "b.png" } }, new() { HeaderText = "Zoo", Files = new() { "c.png" } } };
            var host = (MainWindow)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
            var method = typeof(MainWindow).GetMethod("BuildLibraryContinuousMosaicRowDefinitions", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            foreach (var width in new[] { 480d, 1280d })
            {
                object?[] args = { null, null, groups, null, null, null, width, 220, 1d, false, null, null, null, null, null, null, null, true, 0d, true };
                var rows = (List<MainWindow.VirtualizedRowDefinition>)method.Invoke(host, args)!;
                Assert.Equal(new[] { "a.png", "b.png", "c.png" }, rows.Where(row => row.Files != null).SelectMany(row => row.Files));
                var headers = rows.Where(row => row.Files == null || row.Files.Count == 0).ToList();
                Assert.Equal(2, headers.Count);
                Assert.Equal("Lemons  ·  2", ((System.Windows.Controls.TextBlock)((System.Windows.Controls.Border)headers[0].Build()).Child).Text);
                args[17] = false;
                var allRows = (List<MainWindow.VirtualizedRowDefinition>)method.Invoke(host, args)!;
                Assert.All(allRows, row => Assert.NotEmpty(row.Files));
                Assert.Equal(new[] { "a.png", "b.png", "c.png" }, allRows.SelectMany(row => row.Files));
            }
        });
    }

    [Fact]
    public void SparseGroups_KeepTheSamePhotoDensityAsLargerGroups()
    {
        var files = Enumerable.Range(1, 20).Select(i => "capture" + i + ".png").ToList();
        var media = files.ToDictionary(file => file, _ => new LibraryDetailMediaLayoutInfo { PixelWidth = 1920, PixelHeight = 1080 });
        foreach (var size in new[] { 180, 260, 420 })
        {
            var dense = MainWindow.BuildLibraryDetailMasonryChunks(files, 1280, 4, size, 120, 800, false, media, preserveCellDensity: true);
            var cell = dense.SelectMany(chunk => chunk.Placements).Min(p => Math.Min(p.Width, p.Height));
            foreach (var count in new[] { 1, 2 })
            {
                var sparse = MainWindow.BuildLibraryDetailMasonryChunks(files.Take(count).ToList(), 1280, 4, size, 120, 800, false, media, preserveCellDensity: true);
                Assert.Equal(cell, sparse.SelectMany(chunk => chunk.Placements).Min(p => Math.Min(p.Width, p.Height)));
            }
        }
    }

    sealed class Scope : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "pv-capture-groups-" + Guid.NewGuid().ToString("N"));
        public string Database => Path.Combine(Root, "organization.sqlite");
        public CaptureGroupStore Store => new(Database);
        public Scope() => Directory.CreateDirectory(Root);
        public string File(string name)
        {
            var path = Path.Combine(Root, name);
            System.IO.File.WriteAllText(path, "capture");
            return path;
        }
        public void Dispose()
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(Root, true);
        }
    }
}
