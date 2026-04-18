#nullable enable

using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;

namespace PixelVaultNative
{
    /// <summary>
    /// Wiring for <see cref="TroubleshootingLog"/>. <see cref="LogsRoot"/> is captured once;
    /// <see cref="IsTroubleshootingLoggingEnabled"/> and <see cref="RedactPathsEnabled"/> are read
    /// every call so Settings checkbox toggles take effect without re-constructing the service.
    /// </summary>
    internal sealed class TroubleshootingLogDependencies
    {
        public string LogsRoot = default!;
        public Func<bool> IsTroubleshootingLoggingEnabled = default!;
        public Func<bool> RedactPathsEnabled = default!;
        public string DiagnosticsSessionId = default!;
        public long MaxTroubleshootingBytes = 5_000_000L;
    }

    /// <summary>
    /// PV-PLN-UI-001 Step 10: owns the native + troubleshooting log file IO, redaction, and
    /// rotation. Bodies are ported verbatim from the <see cref="MainWindow"/> block so behavior
    /// must stay byte-identical (file shape, redaction regexes, rotation threshold). WPF concerns
    /// (<c>logBox</c>, <see cref="MainWindow.LoadLogView"/>) stay on the shell — <see cref="AppendMainLine"/>
    /// returns the formatted line so the shell can echo into its TextBox without re-timestamping.
    /// </summary>
    internal sealed class TroubleshootingLog
    {
        readonly TroubleshootingLogDependencies _d;
        readonly object _fileSync = new object();

        public TroubleshootingLog(TroubleshootingLogDependencies dependencies)
        {
            _d = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
            if (string.IsNullOrWhiteSpace(_d.LogsRoot)) throw new ArgumentException("LogsRoot is required.", nameof(dependencies));
            if (_d.IsTroubleshootingLoggingEnabled == null) throw new ArgumentException("IsTroubleshootingLoggingEnabled is required.", nameof(dependencies));
            if (_d.RedactPathsEnabled == null) throw new ArgumentException("RedactPathsEnabled is required.", nameof(dependencies));
            if (string.IsNullOrEmpty(_d.DiagnosticsSessionId)) throw new ArgumentException("DiagnosticsSessionId is required.", nameof(dependencies));
        }

        public string MainLogFilePath() => Path.Combine(_d.LogsRoot, "PixelVault-native.log");
        public string TroubleshootingLogFilePath() => Path.Combine(_d.LogsRoot, "PixelVault-troubleshooting.log");

