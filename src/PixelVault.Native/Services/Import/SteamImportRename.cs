using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PixelVaultNative
{
    /// <summary>Steam intake filename rename rules and path map application (import workflow).</summary>
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
        /// then either ends or continues with a non-digit (e.g. <c>_</c>). Avoids <c>108710</c> matching <c>1087100_...</c>.
        /// </summary>
        internal static bool SteamAppIdLooksLikeFilenamePrefix(string appId, string baseName)
        {
            if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(baseName)) return false;
            if (!appId.All(char.IsDigit)) return false;
            if (baseName.Length < appId.Length) return false;
            if (!baseName.StartsWith(appId, StringComparison.Ordinal)) return false;
            if (baseName.Length == appId.Length) return true;
            return !char.IsDigit(baseName[appId.Length]);
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
