#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;

namespace PixelVaultNative
{
    /// <summary>
    /// PV-PLN-GPRO-001 Phase D.1 — pure session-grouping math for the Game Profile
    /// "Sessions" section. Walks an arbitrarily-ordered set of (file, captured-utc)
    /// entries, drops entries with no resolved date, sorts ascending, and groups
    /// consecutive entries into sessions whenever the gap to the previous capture
    /// exceeds the (clamped) session threshold setting.
    ///
    /// Returns sessions in newest-first order with each session's entries also
    /// newest-first so the renderer can iterate without re-sorting. This is the
    /// surface Phase D.2 / D.3 / D.5 will consume; the older
    /// <see cref="MainWindow.ComputeLibraryGameProfileMetrics"/> placeholder count
    /// also funnels through here so Phase A's stat card and Phase D.2's section
    /// can never disagree on what "1 session" means.
    /// </summary>
    internal sealed class LibraryGameProfileSessionEntry
    {
        public string FilePath { get; }
        public DateTime CapturedUtc { get; }
        public bool IsVideo { get; }

        public LibraryGameProfileSessionEntry(string filePath, DateTime capturedUtc, bool isVideo)
        {
            FilePath = filePath ?? string.Empty;
            CapturedUtc = capturedUtc;
            IsVideo = isVideo;
        }
    }

    internal sealed class LibraryGameProfileSession
    {
        public DateTime StartUtc { get; }
        public DateTime EndUtc { get; }
        public IReadOnlyList<LibraryGameProfileSessionEntry> Entries { get; }
        public int VideoCount { get; }
        public int Count => Entries.Count;
        public TimeSpan Duration => EndUtc - StartUtc;

        public LibraryGameProfileSession(
            DateTime startUtc,
            DateTime endUtc,
            IReadOnlyList<LibraryGameProfileSessionEntry> entriesNewestFirst,
            int videoCount)
        {
            StartUtc = startUtc;
            EndUtc = endUtc;
            Entries = entriesNewestFirst ?? Array.Empty<LibraryGameProfileSessionEntry>();
            VideoCount = videoCount;
        }
    }

    internal static class LibraryGameProfileSessionMath
    {
        /// <summary>
        /// Groups <paramref name="entries"/> into sessions using
        /// <paramref name="thresholdMinutes"/> as the gap-defining boundary. The
        /// raw threshold value is normalized via
        /// <see cref="SettingsService.NormalizeLibrarySessionThresholdMinutes"/>
        /// so callers can pass the live setting without pre-clamping
        /// (PV-POL-GPRO-SESSION-001).
        ///
        /// A new session starts when the gap to the previous capture is
        /// <em>strictly greater than</em> the threshold — matches the existing
        /// Phase A placeholder count so adopting the new helper doesn't silently
        /// re-bucket the Sessions stat card.
        /// </summary>
        public static IReadOnlyList<LibraryGameProfileSession> BuildSessions(
            IReadOnlyList<LibraryGameProfileSessionEntry> entries,
            int thresholdMinutes)
        {
            if (entries == null || entries.Count == 0)
                return Array.Empty<LibraryGameProfileSession>();

            var ascending = entries
                .Where(e => e != null && e.CapturedUtc > DateTime.MinValue)
                .OrderBy(e => e.CapturedUtc)
                .ToList();
            if (ascending.Count == 0) return Array.Empty<LibraryGameProfileSession>();

            var threshold = TimeSpan.FromMinutes(SettingsService.NormalizeLibrarySessionThresholdMinutes(thresholdMinutes));
            var sessions = new List<LibraryGameProfileSession>();
            var current = new List<LibraryGameProfileSessionEntry> { ascending[0] };
            for (var i = 1; i < ascending.Count; i++)
            {
                var prev = ascending[i - 1];
                var entry = ascending[i];
                if (entry.CapturedUtc - prev.CapturedUtc > threshold)
                {
                    sessions.Add(BuildSession(current));
                    current = new List<LibraryGameProfileSessionEntry>();
                }
                current.Add(entry);
            }
            if (current.Count > 0) sessions.Add(BuildSession(current));

            sessions.Reverse();
            return sessions;
        }

        public static int CountSessions(
            IReadOnlyList<LibraryGameProfileSessionEntry> entries,
            int thresholdMinutes)
            => BuildSessions(entries, thresholdMinutes).Count;

        public static IReadOnlyList<LibraryGameProfileSessionEntry> BuildEntriesFromDates(
            IReadOnlyList<DateTime> dates)
        {
            if (dates == null || dates.Count == 0)
                return Array.Empty<LibraryGameProfileSessionEntry>();
            var result = new LibraryGameProfileSessionEntry[dates.Count];
            for (var i = 0; i < dates.Count; i++)
                result[i] = new LibraryGameProfileSessionEntry(string.Empty, dates[i], false);
            return result;
        }

        static LibraryGameProfileSession BuildSession(IReadOnlyList<LibraryGameProfileSessionEntry> ascendingEntries)
        {
            var start = ascendingEntries[0].CapturedUtc;
            var end = ascendingEntries[ascendingEntries.Count - 1].CapturedUtc;
            var newestFirst = new LibraryGameProfileSessionEntry[ascendingEntries.Count];
            for (var i = 0; i < ascendingEntries.Count; i++)
                newestFirst[i] = ascendingEntries[ascendingEntries.Count - 1 - i];
            var videoCount = 0;
            for (var i = 0; i < newestFirst.Length; i++)
                if (newestFirst[i].IsVideo) videoCount++;
            return new LibraryGameProfileSession(start, end, newestFirst, videoCount);
        }
    }
}