        public string TryReadMainLog()
        {
            var path = MainLogFilePath();
            if (!File.Exists(path)) return string.Empty;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var reader = new StreamReader(stream))
                    {
                        return reader.ReadToEnd();
                    }
                }
                catch (IOException)
                {
                    Thread.Sleep(25 * (attempt + 1));
                }
            }
            return string.Empty;
        }

        /// <summary>Appends a timestamped line to the main log and returns the formatted line
        /// (so the WPF shell can echo the same content into its <c>logBox</c>).</summary>
        public string AppendMainLine(string? message)
        {
            var line = "[" + FormatLogUtcTimestamp() + "] " + (message ?? string.Empty);
            AppendLine(MainLogFilePath(), line);
            return line;
        }

        /// <summary>
        /// Troubleshooting-only log file shape:
        /// <c>[UTC] DIAG | S&lt;session&gt; | T&lt;managedThreadId&gt; | &lt;Area&gt; | &lt;message&gt;</c>.
        /// When path redaction is enabled, <paramref name="message"/> is passed through
        /// <see cref="RedactEmbeddedPaths"/> so IO exceptions cannot bypass folder-path redaction via
        /// <c>ex.Message</c> / stack text.
        /// </summary>
        public void LogTroubleshooting(string? area, string? message)
        {
            if (!_d.IsTroubleshootingLoggingEnabled()) return;
            var safeBody = RedactEmbeddedPaths(message ?? string.Empty);
            var line = "[" + FormatLogUtcTimestamp() + "] "
                + "DIAG"
                + " | S=" + _d.DiagnosticsSessionId
                + " | T=" + Environment.CurrentManagedThreadId
                + " | " + (string.IsNullOrWhiteSpace(area) ? "General" : area!.Trim())
                + " | " + safeBody;
            AppendLine(TroubleshootingLogFilePath(), line);
        }

        // region: redaction ------------------------------------------------------------

        /// <summary>
        /// Strips absolute path-shaped fragments from free text when path redaction is on
        /// (exception messages, stack lines, IO errors).
        /// </summary>
        public string RedactEmbeddedPaths(string? text)
        {
            if (string.IsNullOrEmpty(text) || !_d.RedactPathsEnabled()) return text ?? string.Empty;
            try
            {
                var s = text!;
                // Quoted Win32 extended paths (common in IO exception text): '\\?\C:\…' or '\\?\UNC\…'
                s = Regex.Replace(
                    s,
                    @"'(?:\\{2}\?\\)([^']*)'",
                    m => "'" + RedactBareWindowsPath(m.Groups[1].Value) + "'",
                    RegexOptions.CultureInvariant);
                // Quoted drive-letter paths — regex above stops at spaces; IO messages quote full paths.
                s = Regex.Replace(
                    s,
                    @"'([A-Za-z]:\\[^']*)'",
                    m => "'" + RedactBareWindowsPath(m.Groups[1].Value) + "'",
                    RegexOptions.CultureInvariant);
                // Double-quoted drive paths
                s = Regex.Replace(
                    s,
                    @"""([A-Za-z]:\\[^""]*)""",
                    m => "\"" + RedactBareWindowsPath(m.Groups[1].Value) + "\"",
                    RegexOptions.CultureInvariant);
                // DIAG-style key=value segments often hold spaced paths; stop at ';' or line end (not at first space).
                s = Regex.Replace(
                    s,
                    @"([A-Za-z_][\w]*=)(\\\\[^;|\r\n]+)",
                    m => m.Groups[1].Value + RedactBareWindowsPath(m.Groups[2].Value),
                    RegexOptions.CultureInvariant);
                s = Regex.Replace(
                    s,
                    @"([A-Za-z_][\w]*=)([A-Za-z]:\\[^;|\r\n]+)",
                    m => m.Groups[1].Value + RedactBareWindowsPath(m.Groups[2].Value),
                    RegexOptions.CultureInvariant);
                // Long/Win32 extended: \\?\C:\... or \\?\UNC\...
                s = Regex.Replace(s, @"\\{2}\?\\[^\s|""]+", RedactPathMatch, RegexOptions.CultureInvariant);
                // Standard UNC \\server\share\...
                s = Regex.Replace(s, @"\\{2}(?!\?\\)[^\s|""]+", RedactPathMatch, RegexOptions.CultureInvariant);
                // Drive-letter paths (UTF-16 style); optional forward-slash form — tokens without spaces only
                s = Regex.Replace(s, @"(?<![\w/:])(?:[A-Za-z]:\\[^\s|""]+|[A-Za-z]:/[^\s|""]+)", RedactPathMatch, RegexOptions.CultureInvariant);
                return s;
            }
            catch
            {
                return text!;
            }
        }

        /// <summary>
        /// Turns a Windows file path into <see cref="FormatPath"/> form (e.g. <c>.../LastSegment</c>) when redaction is on.
        /// </summary>
        string RedactBareWindowsPath(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return raw ?? string.Empty;
            try
            {
                var t = raw!.Trim().Trim('"', '\'');
                t = t.Replace('/', Path.DirectorySeparatorChar);
                return FormatPath(t);
            }
            catch
            {
                return "(redacted)";
            }
        }

        string RedactPathMatch(Match m)
        {
            if (m == null || string.IsNullOrEmpty(m.Value)) return string.Empty;
            var raw = m.Value.TrimEnd('"', '\'', ')', ']', ',', ';');
            if (string.IsNullOrWhiteSpace(raw)) return m.Value;
            // Stack / compiler snippets like "…\Foo.cs:line 42" or "…\Foo.cs:12" — strip :line so we do not treat ":12" as path.
            if (Regex.IsMatch(raw, @"\.[A-Za-z0-9]{1,12}:\d+$", RegexOptions.CultureInvariant))
                raw = Regex.Replace(raw, @":\d+$", string.Empty, RegexOptions.CultureInvariant);
            try
            {
                return FormatPath(raw.Replace('/', Path.DirectorySeparatorChar));
            }
            catch
            {
                return "(redacted)";
            }
        }

        public string FormatPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            if (!_d.RedactPathsEnabled()) return path!;
            try
            {
                var trimmed = path!.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var name = Path.GetFileName(trimmed);
                return string.IsNullOrWhiteSpace(name) ? "(redacted)" : ".../" + name;
            }
            catch
            {
                return "(redacted)";
            }
        }

        /// <summary>
        /// <see cref="LibraryBrowserFolderView.ViewKey"/> can embed a full folder path (e.g. console
        /// grouping). When path redaction is on, only path-like pipe segments are shortened.
        /// </summary>
        public string FormatViewKey(string? viewKey)
        {
            if (string.IsNullOrWhiteSpace(viewKey) || !_d.RedactPathsEnabled()) return viewKey ?? string.Empty;
            var parts = viewKey!.Split('|');
            for (var i = 0; i < parts.Length; i++)
            {
                if (SegmentLooksLikePath(parts[i]))
                    parts[i] = FormatPath(parts[i]);
            }
            return string.Join("|", parts);
        }

        public static bool SegmentLooksLikePath(string? segment)
        {
            if (string.IsNullOrWhiteSpace(segment)) return false;
            if (segment!.IndexOf(Path.DirectorySeparatorChar) >= 0) return true;
            if (segment.IndexOf(Path.AltDirectorySeparatorChar) >= 0) return true;
            if (segment.StartsWith("\\\\", StringComparison.Ordinal)) return true;
            return segment.Length >= 2 && char.IsLetter(segment[0]) && segment[1] == ':';
        }

        public static string FormatException(Exception? ex)
        {
            if (ex == null) return string.Empty;
            var s = ex.ToString();
            const int max = 32768;
            if (s.Length > max) s = s.Substring(0, max) + "... (truncated)";
            return s;
        }

        public static string FormatLogUtcTimestamp()
        {
            return DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        }

        // region: file IO --------------------------------------------------------------

        void AppendLine(string? path, string line)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            Directory.CreateDirectory(_d.LogsRoot);
            lock (_fileSync)
            {
                if (string.Equals(path, TroubleshootingLogFilePath(), StringComparison.OrdinalIgnoreCase))
                    RotateTroubleshootingIfNeeded(path!);
                for (int attempt = 0; attempt < 4; attempt++)
                {
                    try
                    {
                        using (var stream = new FileStream(path!, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
                        using (var writer = new StreamWriter(stream))
                        {
                            writer.WriteLine(line);
                            writer.Flush();
                            return;
                        }
                    }
                    catch (IOException)
                    {
                        if (attempt == 3) return;
                        Thread.Sleep(25 * (attempt + 1));
                    }
                }
            }
        }

        void RotateTroubleshootingIfNeeded(string troubleshootingPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(troubleshootingPath) || !File.Exists(troubleshootingPath)) return;
                var length = new FileInfo(troubleshootingPath).Length;
                if (length < _d.MaxTroubleshootingBytes) return;
                var rotated = Path.Combine(
                    _d.LogsRoot,
                    "PixelVault-troubleshooting-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".log");
                File.Move(troubleshootingPath, rotated);
            }
            catch
            {
            }
        }
    }
}
