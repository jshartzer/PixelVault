using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace PixelVaultNative.Tests;

/// <summary>
/// PV-PLN-UI-001 Step 12: guard rails for <see cref="TextAndPathHelpers"/> so the pure text /
/// path / media-type helpers stay byte-identical after future refactors. These used to live as
/// <c>static</c> / pure-instance methods on <c>MainWindow</c> and are consumed by StartupInitialization,
/// IndexServicesWiring, LibraryScanner, GameIndexCore, ImportService, MetadataHelpers,
/// LibraryWorkspaceContext, and ~a dozen other partials — so any silent drift here moves UI text
/// or disk layout across the whole app.
/// </summary>
public sealed class TextAndPathHelpersTests : IDisposable
{
    readonly string _root;

    public TextAndPathHelpersTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "pv_tph_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
        catch
        {
        }
    }

    [Theory]
    [InlineData("42", 42)]
    [InlineData("-7", -7)]
    [InlineData("0", 0)]
    [InlineData("abc", 0)]
    [InlineData("", 0)]
    [InlineData(null, 0)]
    public void ParseInt_ReturnsZeroOnInvalid(string? input, int expected)
    {
        Assert.Equal(expected, TextAndPathHelpers.ParseInt(input!));
    }

    [Theory]
    [InlineData("9000000000", 9000000000L)]
    [InlineData("abc", 0L)]
    public void ParseLong_ReturnsZeroOnInvalid(string input, long expected)
    {
        Assert.Equal(expected, TextAndPathHelpers.ParseLong(input));
    }

    [Fact]
    public void FormatFriendlyTimestamp_PadsAndUsesTwelveHourClock()
    {
        var midnight = new DateTime(2026, 4, 18, 0, 5, 9);
        Assert.Equal("2026-04-18 12:05:09 AM", TextAndPathHelpers.FormatFriendlyTimestamp(midnight));

        var noon = new DateTime(2026, 4, 18, 12, 0, 0);
        Assert.Equal("2026-04-18 12:00:00 PM", TextAndPathHelpers.FormatFriendlyTimestamp(noon));

        var evening = new DateTime(2026, 4, 18, 23, 15, 30);
        Assert.Equal("2026-04-18 11:15:30 PM", TextAndPathHelpers.FormatFriendlyTimestamp(evening));
    }

    [Fact]
    public void Sanitize_ReplacesInvalidFileNameChars()
    {
        var input = "Half-Life 2:  Episode\t One?";
        var result = TextAndPathHelpers.Sanitize(input);
        Assert.DoesNotContain("?", result);
        Assert.DoesNotContain(":", result);
        Assert.DoesNotContain("  ", result);
        Assert.Equal(result, result.Trim());
    }

    [Fact]
    public void CleanComment_CollapsesNewlinesAndWhitespace()
    {
        Assert.Equal("line one line two", TextAndPathHelpers.CleanComment("line one\r\nline two"));
        Assert.Equal("a b c", TextAndPathHelpers.CleanComment("a   b\tc"));
        Assert.Equal(string.Empty, TextAndPathHelpers.CleanComment(""));
        Assert.Equal(string.Empty, TextAndPathHelpers.CleanComment(null!));
    }

    [Fact]
    public void CleanTag_TrimsAndCollapsesInternalWhitespace()
    {
        Assert.Equal("Xbox PC", TextAndPathHelpers.CleanTag("  Xbox   PC  "));
        Assert.Equal(string.Empty, TextAndPathHelpers.CleanTag(""));
        Assert.Equal(string.Empty, TextAndPathHelpers.CleanTag(null!));
    }

    [Fact]
    public void ParseTagText_SplitsAndDedupesCaseInsensitively()
    {
        var tags = TextAndPathHelpers.ParseTagText("Steam, Emulation;steam\nXbox PC\rSTEAM");
        Assert.Equal(new[] { "Steam", "Emulation", "Xbox PC" }, tags);
    }

    [Fact]
    public void ParseTagText_EmptyInputReturnsEmptyArray()
    {
        Assert.Empty(TextAndPathHelpers.ParseTagText(""));
        Assert.Empty(TextAndPathHelpers.ParseTagText(null!));
    }

    [Theory]
    [InlineData("foo", "foo", true)]
    [InlineData("  foo  ", "foo", true)]
    [InlineData("Foo", "foo", false)]
    [InlineData(null, "", true)]
    [InlineData(null, null, true)]
    public void SameManualText_TrimsAndComparesOrdinal(string? left, string? right, bool expected)
    {
        Assert.Equal(expected, TextAndPathHelpers.SameManualText(left, right));
    }

    [Fact]
    public void Unique_ReturnsInputWhenMissing()
    {
        var target = Path.Combine(_root, "novel.txt");
        Assert.Equal(target, TextAndPathHelpers.Unique(target));
    }

    [Fact]
    public void Unique_AddsParentheticalCounterUntilAvailable()
    {
        var first = Path.Combine(_root, "pic.jpg");
        File.WriteAllText(first, "x");
        var next = TextAndPathHelpers.Unique(first);
        Assert.Equal(Path.Combine(_root, "pic (2).jpg"), next);

        File.WriteAllText(next, "x");
        var after = TextAndPathHelpers.Unique(first);
        Assert.Equal(Path.Combine(_root, "pic (3).jpg"), after);
    }

    [Fact]
    public void EnsureDir_ThrowsWhenMissingAndNoopWhenPresent()
    {
        Assert.Throws<InvalidOperationException>(() => TextAndPathHelpers.EnsureDir(Path.Combine(_root, "does-not-exist"), "Library"));
        Assert.Throws<InvalidOperationException>(() => TextAndPathHelpers.EnsureDir("", "Library"));
        TextAndPathHelpers.EnsureDir(_root, "Library");
    }

    [Theory]
    [InlineData("foo.png", true)]
    [InlineData("foo.PNG", true)]
    [InlineData("foo.jpg", true)]
    [InlineData("foo.jpeg", true)]
    [InlineData("foo.webp", true)]
    [InlineData("foo.jxr", true)]
    [InlineData("foo.mp4", false)]
    [InlineData("foo.txt", false)]
    public void IsImage_CoversTheSupportedPhotoExtensions(string name, bool expected)
    {
        Assert.Equal(expected, TextAndPathHelpers.IsImage(name));
    }

    [Theory]
    [InlineData("foo.png", true)]
    [InlineData("foo.jpg", true)]
    [InlineData("foo.webp", false)]
    [InlineData("foo.jxr", false)]
    public void IsPngOrJpeg_IsSteamSavedCoverGate(string name, bool expected)
    {
        Assert.Equal(expected, TextAndPathHelpers.IsPngOrJpeg(name));
    }

    [Theory]
    [InlineData("foo.mp4", true)]
    [InlineData("foo.MKV", true)]
    [InlineData("foo.avi", true)]
    [InlineData("foo.mov", true)]
    [InlineData("foo.wmv", true)]
    [InlineData("foo.webm", true)]
    [InlineData("foo.png", false)]
    public void IsVideo_CoversTheSupportedClipExtensions(string name, bool expected)
    {
        Assert.Equal(expected, TextAndPathHelpers.IsVideo(name));
    }

    [Theory]
    [InlineData("foo.jpg", true)]
    [InlineData("foo.mp4", true)]
    [InlineData("foo.txt", false)]
    public void IsMedia_IsPhotoOrVideo(string name, bool expected)
    {
        Assert.Equal(expected, TextAndPathHelpers.IsMedia(name));
    }

    [Theory]
    [InlineData("simple", "simple")]
    [InlineData("has\"quote\"", "has\"quote\"")]
    [InlineData("has space", "\"has space\"")]
    [InlineData("has space and \"quote\"", "\"has space and \\\"quote\\\"\"")]
    public void Quote_WrapsOnlyWhenSpacesPresent(string input, string expected)
    {
        Assert.Equal(expected, TextAndPathHelpers.Quote(input));
    }

    [Fact]
    public void NormalizeTitle_StripsPunctuationAndLowercases()
    {
        // Hyphen + colon are explicitly replaced with a space, then non-letter/digit runs collapse.
        Assert.Equal("half life 2 episode one", TextAndPathHelpers.NormalizeTitle("Half-Life 2: Episode One"));
        Assert.Equal("forza horizon 5", TextAndPathHelpers.NormalizeTitle("Forza_Horizon_5"));
        Assert.Equal(string.Empty, TextAndPathHelpers.NormalizeTitle(null));
        Assert.Equal(string.Empty, TextAndPathHelpers.NormalizeTitle(""));
    }

    [Fact]
    public void NormalizeTitle_ScrubsMojibakeTrademarkSymbols()
    {
        // Same Windows-1252 mojibake sequences the real Steam / store titles have been observed with.
        var input = "Halo\u00E2\u201E\u00A2: The Master Chief Collection \u00C2\u00AE";
        var result = TextAndPathHelpers.NormalizeTitle(input);
        Assert.Equal("halo the master chief collection", result);
    }

    [Fact]
    public void NormalizeTitle_DecodesHtmlEntitiesAndStripsResultingPunctuation()
    {
        // `&#39;` decodes to an apostrophe, which is non-letter/digit and therefore collapses to
        // a space alongside the surrounding characters.
        Assert.Equal("sid meier s civilization", TextAndPathHelpers.NormalizeTitle("Sid Meier&#39;s Civilization"));
    }

    [Fact]
    public void SafeCacheName_ReplacesSpacesWithUnderscores()
    {
        Assert.Equal("half_life_2", TextAndPathHelpers.SafeCacheName("Half-Life 2"));
        Assert.Equal("halo", TextAndPathHelpers.SafeCacheName("Halo"));
    }

    [Fact]
    public void StripTags_RemovesHtmlAngleBrackets()
    {
        Assert.Equal("bold text here", TextAndPathHelpers.StripTags("<b>bold</b> text <em>here</em>"));
        Assert.Equal(string.Empty, TextAndPathHelpers.StripTags(null));
    }

    [Fact]
    public void GetLibraryDate_PrefersParsedCaptureTimeForNonXbox()
    {
        var file = Path.Combine(_root, "game.png");
        File.WriteAllText(file, "x");
        File.SetCreationTime(file, new DateTime(2020, 1, 1, 8, 0, 0));
        File.SetLastWriteTime(file, new DateTime(2020, 1, 2, 8, 0, 0));

        var parsed = new FilenameParseResult
        {
            CaptureTime = new DateTime(2025, 6, 1, 10, 30, 0),
            PlatformTags = new[] { "Steam" }
        };

        Assert.Equal(new DateTime(2025, 6, 1, 10, 30, 0), TextAndPathHelpers.GetLibraryDate(file, parsed));
    }

    [Fact]
    public void GetLibraryDate_FallsBackToFileTimesForXboxEvenWhenCaptureTimePresent()
    {
        var file = Path.Combine(_root, "xbox.png");
        File.WriteAllText(file, "x");
        var created = new DateTime(2023, 3, 10, 9, 0, 0);
        var modified = new DateTime(2023, 3, 11, 9, 0, 0);
        File.SetCreationTime(file, created);
        File.SetLastWriteTime(file, modified);

        var parsed = new FilenameParseResult
        {
            CaptureTime = new DateTime(2026, 1, 1, 0, 0, 0),
            PlatformTags = new[] { "Xbox" }
        };

        // Earlier of created / modified.
        Assert.Equal(created, TextAndPathHelpers.GetLibraryDate(file, parsed));
    }

    [Fact]
    public void GetLibraryDate_FallsBackToFileTimesWhenCaptureTimeMissing()
    {
        var file = Path.Combine(_root, "no-capture.png");
        File.WriteAllText(file, "x");
        var created = new DateTime(2022, 5, 1, 12, 0, 0);
        var modified = new DateTime(2022, 5, 5, 12, 0, 0);
        File.SetCreationTime(file, created);
        File.SetLastWriteTime(file, modified);

        var parsed = new FilenameParseResult
        {
            CaptureTime = null,
            PlatformTags = new[] { "Steam" }
        };

        Assert.Equal(created, TextAndPathHelpers.GetLibraryDate(file, parsed));
    }

    [Fact]
    public void GetLibraryDate_HandlesNullPlatformTags()
    {
        var file = Path.Combine(_root, "loose.png");
        File.WriteAllText(file, "x");

        var parsed = new FilenameParseResult
        {
            CaptureTime = new DateTime(2024, 2, 2, 14, 0, 0),
            PlatformTags = null!
        };

        Assert.Equal(new DateTime(2024, 2, 2, 14, 0, 0), TextAndPathHelpers.GetLibraryDate(file, parsed));
    }
}
