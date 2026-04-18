using System;
using System.IO;
using Xunit;

namespace PixelVaultNative.Tests;

/// <summary>
/// PV-PLN-UI-001 Step 10: guard rails for the troubleshooting-log path redaction so the
/// "redact paths" user setting keeps reshaping IO exception text, view keys, and quoted
/// paths after future refactors. Each test spins a fresh <see cref="TroubleshootingLog"/>
/// pointed at a throw-away logs dir so file IO never leaks into CI.
/// </summary>
public sealed class TroubleshootingLogRedactionTests : IDisposable
{
    readonly string _logsRoot;

    public TroubleshootingLogRedactionTests()
    {
        _logsRoot = Path.Combine(Path.GetTempPath(), "pv_tslog_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_logsRoot);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_logsRoot)) Directory.Delete(_logsRoot, recursive: true); } catch { }
    }

    TroubleshootingLog NewLog(bool redact)
    {
        return new TroubleshootingLog(new TroubleshootingLogDependencies
        {
            LogsRoot = _logsRoot,
            IsTroubleshootingLoggingEnabled = () => true,
            RedactPathsEnabled = () => redact,
            DiagnosticsSessionId = "testsess",
        });
    }

    [Fact]
    public void FormatPath_RedactsToLastSegment_WhenEnabled()
    {
        var log = NewLog(redact: true);
        Assert.Equal(".../Fallout 4", log.FormatPath(@"C:\Users\me\Captures\Fallout 4"));
    }

    [Fact]
    public void FormatPath_ReturnsOriginal_WhenRedactionOff()
    {
        var log = NewLog(redact: false);
        Assert.Equal(@"C:\Users\me\Captures\Fallout 4", log.FormatPath(@"C:\Users\me\Captures\Fallout 4"));
    }

    [Fact]
    public void FormatPath_TrailingSeparator_StillKeepsLastSegment()
    {
        var log = NewLog(redact: true);
        Assert.Equal(".../Fallout 4", log.FormatPath(@"C:\Users\me\Captures\Fallout 4\"));
    }

    [Fact]
    public void RedactEmbeddedPaths_QuotedDriveLetterPath_IsShortened()
    {
        var log = NewLog(redact: true);
        var msg = "Could not access 'C:\\Users\\me\\secret\\cover.png' for read.";
        var redacted = log.RedactEmbeddedPaths(msg);
        Assert.DoesNotContain(@"C:\Users\me\secret", redacted);
        Assert.Contains(".../cover.png", redacted);
    }

    [Fact]
    public void RedactEmbeddedPaths_DoubleQuotedDrivePath_IsShortened()
    {
        var log = NewLog(redact: true);
        var msg = "File \"C:\\Users\\me\\library\\game.txt\" locked.";
        var redacted = log.RedactEmbeddedPaths(msg);
        Assert.DoesNotContain(@"C:\Users\me\library", redacted);
        Assert.Contains(".../game.txt", redacted);
    }

    [Fact]
    public void RedactEmbeddedPaths_Win32ExtendedPath_IsShortened()
    {
        var log = NewLog(redact: true);
        var msg = @"IO failure at \\?\C:\Users\me\PixelVaultData\cache\index.sqlite";
        var redacted = log.RedactEmbeddedPaths(msg);
        Assert.DoesNotContain(@"\\?\C:\Users\me", redacted);
        Assert.Contains(".../index.sqlite", redacted);
    }

    [Fact]
    public void RedactEmbeddedPaths_UncPath_IsShortened()
    {
        var log = NewLog(redact: true);
        var msg = @"access denied at \\server\share\captures\shot.png";
        var redacted = log.RedactEmbeddedPaths(msg);
        Assert.DoesNotContain(@"\\server\share\captures", redacted);
        Assert.Contains(".../shot.png", redacted);
    }

    [Fact]
    public void RedactEmbeddedPaths_KeyValueSpacedPath_IsShortened()
    {
        var log = NewLog(redact: true);
        var msg = @"folder=C:\Users\me\Game Captures\Fallout 4;extra=42";
        var redacted = log.RedactEmbeddedPaths(msg);
        Assert.DoesNotContain("Game Captures", redacted);
        Assert.Contains(".../Fallout 4", redacted);
        Assert.Contains("extra=42", redacted); // preserved
    }

    [Fact]
    public void RedactEmbeddedPaths_StackFrameLineSuffix_IsNotCounterpartOfPath()
    {
        var log = NewLog(redact: true);
        // "at PixelVaultNative.MainWindow.Log() in C:\Codex\src\Foo.cs:line 42" — after redaction
        // the path should collapse but ":line 42" must not survive as a path continuation.
        var msg = @"at PixelVaultNative.MainWindow.Log() in C:\Codex\src\Foo.cs:line 42";
        var redacted = log.RedactEmbeddedPaths(msg);
        Assert.DoesNotContain(@"C:\Codex\src", redacted);
        Assert.Contains(".../Foo.cs", redacted);
    }

    [Fact]
    public void RedactEmbeddedPaths_OffWhenDisabled()
    {
        var log = NewLog(redact: false);
        var msg = @"Could not access 'C:\Users\me\secret\cover.png' for read.";
        Assert.Equal(msg, log.RedactEmbeddedPaths(msg));
    }

    [Fact]
    public void FormatViewKey_RedactsOnlyPathLikeSegments()
    {
        var log = NewLog(redact: true);
        var key = @"console|Fallout 4|C:\Users\me\Captures\Fallout 4";
        var redacted = log.FormatViewKey(key);
        // Only the path segment mutates; the "console" + title segments stay.
        Assert.Equal(@"console|Fallout 4|.../Fallout 4", redacted);
    }

    [Fact]
    public void FormatViewKey_NoPathSegments_IsUnchanged()
    {
        var log = NewLog(redact: true);
        var key = "all|Fallout 4|tag-shooter";
        Assert.Equal(key, log.FormatViewKey(key));
    }

    [Fact]
    public void SegmentLooksLikePath_CatchesUncAndDriveLetterForms()
    {
        Assert.True(TroubleshootingLog.SegmentLooksLikePath(@"C:\Users\me"));
        Assert.True(TroubleshootingLog.SegmentLooksLikePath(@"/etc/passwd"));
        Assert.True(TroubleshootingLog.SegmentLooksLikePath(@"\\server\share\x"));
        Assert.False(TroubleshootingLog.SegmentLooksLikePath("Fallout 4"));
        Assert.False(TroubleshootingLog.SegmentLooksLikePath(""));
        Assert.False(TroubleshootingLog.SegmentLooksLikePath(null));
    }

    [Fact]
    public void FormatException_TruncatesAfterMax()
    {
        var huge = new Exception(new string('x', 40_000));
        var formatted = TroubleshootingLog.FormatException(huge);
        Assert.Contains("... (truncated)", formatted);
        Assert.True(formatted.Length <= 32768 + "... (truncated)".Length + 64);
    }

    [Fact]
    public void FormatException_NullReturnsEmpty()
    {
        Assert.Equal(string.Empty, TroubleshootingLog.FormatException(null));
    }

    [Fact]
    public void LogTroubleshooting_WritesRedactedLineToTroubleshootingFile()
    {
        var log = NewLog(redact: true);
        log.LogTroubleshooting("TestArea", @"failed 'C:\Users\me\secret\cover.png'");

        var path = Path.Combine(_logsRoot, "PixelVault-troubleshooting.log");
        Assert.True(File.Exists(path), "troubleshooting log file was not created");
        var content = File.ReadAllText(path);
        Assert.Contains("DIAG", content);
        Assert.Contains("S=testsess", content);
        Assert.Contains("TestArea", content);
        Assert.Contains(".../cover.png", content);
        Assert.DoesNotContain(@"C:\Users\me\secret", content);
    }

    [Fact]
    public void LogTroubleshooting_NoopWhenDisabled()
    {
        var log = new TroubleshootingLog(new TroubleshootingLogDependencies
        {
            LogsRoot = _logsRoot,
            IsTroubleshootingLoggingEnabled = () => false,
            RedactPathsEnabled = () => true,
            DiagnosticsSessionId = "testsess",
        });
        log.LogTroubleshooting("TestArea", "hello");
        var path = Path.Combine(_logsRoot, "PixelVault-troubleshooting.log");
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void AppendMainLine_ReturnsFormattedTimestampedLine()
    {
        var log = NewLog(redact: false);
        var line = log.AppendMainLine("hello world");

        Assert.StartsWith("[", line);
        Assert.Contains("] hello world", line);

        var mainPath = Path.Combine(_logsRoot, "PixelVault-native.log");
        Assert.True(File.Exists(mainPath));
        var content = File.ReadAllText(mainPath);
        Assert.Contains("hello world", content);
    }
}
