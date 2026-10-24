using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class AchievementCollectionAccessTests
{
    [Fact]
    public void RemainingFilter_ExcludesEarnedAndUnknownWithoutChangingProviderOrder()
    {
        var rows = new[] { Row("earned", true, true), Row("second", true, false),
            Row("unknown", false, false), Row("first", true, false), Row("unknown-earned", false, true) };
        Assert.Equal(new[] { "second", "first" },
            AchievementsInfoWindow.FilterAchievementRows(rows, true).Select(r => r.Title));
        Assert.Equal(rows, AchievementsInfoWindow.FilterAchievementRows(rows, false));
    }

    [Fact]
    public void RemainingFilter_DoesNotChangeFullGroupTotalsOrPercentages()
    {
        var rows = new[] { Row("earned", true, true), Row("locked", true, false) };
        var visible = AchievementsInfoWindow.FilterAchievementRows(rows, true);
        var group = Assert.Single(AchievementsInfoWindow.BuildAchievementGroups(rows));
        Assert.Single(visible);
        Assert.Equal(2, group.Total);
        Assert.Equal(50, group.Percent);
    }

    [Fact]
    public void EmptyCollections_HaveNoFabricatedCompletion()
    {
        Assert.Empty(AchievementsInfoWindow.FilterAchievementRows(null!, true));
        Assert.Contains("No achievement definitions", AchievementsInfoWindow.BuildRemainingAchievementMessage(null!));
        Assert.Contains("obtained", AchievementsInfoWindow.BuildRemainingAchievementMessage(new[] { Row("done", true, true) }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnknownProgress_IsNeverCalledComplete(bool unknownUnlocked)
    {
        var message = AchievementsInfoWindow.BuildRemainingAchievementMessage(
            new[] { Row("done", true, true), Row("unknown", false, unknownUnlocked) });
        Assert.Contains("Progress unavailable for 1", message);
        Assert.DoesNotContain("obtained", message);
    }

    [Fact]
    public void MixedProgress_ExplainsRowsExcludedByRemainingFilter()
    {
        var message = AchievementsInfoWindow.BuildRemainingAchievementMessage(
            new[] { Row("done", true, true), Row("locked", true, false), Row("unknown", false, false) });
        Assert.Contains("Showing 1 known remaining", message);
        Assert.Contains("Progress unavailable for 1", message);
    }

    [Fact]
    public void MissableFilter_WorksInGameGridAndListAlongsideRemainingOnly()
    {
        OnSta(() =>
        {
            var locked = Row("locked", true, false); locked.IsMissable = true;
            var earned = Row("earned", true, true); earned.IsMissable = true;
            var unmarked = Row("unmarked", true, false);
            var window = CreateWindow(() => Task.FromResult(Result(locked, earned, unmarked)));
            try
            {
                Refresh(window);
                var filter = Field<ComboBox>(window, "_missableFilter");
                filter.SelectedIndex = 1;
                var grid = Field<StackPanel>(window, "_gridContent");
                var cards = grid.Children.OfType<WrapPanel>().SelectMany(p => p.Children.OfType<Border>()).ToList();
                Assert.Equal(2, cards.Count);
                var marked = cards.Single(b => b.Tag == locked);
                ((FrameworkElement)window.Content).Measure(new Size(636, 760));
                ((FrameworkElement)window.Content).Arrange(new Rect(0, 0, 636, 760));
                var label = ((Grid)marked.Child).Children.OfType<StackPanel>().Single().Children.OfType<TextBlock>().Single(t => t.Text == "Missable");
                var position = label.TranslatePoint(new Point(), marked);
                Assert.True(label.ActualHeight > 0);
                Assert.True(position.Y + label.ActualHeight <= marked.ActualHeight);
                var remaining = Field<CheckBox>(window, "_remainingOnly");
                remaining.IsChecked = true;
                remaining.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
                Assert.Single(grid.Children.OfType<WrapPanel>().SelectMany(p => p.Children.OfType<Border>()));
                Field<Button>(window, "_listView").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Same(locked, Assert.Single(Field<ListBox>(window, "_list").Items.OfType<Border>(), b => b.Tag is GameAchievementsFetchService.AchievementRow).Tag);
                Assert.Contains("1 of 3", Field<TextBlock>(window, "_earnedSummary").Text);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void RefreshCommand_PreservesFilterViewGroupsAndFailedUpdateTimestamp()
    {
        OnSta(() =>
        {
            var results = new Queue<GameAchievementsFetchService.FetchResult>(new[]
            {
                Result(Row("one", true, false), Row("two", true, true)),
                Result(Row("one", true, true), Row("two", true, true)),
                new GameAchievementsFetchService.FetchResult { ErrorMessage = "Offline" }
            });
            var window = CreateWindow(() => Task.FromResult(results.Dequeue()));
            try
            {
                Refresh(window);
                var toggle = Field<CheckBox>(window, "_remainingOnly");
                toggle.IsChecked = true;
                toggle.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
                var groups = Field<HashSet<string>>(window, "_collapsedGroups");
                groups.Add("Base Game");
                Field<Button>(window, "_listView").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var binding = Assert.Single(window.InputBindings.OfType<KeyBinding>(), b => b.Key == Key.F5);
                Assert.Same(NavigationCommands.Refresh, binding.Command);
                NavigationCommands.Refresh.Execute(null, window);
                Assert.True(toggle.IsChecked);
                Assert.False(Field<bool>(window, "_showGrid"));
                Assert.Contains("Base Game", groups);
                Assert.Contains("2 of 2", Field<TextBlock>(window, "_earnedSummary").Text);
                Assert.Contains("obtained", Field<TextBlock>(window, "_empty").Text);
                var updated = Field<TextBlock>(window, "_updated");
                var timestamp = updated.ToolTip;
                Refresh(window);
                Assert.Same(timestamp, updated.ToolTip);
                Assert.Contains("2 of 2", Field<TextBlock>(window, "_earnedSummary").Text);
                Assert.Equal(Visibility.Visible, Field<TextBlock>(window, "_error").Visibility);
                Assert.True(Field<Button>(window, "_refresh").IsEnabled);
                Assert.Empty(results);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void NarrowUnknownGroupHeader_KeepsNameReadableWithoutExcessiveHeight()
    {
        OnSta(() =>
        {
            var group = Assert.Single(AchievementsInfoWindow.BuildAchievementGroups(new[] { Row("unknown", false, false) }));
            var header = AchievementsInfoWindow.BuildAchievementGroupHeader(group, false, () => { });
            header.Measure(new Size(280, double.PositiveInfinity));
            header.Arrange(new Rect(0, 0, 280, header.DesiredSize.Height));
            var title = ((Grid)header.Content).Children.OfType<TextBlock>().Single(t => t.Text == "Base Game");
            Assert.True(title.ActualWidth > 80);
            Assert.InRange(header.ActualHeight, 35, 100);
        });
    }

    [Fact]
    public void PendingRefresh_BlocksOverlapAndIgnoresCompletionAfterClose()
    {
        OnSta(() =>
        {
            // No synchronization context: complete the source on this same STA to exercise the continuation.
            var source = new TaskCompletionSource<GameAchievementsFetchService.FetchResult>();
            var calls = 0;
            var window = CreateWindow(() => { calls++; return source.Task; });
            Refresh(window);
            Refresh(window);
            Assert.Equal(1, calls);
            Assert.False(Field<Button>(window, "_refresh").IsEnabled);
            Assert.False(NavigationCommands.Refresh.CanExecute(null, window));
            window.Close();
            source.SetResult(Result(Row("one", true, true)));
            Assert.Null(Field<object?>(window, "_loadedResult"));
            Assert.Equal(Visibility.Collapsed, Field<TextBlock>(window, "_updated").Visibility);
        });
    }

    internal static GameAchievementsFetchService.AchievementRow Row(string title, bool known, bool unlocked) =>
        new() { Title = title, ProgressKnown = known, Unlocked = unlocked };

    internal static GameAchievementsFetchService.FetchResult Result(params GameAchievementsFetchService.AchievementRow[] rows) =>
        new() { SourceLabel = "Steam", GameTitle = "Collection access fixture", Rows = rows.ToList() };

    internal static AchievementsInfoWindow CreateWindow(Func<Task<GameAchievementsFetchService.FetchResult>> fetch, IAchievementGuideService? guideService = null) =>
        (AchievementsInfoWindow)Activator.CreateInstance(typeof(AchievementsInfoWindow),
            BindingFlags.Instance | BindingFlags.NonPublic, null,
            new object?[] { null, "Collection access fixture", guideService, null, "PixelVault.Tests", fetch }, null)!;

    internal static T Field<T>(object window, string name) =>
        (T)window.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;

    static void Refresh(AchievementsInfoWindow window) =>
        Field<Button>(window, "_refresh").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    internal static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ex; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "STA UI fixture timed out.");
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
