using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using PixelVaultNative;
using Xunit;
using static PixelVaultNative.Tests.AchievementCollectionAccessTests;

namespace PixelVaultNative.Tests;

public sealed class AchievementUnlockFeedbackTests
{
    static GameAchievementsFetchService.AchievementRow Entry(string id, bool known = true, bool earned = false) =>
        new() { Provider = "steam", ProviderGameId = "123", ProviderAchievementId = id, Title = id, ProgressKnown = known, Unlocked = earned };

    [Theory]
    [InlineData(true, false, true, true, 1)]
    [InlineData(true, true, true, true, 0)]
    [InlineData(false, false, true, true, 0)]
    [InlineData(true, false, false, true, 0)]
    [InlineData(true, false, true, false, 0)]
    public void OnlyKnownLockedToEarnedProducesFeedback(bool oldKnown, bool oldEarned, bool newKnown, bool newEarned, int expected)
    {
        var tracker = new AchievementUnlockTracker();
        Assert.Empty(tracker.Observe(Result(Entry("A", oldKnown, oldEarned)), "account"));
        Assert.Equal(expected, tracker.Observe(Result(Entry("A", newKnown, newEarned)), "account").Count);
    }

    [Fact]
    public void SnapshotsDoNotAliasMutableProviderRows_AndRepeatDoesNotCelebrate()
    {
        var tracker = new AchievementUnlockTracker();
        var row = Entry("A");
        tracker.Observe(Result(row), "one");
        row.Unlocked = true;
        Assert.Single(tracker.Observe(Result(row), "one"));
        Assert.Empty(tracker.Observe(Result(row), "one"));
    }

    [Fact]
    public void FailedRefreshPreservesBaseline_ButMissingAndUnknownBreakContinuity()
    {
        foreach (var failure in new[] { new GameAchievementsFetchService.FetchResult { ErrorMessage = "offline" },
            new GameAchievementsFetchService.FetchResult { ProgressErrorMessage = "private" } })
        {
            var tracker = new AchievementUnlockTracker();
            tracker.Observe(Result(Entry("A")), "one");
            Assert.Empty(tracker.Observe(failure, "one"));
            Assert.Single(tracker.Observe(Result(Entry("A", earned: true)), "one"));
            tracker.Observe(Result(Entry("B")), "one");
            tracker.Observe(Result(), "one");
            Assert.Empty(tracker.Observe(Result(Entry("B", earned: true)), "one"));
            tracker.Observe(Result(Entry("C")), "one");
            tracker.Observe(Result(Entry("C", known: false)), "one");
            Assert.Empty(tracker.Observe(Result(Entry("C", earned: true)), "one"));
        }
    }

    [Fact]
    public void AccountProviderAndGameScopesDoNotMix()
    {
        var tracker = new AchievementUnlockTracker();
        tracker.Observe(Result(Entry("A")), "one");
        Assert.Empty(tracker.Observe(Result(Entry("A", earned: true)), "two"));
        tracker.Observe(Result(Entry("A")), "two");
        var other = Entry("A", earned: true); other.ProviderGameId = "456";
        Assert.Empty(tracker.Observe(Result(other), "two"));
        tracker.Observe(Result(Entry("A")), "two");
        other.ProviderGameId = "123"; other.Provider = "retroachievements";
        Assert.Empty(tracker.Observe(Result(other), "two"));
    }

    [Fact]
    public void ConflictingDuplicateOrUnidentifiedRowsCannotProveUnlocks()
    {
        var tracker = new AchievementUnlockTracker();
        tracker.Observe(Result(Entry("A"), Entry("B")), "one");
        Assert.Empty(tracker.Observe(Result(Entry("A"), Entry("A", earned: true), Entry("B", known: false), Entry("B", earned: true)), "one"));
        Assert.Empty(tracker.Observe(Result(Entry("A", earned: true), Entry("B", earned: true)), "one"));
        Assert.Empty(tracker.Observe(Result(Entry("", earned: true)), "one"));
    }

    [Fact]
    public void GroupProgressDistinguishesUnknownFromZeroAndUsesFullTotals()
    {
        OnSta(() =>
        {
            var group = Assert.Single(AchievementsInfoWindow.BuildAchievementGroups(new[] { Entry("A"), Entry("B", earned: true) }));
            var bar = (Grid)AchievementCollectionUi.GroupProgress(group);
            Assert.Equal(Visibility.Visible, bar.Visibility);
            Assert.Equal(.5, bar.ColumnDefinitions[0].Width.Value);
            group.Rows[1].Unlocked = false;
            Assert.Equal(Visibility.Visible, AchievementCollectionUi.GroupProgress(group).Visibility);
            group.Rows[1].ProgressKnown = false;
            Assert.Equal(Visibility.Collapsed, AchievementCollectionUi.GroupProgress(group).Visibility);
        });
    }

    [Fact]
    public void RefreshNoticeSurvivesFilteringWithoutMovingFocus_AndFailureClearsIt()
    {
        OnSta(() =>
        {
            var queue = new Queue<GameAchievementsFetchService.FetchResult>(new[] { Result(Entry("A")),
                Result(Entry("A", earned: true)), new GameAchievementsFetchService.FetchResult { ErrorMessage = "offline" } });
            var window = CreateWindow(() => Task.FromResult(queue.Dequeue()));
            try
            {
                var refresh = Field<Button>(window, "_refresh");
                refresh.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(Visibility.Collapsed, Field<TextBlock>(window, "_unlockNotice").Visibility);
                Field<CheckBox>(window, "_remainingOnly").IsChecked = true;
                refresh.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal("1 achievement newly earned", Field<TextBlock>(window, "_unlockNotice").Text);
                Assert.Equal(Visibility.Visible, Field<TextBlock>(window, "_unlockNotice").Visibility);
                Field<Button>(window, "_listView").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(Visibility.Visible, Field<TextBlock>(window, "_unlockNotice").Visibility);
                refresh.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(Visibility.Collapsed, Field<TextBlock>(window, "_unlockNotice").Visibility);
                Assert.Null(Field<object?>(window, "_feedbackTimer"));
                Assert.Contains("1 of 1", Field<TextBlock>(window, "_earnedSummary").Text);
            }
            finally { window.Close(); }
        });
    }
}
