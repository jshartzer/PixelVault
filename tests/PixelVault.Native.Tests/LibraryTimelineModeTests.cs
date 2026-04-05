using System;
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class LibraryTimelineModeTests
{
    [Fact]
    public void BuildLibraryTimelinePresetDateRange_ThirtyDays_UsesTrailingInclusiveWindow()
    {
        MainWindow.BuildLibraryTimelinePresetDateRange("30d", new DateTime(2026, 4, 5, 14, 30, 0), out var startDate, out var endDate);

        Assert.Equal(new DateTime(2026, 3, 7), startDate);
        Assert.Equal(new DateTime(2026, 4, 5), endDate);
    }

    [Fact]
    public void DetectLibraryTimelinePresetKey_ReturnsCustomForManualRange()
    {
        var preset = MainWindow.DetectLibraryTimelinePresetKey(
            new DateTime(2026, 2, 10),
            new DateTime(2026, 2, 18),
            new DateTime(2026, 4, 5));

        Assert.Equal("custom", preset);
    }

    [Fact]
    public void LibraryTimelineRangeContainsCapture_NormalizesReversedBounds()
    {
        var included = MainWindow.LibraryTimelineRangeContainsCapture(
            new DateTime(2026, 4, 1, 18, 45, 0),
            new DateTime(2026, 4, 5),
            new DateTime(2026, 3, 29));

        Assert.True(included);
    }

    [Fact]
    public void BuildLibraryTimelineSummaryText_IncludesCountsAndDateRange()
    {
        var summary = MainWindow.BuildLibraryTimelineSummaryText(
            42,
            7,
            3,
            new DateTime(2026, 4, 4, 21, 0, 0),
            new DateTime(2026, 3, 29, 10, 0, 0));

        Assert.Equal("42 photos | 7 games | 3 platforms | March 29, 2026 - April 4, 2026", summary);
    }

    [Fact]
    public void BuildLibraryTimelineSummaryText_UsesSingleDayLabelWhenRangeMatches()
    {
        var summary = MainWindow.BuildLibraryTimelineSummaryText(
            1,
            1,
            1,
            new DateTime(2026, 4, 4, 21, 34, 0),
            new DateTime(2026, 4, 4, 9, 15, 0));

        Assert.Equal("1 photo | 1 game | 1 platform | April 4, 2026", summary);
    }
}
