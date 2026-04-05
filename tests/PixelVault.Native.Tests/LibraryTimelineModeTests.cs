using System;
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class LibraryTimelineModeTests
{
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
