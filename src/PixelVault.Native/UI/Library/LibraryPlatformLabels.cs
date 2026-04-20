#nullable enable
using System;
using System.Windows.Media;

namespace PixelVaultNative
{
    /// <summary>
    /// PV-PLN-UI-001 Step 12: platform-label helpers lifted off <c>MainWindow</c> (lines ~528–587
    /// before the slice). The data-shaped helpers (<see cref="PrimaryPlatformLabel(FilenameParseResult)"/>,
    /// <see cref="FilenameGuessLabel(FilenameParseResult)"/>, <see cref="IsSteamManualExportWithoutAppId(FilenameParseResult)"/>,
    /// <see cref="PlatformGroupOrder(string)"/>) take already-parsed input so they stay pure and
    /// parser-free. <see cref="PreviewBadgeBrush(string)"/> is WPF-only and lives here so callers
    /// never re-hunt for the "what color is a Steam badge?" mapping.
    ///
    /// iOS alignment: contract-shaped. The data-shaped helpers are safe to consume from any client
    /// (iOS, backend, tests) and produce the exact same grouping / label vocabulary the desktop app
    /// uses today. The brush method stays put as an explicit WPF-only tail.
    /// </summary>
    internal static class LibraryPlatformLabels
    {
        /// <summary>Canonical platform label for a parsed filename (<c>Other</c> when unknown).</summary>
        public static string PrimaryPlatformLabel(FilenameParseResult parsed)
        {
            return parsed.PlatformLabel;
        }

        /// <summary>
        /// Human-facing "what did we infer?" label for the intake preview / manual-metadata drawer.
        /// Prefers Steam AppID, then non-Steam ID, then the "needs manual" hint, else falls back to
        /// the platform label (or <c>"No confident match"</c> when it's just <c>Other</c>).
        /// </summary>
        public static string FilenameGuessLabel(FilenameParseResult parsed)
        {
            var appId = parsed.SteamAppId;
            if (!string.IsNullOrWhiteSpace(appId)) return "Steam AppID " + appId;
            var nonSteamId = parsed.NonSteamId;
            if (!string.IsNullOrWhiteSpace(nonSteamId)) return "Non-Steam ID " + nonSteamId;
            if (parsed.RoutesToManualWhenMissingSteamAppId) return "Steam export | AppID needed";
            var label = parsed.PlatformLabel;
            return string.Equals(label, "Other", StringComparison.OrdinalIgnoreCase) ? "No confident match" : label;
        }

        /// <summary>
        /// True when the parse result is a Steam-style manual export that still needs an AppID
        /// attached before import (no Steam AppID and no non-Steam ID yet).
        /// </summary>
        public static bool IsSteamManualExportWithoutAppId(FilenameParseResult parsed)
        {
            return parsed.RoutesToManualWhenMissingSteamAppId
                && string.IsNullOrWhiteSpace(parsed.SteamAppId)
                && string.IsNullOrWhiteSpace(parsed.NonSteamId);
        }

        /// <summary>
        /// Stable sort order for platform groups in the intake preview, folder tile badges, and
        /// library folder list. Unknown labels sort after everything else (<c>8</c>).
        /// </summary>
        public static int PlatformGroupOrder(string label)
        {
            switch (label)
            {
                case "Steam": return 0;
                case "Emulation": return 1;
                case "PS5": return 2;
                case "Switch": return 3;
                case "Xbox": return 4;
                case "PC": return 5;
                case "Multiple Tags": return 6;
                case "Other": return 7;
                default: return 8;
            }
        }

        /// <summary>WPF-only badge color for a platform label.</summary>
        public static Brush PreviewBadgeBrush(string label)
        {
            switch (label)
            {
                case "Xbox": return UiBrushHelper.FromHex("#2E8B57");
                case "Steam": return UiBrushHelper.FromHex("#2F6FDB");
                case "Emulation": return UiBrushHelper.FromHex("#B26A3C");
                case "PC": return UiBrushHelper.FromHex("#4F6D7A");
                case "PS5": return UiBrushHelper.FromHex("#2563EB");
                case "PlayStation": return UiBrushHelper.FromHex("#2563EB");
                case "Switch":
                case "Nintendo": return UiBrushHelper.FromHex("#E94B43");
                default: return UiBrushHelper.FromHex("#8B6F47");
            }
        }
    }
}
