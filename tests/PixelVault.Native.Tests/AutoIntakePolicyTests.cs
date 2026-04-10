using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class AutoIntakePolicyTests
{
    [Fact]
    public void IsEligible_CustomRequiresTrustedMode()
    {
        var rule = new FilenameConventionRule
        {
            ConventionId = "c1",
            Enabled = true,
            IsBuiltIn = false,
            AutoIntakeMode = FilenameAutoIntakeModes.ManualOnly
        };
        var analysis = new IntakePreviewFileAnalysis
        {
            CanUpdateMetadata = true,
            Parsed = new FilenameParseResult { MatchedConvention = true, ConventionId = "c1" }
        };
        Assert.False(AutoIntakePolicy.IsEligibleForBackgroundAutoImport(analysis, rule));

        rule.AutoIntakeMode = FilenameAutoIntakeModes.TrustedExactMatch;
        Assert.True(AutoIntakePolicy.IsEligibleForBackgroundAutoImport(analysis, rule));
    }

    [Fact]
    public void IsEligible_BuiltInIgnoresManualModeFlag()
    {
        var rule = new FilenameConventionRule
        {
            ConventionId = "b1",
            Enabled = true,
            IsBuiltIn = true,
            AutoIntakeMode = FilenameAutoIntakeModes.ManualOnly
        };
        var analysis = new IntakePreviewFileAnalysis
        {
            CanUpdateMetadata = true,
            Parsed = new FilenameParseResult { MatchedConvention = true, ConventionId = "b1" }
        };
        Assert.True(AutoIntakePolicy.IsEligibleForBackgroundAutoImport(analysis, rule));
    }

    [Fact]
    public void TryGetIneligibilityReason_ReturnsNullWhenEligible()
    {
        var rule = new FilenameConventionRule
        {
            ConventionId = "c1",
            Enabled = true,
            IsBuiltIn = false,
            AutoIntakeMode = FilenameAutoIntakeModes.TrustedExactMatch
        };
        var analysis = new IntakePreviewFileAnalysis
        {
            CanUpdateMetadata = true,
            Parsed = new FilenameParseResult { MatchedConvention = true, ConventionId = "c1" }
        };
        Assert.Null(AutoIntakePolicy.TryGetIneligibilityReason(analysis, rule));
    }

    [Fact]
    public void TryGetIneligibilityReason_ExplainsCustomManualOnly()
    {
        var rule = new FilenameConventionRule
        {
            ConventionId = "c1",
            Enabled = true,
            IsBuiltIn = false,
            AutoIntakeMode = FilenameAutoIntakeModes.ManualOnly
        };
        var analysis = new IntakePreviewFileAnalysis
        {
            CanUpdateMetadata = true,
            Parsed = new FilenameParseResult { MatchedConvention = true, ConventionId = "c1" }
        };
        Assert.Equal("custom_rule_not_trusted_exact_match", AutoIntakePolicy.TryGetIneligibilityReason(analysis, rule));
    }

    [Fact]
    public void IsEligible_RequiresCanUpdateMetadata_EvenForBuiltIn()
    {
        var rule = new FilenameConventionRule
        {
            ConventionId = "b1",
            Enabled = true,
            IsBuiltIn = true,
            AutoIntakeMode = FilenameAutoIntakeModes.ManualOnly
        };
        var analysis = new IntakePreviewFileAnalysis
        {
            CanUpdateMetadata = false,
            Parsed = new FilenameParseResult { MatchedConvention = true, ConventionId = "b1" }
        };
        Assert.False(AutoIntakePolicy.IsEligibleForBackgroundAutoImport(analysis, rule));
        Assert.Equal("cannot_update_metadata", AutoIntakePolicy.TryGetIneligibilityReason(analysis, rule));
    }

    [Fact]
    public void IsEligible_DisabledRule_NotEligible()
    {
        var rule = new FilenameConventionRule
        {
            ConventionId = "c1",
            Enabled = false,
            IsBuiltIn = false,
            AutoIntakeMode = FilenameAutoIntakeModes.TrustedExactMatch
        };
        var analysis = new IntakePreviewFileAnalysis
        {
            CanUpdateMetadata = true,
            Parsed = new FilenameParseResult { MatchedConvention = true, ConventionId = "c1" }
        };
        Assert.False(AutoIntakePolicy.IsEligibleForBackgroundAutoImport(analysis, rule));
        Assert.Equal("rule_disabled", AutoIntakePolicy.TryGetIneligibilityReason(analysis, rule));
    }

    [Fact]
    public void IsEligible_ConventionIdMismatch_NotEligible()
    {
        var rule = new FilenameConventionRule
        {
            ConventionId = "c1",
            Enabled = true,
            IsBuiltIn = false,
            AutoIntakeMode = FilenameAutoIntakeModes.TrustedExactMatch
        };
        var analysis = new IntakePreviewFileAnalysis
        {
            CanUpdateMetadata = true,
            Parsed = new FilenameParseResult { MatchedConvention = true, ConventionId = "other" }
        };
        Assert.False(AutoIntakePolicy.IsEligibleForBackgroundAutoImport(analysis, rule));
        Assert.Equal("convention_id_mismatch", AutoIntakePolicy.TryGetIneligibilityReason(analysis, rule));
    }

    [Fact]
    public void TryGetIneligibilityReason_UnmatchedConvention()
    {
        var rule = new FilenameConventionRule { ConventionId = "c1", Enabled = true, IsBuiltIn = false, AutoIntakeMode = FilenameAutoIntakeModes.TrustedExactMatch };
        var analysis = new IntakePreviewFileAnalysis
        {
            CanUpdateMetadata = true,
            Parsed = new FilenameParseResult { MatchedConvention = false, ConventionId = "c1" }
        };
        Assert.Equal("filename_not_matched_to_convention", AutoIntakePolicy.TryGetIneligibilityReason(analysis, rule));
    }
}
