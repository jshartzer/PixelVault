#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;

namespace PixelVaultNative
{
    /// <summary>
    /// PV-PLN-UI-001 Step 12: pure text / path / media-type helpers that used to live as
    /// <c>static</c> or pure-instance methods on <c>MainWindow</c> (lines ~589–637 before the slice).
    /// No WPF, no <c>MainWindow</c> state — anything that needs filename parsing takes a pre-parsed
    /// <see cref="FilenameParseResult"/> so the helper stays pure and the parser dependency lives at
    /// the call site (currently the <c>MainWindow</c> forwarders).
    ///
    /// iOS alignment: contract-shaped. Same strings / timestamps come out on every platform, so the
    /// iOS client and any future backend can share this vocabulary for library dates, tag lists, and
    /// media-type gates without pulling in any WPF types.
    /// </summary>
    internal static class TextAndPathHelpers
    {
        public static int ParseInt(string value)
        {
            int result;
            return int.TryParse(value, out result) ? result : 0;
        }

        public static long ParseLong(string value)
        {
            long result;
            return long.TryParse(value, out result) ? result : 0L;
        }

        public static string FormatFriendlyTimestamp(DateTime value)
        {
            int hour12 = value.Hour % 12;
            if (hour12 == 0) hour12 = 12;
            var suffix = value.Hour >= 12 ? "PM" : "AM";
            return value.Year.ToString("0000") + "-" + value.Month.ToString("00") + "-" + value.Day.ToString("00") + " " + hour12 + ":" + value.Minute.ToString("00") + ":" + value.Second.ToString("00") + " " + suffix;
        }

        public static string Sanitize(string s)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '-');
            return Regex.Replace(s, "\\s+", " ").Trim();
        }

        public static string CleanComment(string s)
        {
            return string.IsNullOrWhiteSpace(s) ? string.Empty : Regex.Replace(s.Replace("\r", " ").Replace("\n", " "), "\\s+", " ").Trim();
        }

        public static string CleanTag(string s)
        {
            return string.IsNullOrWhiteSpace(s) ? string.Empty : Regex.Replace(s, "\\s+", " ").Trim();
        }

        public static string[] ParseTagText(string s)
        {
            return (s ?? string.Empty)
                .Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(CleanTag)
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public static bool SameManualText(string? left, string? right)
        {
            return string.Equals((left ?? string.Empty).Trim(), (right ?? string.Empty).Trim(), StringComparison.Ordinal);
        }

        public static string Unique(string path)
        {
            if (!File.Exists(path)) return path;
            var dir = Path.GetDirectoryName(path);
            var name = Path.GetFileNameWithoutExtension(path);
            var ext = Path.GetExtension(path);
            int i = 2;
            string candidate;
            do
            {
                candidate = Path.Combine(dir!, name + " (" + i + ")" + ext);
                i++;
            } while (File.Exists(candidate));
            return candidate;
        }

        public static void EnsureDir(string path, string label)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) throw new InvalidOperationException(label + " not found: " + path);
        }

        public static bool IsImage(string p)
        {
            var e = Path.GetExtension(p).ToLowerInvariant();
            return e == ".png" || e == ".jpg" || e == ".jpeg" || e == ".webp" || e == ".jxr";
        }

        public static bool IsPngOrJpeg(string p)
        {
            var e = Path.GetExtension(p).ToLowerInvariant();
            return e == ".png" || e == ".jpg" || e == ".jpeg";
        }

        public static bool IsVideo(string p)
        {
            var e = Path.GetExtension(p).ToLowerInvariant();
            return e == ".mp4" || e == ".mkv" || e == ".avi" || e == ".mov" || e == ".wmv" || e == ".webm";
        }

        public static bool IsMedia(string p)
        {
            var e = Path.GetExtension(p).ToLowerInvariant();
            return new[] { ".jpg", ".jpeg", ".png", ".webp", ".jxr", ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".webm" }.Contains(e);
        }

        public static string Quote(string s)
        {
            return s.Contains(" ") ? "\"" + s.Replace("\"", "\\\"") + "\"" : s;
        }

        public static string SafeCacheName(string title)
        {
            return Regex.Replace(NormalizeTitle(title), @"\s+", "_");
        }

        public static string NormalizeTitle(string? title)
        {
            title = WebUtility.HtmlDecode(title ?? string.Empty);
            title = title.Replace("â„¢", " ").Replace("Â®", " ").Replace("Â©", " ").Replace("_", " ").Replace("-", " ").Replace(":", " ");
            title = Regex.Replace(title, @"[^\p{L}\p{Nd}]+", " ");
            return Regex.Replace(title, @"\s+", " ").Trim().ToLowerInvariant();
        }

        public static string StripTags(string? html)
        {
            return Regex.Replace(html ?? string.Empty, "<.*?>", string.Empty);
        }

        /// <summary>
        /// Library "captured on" date for a file. Prefers the parsed capture timestamp — but for Xbox
        /// captures the parsed timestamp is the local broadcast start, not the file's on-disk time, so
        /// we fall back to the earlier of file-created / file-modified. Callers pass the pre-parsed
        /// <see cref="FilenameParseResult"/> so the parser dependency stays at the boundary.
        /// </summary>
        public static DateTime GetLibraryDate(string file, FilenameParseResult parsed)
        {
            var tags = parsed.PlatformTags ?? new string[0];
            if (!tags.Contains("Xbox"))
            {
                if (parsed.CaptureTime.HasValue) return parsed.CaptureTime.Value;
            }
            var created = File.GetCreationTime(file);
            var modified = File.GetLastWriteTime(file);
            if (created == DateTime.MinValue) return modified;
            if (modified == DateTime.MinValue) return created;
            return created < modified ? created : modified;
        }
    }
}
