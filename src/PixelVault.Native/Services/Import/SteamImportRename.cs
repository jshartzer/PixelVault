using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PixelVaultNative
{
    /// <summary>
    /// Steam intake filename rename rules and path map application (import workflow).
    /// </summary>
    /// <remarks>
    /// <para><b>ID prefix rule</b> — <see cref="SteamAppIdLooksLikeFilenamePrefix"/> requires an all-digit token (Steam AppID or numeric NonSteamId) with
    /// a minimum length of two digits, then either the basename equals that token or the next character is <c>_</c> or <c>-</c>.
    /// Title-hint branch in <see cref="TryBuildSteamRenameBase"/> requires a trailing underscore after the hint. Tests:
    /// <c>SteamRenamePathMappingTests</c>.</para>
    /// <para>Cross-ref: <c>CODE_QUALITY_IMPROVEMENT_PLAN.md</c>, <c>NEXT_TRIM_PLAN.md</c> Tier 3, <c>PV-PLN-RVW-001</c> Phase 3.</para>
    /// </remarks>
    internal static class SteamImportRename
    {
        public static void ApplySteamRenameMapToReviewItems(List<ReviewItem> items, Dictionary<string, string> oldToNew)
        {
            if (items == null || oldToNew == null || oldToNew.Count == 0) return;
            foreach (var item in items)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.FilePath)) continue;
                string newPath;
                if (!oldToNew.TryGetValue(item.FilePath, out newPath) || string.IsNullOrWhiteSpace(newPath)) continue;
                item.FilePath = newPath;
                item.FileName = Path.GetFileName(newPath);
            }
        }

        public static void ApplySteamRenameMapToManualMetadataItems(List<ManualMetadataItem> items, Dictionary<string, string> oldToNew)
        {
            if (items == null || oldToNew == null || oldToNew.Count == 0) return;
            foreach (var item in items)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.FilePath)) continue;
                string newPath;
                if (!oldToNew.TryGetValue(item.FilePath, out newPath) || string.IsNullOrWhiteSpace(newPath)) continue;
                item.FilePath = newPath;
                item.FileName = Path.GetFileName(newPath);
            }
        }

        public static List<string> ResolveTopLevelPathsAfterSteamRename(IEnumerable<string> topLevelBeforeRename, Dictionary<string, string> oldToNew)
        {
            var list = new List<string>();
            if (topLevelBeforeRename == null) return list;
            foreach (var path in topLevelBeforeRename)
            {
                if (string.IsNullOrWhiteSpace(path)) continue;
                string newPath;
                if (oldToNew != null && oldToNew.TryGetValue(path, out newPath) && !string.IsNullOrWhiteSpace(newPath)) list.Add(newPath);
                else list.Add(path);
            }
            return list;
        }

        /// <summary>
        /// True when <paramref name="baseName"/> starts with the full Steam AppID token: same digit run as <paramref name="appId"/>,
        /// at least two digits, then either ends or continues with <c>_</c> or <c>-</c>.
        /// Avoids <c>108710</c> matching <c>1087100_...</c> and avoids treating <c>2561580.png</c> as an ID prefix.
        /// No upper length bound — <see cref="TryBuildSteamRenameBase"/> is also used for long numeric <b>NonSteamId</b> keys.
        /// </summary>
        internal static bool SteamAppIdLooksLikeFilenamePrefix(string appId, string baseName)
        {
            if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(baseName)) return false;
            if (!appId.All(char.IsDigit)) return false;
            const int minDigitTokenLength = 2;
            if (appId.Length < minDigitTokenLength) return false;
            if (baseName.Length < appId.Length) return false;
            if (!baseName.StartsWith(appId, StringComparison.Ordinal)) return false;
            if (baseName.Length == appId.Length) return true;
            var boundary = baseName[appId.Length];
            return boundary == '_' || boundary == '-';
        }

        internal static bool TryBuildSteamRenameBase(string baseName, string appId, string canonicalGameTitle, string gameTitleHint, out string newBase)
        {
            newBase = null;
            if (string.IsNullOrWhiteSpace(baseName) || string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(canonicalGameTitle)) return false;
            if (SteamAppIdLooksLikeFilenamePrefix(appId, baseName))
            {
                newBase = canonicalGameTitle + baseName.Substring(appId.Length);
                return true;
            }
            if (!string.IsNullOrWhiteSpace(gameTitleHint)
                && baseName.Length > gameTitleHint.Length
                && baseName.StartsWith(gameTitleHint, StringComparison.OrdinalIgnoreCase)
                && baseName[gameTitleHint.Length] == '_')
            {
                newBase = canonicalGameTitle + baseName.Substring(gameTitleHint.Length);
                return true;
            }
            if (baseName.StartsWith(canonicalGameTitle + "_", StringComparison.OrdinalIgnoreCase))
            {
                newBase = baseName;
                return true;
            }
            return false;
        }
    }
}
