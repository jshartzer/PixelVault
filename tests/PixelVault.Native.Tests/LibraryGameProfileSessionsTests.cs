#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests
{
    /// <summary>
    /// PV-PLN-GPRO-001 Phase D.1: focused tests for the pure session-grouping math
    /// extracted into <see cref="LibraryGameProfileSessionMath"/>. The helper backs
    /// both the Phase A "Sessions" stat card and the Phase D Sessions section, so
    /// these tests pin the gap-defining boundary, the newest-first ordering
    /// contract, and the threshold normalization parity the rest of the profile
    /// relies on (PV-POL-GPRO-SESSION-001).
    /// </summary>
    public sealed class LibraryGameProfileSessionsTests
    {
        static readonly DateTime BaseUtc = new DateTime(2025, 5, 8, 18, 0, 0, DateTimeKind.Utc);

        static LibraryGameProfileSessionEntry Entry(int minuteOffset, bool isVideo = false, string? path = null)
            => new LibraryGameProfileSessionEntry(
                path ?? ("file_" + minuteOffset.ToString("00") + ".png"),
                BaseUtc.AddMinutes(minuteOffset),
                isVideo);

        [Fact]
        public void BuildSessions_NoEntries_ReturnsEmpty()
        {
            Assert.Empty(LibraryGameProfileSessionMath.BuildSessions(Array.Empty<LibraryGameProfileSessionEntry>(), 60));
            Assert.Empty(LibraryGameProfileSessionMath.BuildSessions(null!, 60));
        }

        [Fact]
        public void BuildSessions_AllEntriesUnresolvedDate_ReturnsEmpty()
        {
            var entries = new[]
            {
                new LibraryGameProfileSessionEntry("a.png", DateTime.MinValue, false),
                new LibraryGameProfileSessionEntry("b.png", DateTime.MinValue, false)
            };
            Assert.Empty(LibraryGameProfileSessionMath.BuildSessions(entries, 60));
        }

        [Fact]
        public void BuildSessions_AllWithinThreshold_ReturnsOneSession()
        {
            var entries = new[] { Entry(0), Entry(15), Entry(45), Entry(60) };

            var sessions = LibraryGameProfileSessionMath.BuildSessions(entries, 60);

            var session = Assert.Single(sessions);
            Assert.Equal(BaseUtc, session.StartUtc);
            Assert.Equal(BaseUtc.AddMinutes(60), session.EndUtc);
            Assert.Equal(TimeSpan.FromMinutes(60), session.Duration);
            Assert.Equal(4, session.Count);
            Assert.Equal(0, session.VideoCount);
            // Session entries are newest-first so renderers can iterate without re-sorting.
            Assert.Collection(session.Entries,
                e => Assert.Equal(BaseUtc.AddMinutes(60), e.CapturedUtc),
                e => Assert.Equal(BaseUtc.AddMinutes(45), e.CapturedUtc),
                e => Assert.Equal(BaseUtc.AddMinutes(15), e.CapturedUtc),
                e => Assert.Equal(BaseUtc, e.CapturedUtc));
        }

        [Fact]
        public void BuildSessions_GapAtBoundaryIsSameSession_GapAboveBoundaryIsNewSession()
        {
            // Strict-greater-than boundary: gap exactly == threshold stays in the same session.
            var entries = new[] { Entry(0), Entry(60), Entry(121) };

            var sessions = LibraryGameProfileSessionMath.BuildSessions(entries, 60);

            Assert.Equal(2, sessions.Count);
            // Newest-first: the 60->121 gap (61 minutes) opens session #1 (newest).
            Assert.Equal(BaseUtc.AddMinutes(121), sessions[0].StartUtc);
            Assert.Equal(BaseUtc.AddMinutes(121), sessions[0].EndUtc);
            Assert.Equal(BaseUtc, sessions[1].StartUtc);
            Assert.Equal(BaseUtc.AddMinutes(60), sessions[1].EndUtc);
        }

        [Fact]
        public void BuildSessions_MultipleSessionsAcrossThresholds_GroupedNewestFirst()
        {
            // Three bursts: 0-30, 200-220, 600-660. With a 60-minute threshold all
            // three should split.
            var entries = new[]
            {
                Entry(0), Entry(10), Entry(30),
                Entry(200), Entry(210), Entry(220),
                Entry(600), Entry(630), Entry(660)
            };

            var sessions = LibraryGameProfileSessionMath.BuildSessions(entries, 60);

            Assert.Equal(3, sessions.Count);
            Assert.Equal(BaseUtc.AddMinutes(600), sessions[0].StartUtc);
            Assert.Equal(BaseUtc.AddMinutes(660), sessions[0].EndUtc);
            Assert.Equal(3, sessions[0].Count);
            Assert.Equal(BaseUtc.AddMinutes(200), sessions[1].StartUtc);
            Assert.Equal(BaseUtc.AddMinutes(220), sessions[1].EndUtc);
            Assert.Equal(BaseUtc, sessions[2].StartUtc);
            Assert.Equal(BaseUtc.AddMinutes(30), sessions[2].EndUtc);
        }

        [Fact]
        public void BuildSessions_UnsortedInput_IsSortedAscendingBeforeGrouping()
        {
            var entries = new[] { Entry(220), Entry(0), Entry(10), Entry(200) };

            var sessions = LibraryGameProfileSessionMath.BuildSessions(entries, 60);

            Assert.Equal(2, sessions.Count);
            Assert.Equal(BaseUtc.AddMinutes(200), sessions[0].StartUtc);
            Assert.Equal(BaseUtc.AddMinutes(220), sessions[0].EndUtc);
            Assert.Equal(BaseUtc, sessions[1].StartUtc);
            Assert.Equal(BaseUtc.AddMinutes(10), sessions[1].EndUtc);
        }

        [Fact]
        public void BuildSessions_LargeThreshold_CollapsesToOneSession()
        {
            var entries = new[] { Entry(0), Entry(120), Entry(360) };

            var sessions = LibraryGameProfileSessionMath.BuildSessions(entries, 720);

            Assert.Single(sessions);
            Assert.Equal(3, sessions[0].Count);
        }

        [Fact]
        public void BuildSessions_VeryShortThreshold_ClampsToMinimumAndSplitsGapsAboveIt()
        {
            // Threshold below the minimum (15) should clamp UP to 15. Strict-
            // greater-than boundary semantics mean a 15-minute gap stays in the
            // same session, but 16+ minute gaps split. We pick entries so that
            // gaps are 15, 17, 16 minutes => one same-session boundary followed
            // by two splits => 3 sessions.
            var entries = new[] { Entry(0), Entry(15), Entry(32), Entry(48) };

            var sessions = LibraryGameProfileSessionMath.BuildSessions(entries, 1);

            Assert.Equal(3, sessions.Count);
            // Newest-first: sessions[0] is the latest single-entry burst at 48.
            Assert.Equal(BaseUtc.AddMinutes(48), sessions[0].StartUtc);
            Assert.Equal(BaseUtc.AddMinutes(48), sessions[0].EndUtc);
            // The middle session is the single 32-minute capture (16-minute split
            // from the prior 15-minute capture, 16-minute split to the next).
            Assert.Equal(BaseUtc.AddMinutes(32), sessions[1].StartUtc);
            Assert.Equal(BaseUtc.AddMinutes(32), sessions[1].EndUtc);
            // Oldest session contains entries 0 and 15 (15-minute gap == threshold).
            Assert.Equal(BaseUtc, sessions[2].StartUtc);
            Assert.Equal(BaseUtc.AddMinutes(15), sessions[2].EndUtc);
            Assert.Equal(2, sessions[2].Count);
        }

        [Fact]
        public void BuildSessions_ThresholdClampsToSettingsServiceRange()
        {
            // SettingsService.NormalizeLibrarySessionThresholdMinutes clamps to
            // [15, 720]; we verify the helper agrees by feeding sub-15 thresholds.
            var entries = new[] { Entry(0), Entry(14) };

            // 1 -> clamps to 15, gap of 14 < 15 so they stay in one session.
            var clampedUp = LibraryGameProfileSessionMath.BuildSessions(entries, 1);
            Assert.Single(clampedUp);
            Assert.Equal(2, clampedUp[0].Count);

            // 999999 -> clamps to 720; same input still one session.
            var clampedDown = LibraryGameProfileSessionMath.BuildSessions(entries, 999999);
            Assert.Single(clampedDown);
        }

        [Fact]
        public void BuildSessions_VideoCount_ReflectsIsVideoFlag()
        {
            var entries = new[]
            {
                Entry(0, isVideo: true),
                Entry(10, isVideo: false),
                Entry(20, isVideo: true)
            };

            var session = Assert.Single(LibraryGameProfileSessionMath.BuildSessions(entries, 60));

            Assert.Equal(2, session.VideoCount);
            Assert.Equal(3, session.Count);
        }

        [Fact]
        public void CountSessions_MatchesBuildSessionsCount()
        {
            var entries = new[] { Entry(0), Entry(120), Entry(360) };
            var built = LibraryGameProfileSessionMath.BuildSessions(entries, 60);
            Assert.Equal(built.Count, LibraryGameProfileSessionMath.CountSessions(entries, 60));
        }

        [Fact]
        public void BuildEntriesFromDates_PassesThroughThresholdSemantics()
        {
            var dates = new[]
            {
                BaseUtc,
                BaseUtc.AddMinutes(45),
                BaseUtc.AddMinutes(180)
            };
            var fromDates = LibraryGameProfileSessionMath.BuildEntriesFromDates(dates);
            Assert.Equal(3, fromDates.Count);
            Assert.All(fromDates, e =>
            {
                Assert.Equal(string.Empty, e.FilePath);
                Assert.False(e.IsVideo);
            });

            // Same data through the entry-list path should produce the same number
            // of sessions; this is the contract the legacy date-only call sites
            // adopt by going through BuildEntriesFromDates.
            Assert.Equal(2, LibraryGameProfileSessionMath.CountSessions(fromDates, 60));
        }

        [Fact]
        public void BuildSessions_DropsUnresolvedEntriesButKeepsOthers()
        {
            var entries = new List<LibraryGameProfileSessionEntry>
            {
                Entry(0),
                new LibraryGameProfileSessionEntry("missing.png", DateTime.MinValue, false),
                Entry(45)
            };

            var session = Assert.Single(LibraryGameProfileSessionMath.BuildSessions(entries, 60));

            Assert.Equal(2, session.Count);
            Assert.DoesNotContain(session.Entries, e => e.FilePath == "missing.png");
        }
    }
}
