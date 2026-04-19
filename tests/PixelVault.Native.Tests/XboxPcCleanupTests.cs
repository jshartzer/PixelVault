using Xunit;

namespace PixelVaultNative.Tests;

public sealed class XboxPcCleanupTests
{
    [Fact]
    public void RewriteXboxPcCleanupTagText_ReplacesAliasWithPcAndPreservesOtherTags()
    {
        var rewritten = MainWindow.RewriteXboxPcCleanupTagText("Game Capture, Platform:Xbox PC, Photography, Favorite Shot");

        Assert.Equal("Game Capture, PC, Photography, Favorite Shot", rewritten);
    }

    [Fact]
    public void RewriteXboxPcCleanupTagText_CollapsesDuplicatePcAliasesIntoSinglePcTag()
    {
        var rewritten = MainWindow.RewriteXboxPcCleanupTagText("PC, Platform:Xbox PC, Xbox/Windows");

        Assert.Equal("PC", rewritten);
    }

    [Fact]
    public void RewriteXboxPcFilenameConventionRuleInPlace_NormalizesPlatformNameAndTags()
    {
        var rule = new FilenameConventionRule
        {
            ConventionId = "custom_xbox_pc",
            Name = "Custom: Xbox PC Capture",
            PlatformLabel = "Xbox/Windows",
            PlatformTagsText = "Platform:Xbox PC; Favorite"
        };

        var changed = MainWindow.RewriteXboxPcFilenameConventionRuleInPlace(rule);

        Assert.True(changed);
        Assert.Equal("Custom: PC Capture", rule.Name);
        Assert.Equal("PC", rule.PlatformLabel);
        Assert.Equal("PC; Favorite", rule.PlatformTagsText);
    }
}
