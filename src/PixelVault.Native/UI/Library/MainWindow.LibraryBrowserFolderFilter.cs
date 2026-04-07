using System;
using System.Linq;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        /// <summary>Folder-list filter predicate; keep in sync with <c>docs/SMART_VIEWS_LIBRARY.md</c>.</summary>
        internal static bool LibraryBrowserFolderViewMatchesFilter(string normalizedFilterMode, LibraryBrowserFolderView folder, Func<string, string> normalizeConsoleLabel)
        {
            if (folder == null) return false;
            var normConsole = normalizeConsoleLabel ?? (s => s ?? string.Empty);
            switch ((normalizedFilterMode ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "completed":
                    return folder.IsCompleted100Percent;
                case "crossplatform":
                {
                    var distinct = (folder.PlatformLabels ?? new string[0])
                        .Where(label => !string.IsNullOrWhiteSpace(label))
                        .Select(l => normConsole(l))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Count();
                    return distinct > 1 || folder.IsMergedAcrossPlatforms;
                }
                case "large":
                    return folder.FileCount >= 25;
                case "needssteam":
                    if (!string.IsNullOrWhiteSpace(folder.SteamAppId)) return false;
                    if (string.Equals(normConsole(folder.PrimaryPlatformLabel ?? string.Empty), "Steam", StringComparison.OrdinalIgnoreCase))
                        return true;
                    foreach (var label in folder.PlatformLabels ?? new string[0])
                    {
                        if (string.IsNullOrWhiteSpace(label)) continue;
                        if (string.Equals(normConsole(label), "Steam", StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                    return false;
                case "missinggameid":
                    return string.IsNullOrWhiteSpace(folder.GameId);
                case "nocover":
                    return string.IsNullOrWhiteSpace(folder.PreviewImagePath);
                default:
                    return true;
            }
        }
    }
}
