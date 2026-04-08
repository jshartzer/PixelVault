using System;
using System.Text.RegularExpressions;

namespace PixelVaultNative
{
    /// <summary>
    /// Folds game titles that only differ by Windows filename rules (e.g. ":" vs "-" vs stripped)
    /// so one <see cref="GameIndexEditorRow"/> matches on-disk folder / import rename prefixes.
    /// </summary>
    internal static class GameIndexIdentityMatch
    {
        internal static string FoldNormalizedTitle(string normalizedName, Func<string, string> sanitize)
        {
            if (string.IsNullOrWhiteSpace(normalizedName)) return string.Empty;
            var s = sanitize != null ? sanitize(normalizedName) : normalizedName.Trim();
            s = Regex.Replace(s, @"(\w)-\s+(?=\w)", "$1 ", RegexOptions.CultureInvariant);
            s = Regex.Replace(s, "\\s+", " ").Trim();
            return s;
        }
    }
}
