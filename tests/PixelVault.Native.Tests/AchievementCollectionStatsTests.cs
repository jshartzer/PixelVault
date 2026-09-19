using System.Windows;
using System.Windows.Controls;
using PixelVaultNative;
using Xunit;
using static PixelVaultNative.Tests.AchievementCollectionAccessTests;

namespace PixelVaultNative.Tests;

public sealed class AchievementCollectionStatsTests
{
    static readonly DateTime Day = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
    static readonly long Now = Day.AddDays(6).Ticks;

    [Fact]
    public void Stats_UsesOnlyConfirmedEarnedProgressAndValidProviderDates()
    {
        var a = Earned("A", 1, 12); var b = Earned("B", 2, 2); var c = Earned("C", 2, null);
        var noDate = Earned("No date", 3, 8); noDate.UnlockUtcTicks = 0;
        var locked = Earned("Locked", 4, 0.5); locked.Unlocked = false;
        var unknown = Earned("Unknown", 5, 0.1); unknown.ProgressKnown = false;
        var stats = AchievementCollectionStats.Build(new[] { a, b, c, noDate, locked, unknown }, Now);
        Assert.Equal(6, stats.Total); Assert.Equal(4, stats.Earned); Assert.Equal(1, stats.Remaining); Assert.Equal(1, stats.Unknown);
        Assert.Null(stats.Percent);
        Assert.Same(b, stats.Latest); Assert.Same(b, stats.Rarest);
        Assert.Equal(3, stats.DatedEarned); Assert.Equal(3, stats.EarnedWithRarity);
        Assert.Collection(stats.History,
            point => { Assert.Equal(Day.AddDays(1), point.DayUtc); Assert.Equal(1, point.Earned); },
            point => { Assert.Equal(Day.AddDays(2), point.DayUtc); Assert.Equal(3, point.Earned); });
    }

    [Fact]
    public void EmptyCompleteAndMissingHistory_DoNotInventData()
    {
        var empty = AchievementCollectionStats.Build(null!, Now);
        Assert.Null(empty.Percent); Assert.Null(empty.Latest); Assert.Null(empty.Rarest); Assert.Empty(empty.History);
        var valid = Earned("Valid", 1, 0);
        var future = Earned("Future", 8, double.NaN);
        var invalid = Earned("Invalid", 1, double.PositiveInfinity); invalid.UnlockUtcTicks = long.MaxValue;
        var stats = AchievementCollectionStats.Build(new[] { valid, future, invalid }, Now);
        Assert.Equal(100, stats.Percent); Assert.Equal(3, stats.Earned); Assert.Equal(1, stats.DatedEarned);
        Assert.Same(valid, stats.Latest); Assert.Same(valid, stats.Rarest);
        Assert.Single(stats.History);
    }

    [Theory]
    [InlineData(1, "BAEDC")]
    [InlineData(2, "AEBDC")]
    [InlineData(3, "BEADC")]
    [InlineData(4, "ABEDC")]
    [InlineData(0, "ADBEC")]
    public void Sort_DirectionMissingValuesAndTiesArePredictable(int order, string expected)
    {
        var a = Earned("A", 1, 25); var b = Earned("B", 2, 1); var e = Earned("E", 2, 25);
        var d = Earned("D", 3, null); d.UnlockUtcTicks = 0;
        var c = Earned("C", 4, -1); c.ProgressKnown = false;
        var rows = new[] { a, d, b, e, c };
        var sorted = AchievementCollectionStats.Sort(rows, (AchievementCollectionSort)order, Now);
        Assert.Equal(expected, string.Concat(sorted.Select(row => row.Title)));
        Assert.Equal("ADBEC", string.Concat(rows.Select(row => row.Title)));
    }

