using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class GameIndexIdentityMatchTests
{
    static string SanitizeLikeMainWindow(string s)
    {
        if (string.IsNullOrEmpty(s)) return s ?? string.Empty;
        foreach (var c in System.IO.Path.GetInvalidFileNameChars())
            s = s.Replace(c, '-');
        return System.Text.RegularExpressions.Regex.Replace(s, "\\s+", " ").Trim();
    }

    [Fact]
    public void FoldNormalizedTitle_MatchesColonTitle_WithSanitizedAndStrippedDiskForms()
    {
        var a = GameIndexIdentityMatch.FoldNormalizedTitle("Eternal Darkness: Sanity's Requiem", SanitizeLikeMainWindow);
        var b = GameIndexIdentityMatch.FoldNormalizedTitle("Eternal Darkness- Sanity's Requiem", SanitizeLikeMainWindow);
        var c = GameIndexIdentityMatch.FoldNormalizedTitle("Eternal Darkness Sanity's Requiem", SanitizeLikeMainWindow);
        Assert.Equal(a, b, ignoreCase: true);
        Assert.Equal(a, c, ignoreCase: true);
    }
}