    [Fact]
    public void SteamProgress_MissingOrInvalidStatusIsUnknownAndRarityIsValidated()
    {
        Assert.False(GameAchievementsFetchService.ResolveSteamProgress(null!, "A").Known);
        var progress = new Dictionary<string, (int, int)> { ["A"] = (1, 1700000000), ["B"] = (0, 1700000000), ["Bad"] = (-1, 0) };
        Assert.True(GameAchievementsFetchService.ResolveSteamProgress(progress, "A").Unlocked);
        Assert.Equal(0, GameAchievementsFetchService.ResolveSteamProgress(progress, "B").UnlockUtcTicks);
        Assert.False(GameAchievementsFetchService.ResolveSteamProgress(progress, "missing").Known);
        Assert.False(GameAchievementsFetchService.ResolveSteamProgress(progress, "Bad").Known);
        var rarity = new Dictionary<string, double> { ["A"] = 0, ["B"] = 100, ["Bad"] = 100.01, ["Infinite"] = double.PositiveInfinity };
        Assert.Equal(0, GameAchievementsFetchService.ResolveSteamGlobalPercent(rarity, "A"));
        Assert.Equal(100, GameAchievementsFetchService.ResolveSteamGlobalPercent(rarity, "B"));
        Assert.Null(GameAchievementsFetchService.ResolveSteamGlobalPercent(rarity, "Bad"));
        Assert.Null(GameAchievementsFetchService.ResolveSteamGlobalPercent(rarity, "Infinite"));
    }

    [Fact]
    public void Refresh_ReplacesStatisticsAndPreservesSortAndFullDatasetSummary()
    {
        OnSta(() =>
        {
            var a = Earned("A", 1, 25); var b = Earned("B", 2, 1); var locked = AchievementPinTests.Row("C");
            var queue = new Queue<GameAchievementsFetchService.FetchResult>(new[] { Result(a, b, locked), Result(b, locked) });
            var window = CreateWindow(() => Task.FromResult(queue.Dequeue()));
            try
            {
                Field<Button>(window, "_refresh").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Field<ComboBox>(window, "_sort").SelectedIndex = 1;
                Field<CheckBox>(window, "_remainingOnly").IsChecked = true;
                Field<CheckBox>(window, "_remainingOnly").RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
                Field<Expander>(window, "_progressDetails").IsExpanded = true;
                Assert.Contains("2 of 3", Field<TextBlock>(window, "_earnedSummary").Text);
                Assert.Equal(2, Field<AchievementCollectionStats>(window, "_stats").Earned);
                Field<Button>(window, "_listView").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Field<Button>(window, "_refresh").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(1, Field<ComboBox>(window, "_sort").SelectedIndex);
                Assert.True(Field<Expander>(window, "_progressDetails").IsExpanded);
                Assert.Contains("1 of 2", Field<TextBlock>(window, "_earnedSummary").Text);
                Assert.Equal(1, Field<AchievementCollectionStats>(window, "_stats").Earned);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void PlayerProgressFailure_KeepsLastKnownStatisticsAndTimestamp()
    {
        OnSta(() =>
        {
            var queue = new Queue<GameAchievementsFetchService.FetchResult>(new[]
            {
                Result(Earned("A", 1, 3)),
                new GameAchievementsFetchService.FetchResult { SourceLabel = "Steam", Rows = new() { Row("A", false, false) }, ProgressErrorMessage = "Player progress unavailable" }
            });
            var window = CreateWindow(() => Task.FromResult(queue.Dequeue()));
            try
            {
                Field<Button>(window, "_refresh").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var timestamp = Field<TextBlock>(window, "_updated").ToolTip;
                Field<Button>(window, "_refresh").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Same(timestamp, Field<TextBlock>(window, "_updated").ToolTip);
                Assert.Equal(1, Field<AchievementCollectionStats>(window, "_stats").Earned);
                Assert.Equal(Visibility.Visible, Field<TextBlock>(window, "_error").Visibility);
            }
            finally { window.Close(); }
        });
    }

    static GameAchievementsFetchService.AchievementRow Earned(string title, int days, double? rarity) =>
        new() { Title = title, ProgressKnown = true, Unlocked = true, UnlockUtcTicks = Day.AddDays(days).Ticks, GlobalUnlockPercent = rarity };
}
