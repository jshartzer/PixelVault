using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class FilenameRulesServiceTests
{
    [Fact]
    public void CreateBuilderDraftFromFilePath_UsesOnlyBasename()
    {
        var seen = string.Empty;
        var service = CreateService(
            parseFileName: value =>
            {
                seen = value;
                return new FilenameParseResult { PlatformLabel = "Steam" };
            },
            buildRuleFromSample: sample => new FilenameConventionRule
            {
                ConventionId = "sample_rule",
                Name = "Sample Rule",
                Enabled = true,
                Priority = 1200,
                Pattern = "[title]_[yyyy][MM][dd][HH][mm][ss].[ext:media]",
                PatternText = "[title]_[yyyy][MM][dd][HH][mm][ss].[ext:media]",
                PlatformLabel = "Steam",
                PlatformTagsText = "Steam",
                TitleGroup = "title",
                TimestampGroup = "stamp",
                TimestampFormat = "yyyyMMddHHmmss"
            });

        var draft = service.CreateBuilderDraftFromFilePath(@"C:\captures\Halo Infinite_20260326221306.png");

        Assert.Equal("Halo Infinite_20260326221306.png", seen);
        Assert.NotNull(draft);
        Assert.Equal("Halo Infinite_20260326221306.png", draft.FileName);
        Assert.DoesNotContain(@"\", draft.FileName);
    }

    [Fact]
    public void CreateBuilderDraftFromRule_ReadableRule_RoundTripsThroughDraft()
    {
        var service = CreateService();
        var rule = new FilenameConventionRule
        {
            ConventionId = "steam_rule",
            Name = "Steam Rule",
            Enabled = true,
            Priority = 1200,
            Pattern = "[appid]_[yyyy][MM][dd][HH][mm][ss][opt-counter].[ext:media]",
            PatternText = "[appid]_[yyyy][MM][dd][HH][mm][ss][opt-counter].[ext:media]",
            PlatformLabel = "Steam",
            PlatformTagsText = "Steam",
            SteamAppIdGroup = "appid",
            TimestampGroup = "stamp",
            TimestampFormat = "yyyyMMddHHmmss"
        };

        var draft = service.CreateBuilderDraftFromRule(rule);
        var rebuilt = service.ApplyBuilderDraft(draft, new FilenameConventionRule());

        Assert.NotNull(draft);
        Assert.True(draft.CanRoundTripInBuilder);
        Assert.Contains(draft.Segments, segment => segment.AssignedRole == FilenameConventionBuilderComponentRole.SteamAppId);
        Assert.Contains(draft.Segments, segment => segment.AssignedRole == FilenameConventionBuilderComponentRole.Timestamp);
        Assert.Contains(draft.Segments, segment => segment.AssignedRole == FilenameConventionBuilderComponentRole.Counter);
        Assert.Contains(draft.Segments, segment => segment.AssignedRole == FilenameConventionBuilderComponentRole.Extension);
        Assert.Equal("[appid]_[yyyy][MM][dd][HH][mm][ss][opt-counter].[ext:media]", rebuilt.PatternText);
        Assert.Equal("appid", rebuilt.SteamAppIdGroup);
        Assert.Equal("stamp", rebuilt.TimestampGroup);
        Assert.Equal("yyyyMMddHHmmss", rebuilt.TimestampFormat);
    }

    [Fact]
    public void CreateBuilderDraftFromRule_ContainsRule_FallsBackToAdvanced()
    {
        var service = CreateService();
        var rule = new FilenameConventionRule
        {
            ConventionId = "contains_ps5",
            Name = "Contains PS5",
            Enabled = true,
            Priority = 500,
            Pattern = "[contains:PS5]",
            PatternText = "[contains:PS5]",
            PlatformLabel = "PS5",
            PlatformTagsText = "PS5;PlayStation"
        };

        var draft = service.CreateBuilderDraftFromRule(rule);

        Assert.NotNull(draft);
        Assert.False(draft.CanRoundTripInBuilder);
        Assert.Contains("Advanced", draft.FallbackReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateBuilderDraftFromFilePath_WhenSampleRuleIsRegexOnly_UsesGuidedFallback()
    {
        var service = CreateService(
            parseFileName: _ => new FilenameParseResult { PlatformLabel = "Other" },
            buildRuleFromSample: _ => new FilenameConventionRule
            {
                ConventionId = "regex_only",
                Name = "Regex only",
                Pattern = "^2026\\-04\\-02 09\\.07\\.04\\.jpg$",
                PatternText = "^2026\\-04\\-02 09\\.07\\.04\\.jpg$",
                PlatformLabel = "Other"
            });

        var draft = service.CreateBuilderDraftFromFilePath("2026-04-02 09.07.04.jpg");
        var rule = service.ApplyBuilderDraft(draft, new FilenameConventionRule());

        Assert.NotNull(draft);
        Assert.NotEmpty(draft.Segments);
        Assert.False(string.IsNullOrWhiteSpace(rule.PatternText));
    }

    [Fact]
    public void CreateBuilderDraftFromFilePath_PhoneYyyyMmDdDotTime_SplitsDateAndClockRows()
    {
        var service = CreateService(
            parseFileName: _ => new FilenameParseResult { PlatformLabel = "Other" },
            buildRuleFromSample: _ => null!);

        var draft = service.CreateBuilderDraftFromFilePath("2026-04-02 09.07.04.jpg");

        Assert.NotNull(draft);
        Assert.Equal("yyyy-MM-dd HH.mm.ss", draft.TimestampFormat);
        Assert.Contains(
            draft.Segments,
            s => s.AssignedRole == FilenameConventionBuilderComponentRole.Timestamp
                && string.Equals(s.Text, "2026-04-02", StringComparison.Ordinal));
        Assert.Contains(
            draft.Segments,
            s => s.AssignedRole == FilenameConventionBuilderComponentRole.ClockTime
                && string.Equals(s.Text, "09.07.04", StringComparison.Ordinal));
        Assert.DoesNotContain(
            draft.Segments,
            s => s.AssignedRole == FilenameConventionBuilderComponentRole.Literal
                && (s.Text ?? string.Empty).Contains("09.07", StringComparison.Ordinal));
        Assert.DoesNotContain(draft.Segments, s => s != null && s.Locked);
    }

    [Fact]
    public void ApplyBuilderDraft_CompositeDateAndClock_RoundTripsPattern()
    {
        var service = CreateService(
            parseFileName: _ => new FilenameParseResult { PlatformLabel = "Other" },
            buildRuleFromSample: _ => null!);

        var draft = service.CreateBuilderDraftFromFilePath("2026-04-02 09.07.04.jpg");
        Assert.NotNull(draft);
        var rule = service.ApplyBuilderDraft(draft, new FilenameConventionRule());

        Assert.Equal("yyyy-MM-dd HH.mm.ss", rule.TimestampFormat);
        Assert.Equal("[yyyy]-[MM]-[dd] [HH].[mm].[ss].[ext:image]", rule.PatternText);

        var fromRule = service.CreateBuilderDraftFromRule(rule);
        Assert.NotNull(fromRule);
        Assert.Contains(fromRule.Segments, s => s.AssignedRole == FilenameConventionBuilderComponentRole.Timestamp);
        Assert.Contains(fromRule.Segments, s => s.AssignedRole == FilenameConventionBuilderComponentRole.ClockTime);
        var rebuilt = service.ApplyBuilderDraft(fromRule, rule);
        Assert.Equal(rule.PatternText, rebuilt.PatternText);
        Assert.Equal(rule.TimestampFormat, rebuilt.TimestampFormat);
    }

    [Fact]
    public void CreateBuilderDraftFromSample_SuggestsNonSteamId_ForLongShortcutPrefix()
    {
        var service = CreateService(
            parseFileName: _ => new FilenameParseResult
            {
                PlatformLabel = "Emulation",
                NonSteamId = "16245548604121415680"
            },
            buildRuleFromSample: sample => new FilenameConventionRule
            {
                ConventionId = "sample_rule",
                Name = "Sample Rule",
                Enabled = true,
                Priority = 1200,
                Pattern = "[appid]_[yyyy][MM][dd][HH][mm][ss][opt-counter].[ext:media]",
                PatternText = "[appid]_[yyyy][MM][dd][HH][mm][ss][opt-counter].[ext:media]",
                PlatformLabel = "Emulation",
                PlatformTagsText = "Emulation",
                SteamAppIdGroup = "appid",
                TimestampGroup = "stamp",
                TimestampFormat = "yyyyMMddHHmmss"
            });

        var draft = service.CreateBuilderDraftFromSample(new FilenameConventionSample
        {
            FileName = "16245548604121415680_20260407183242_1.png",
            SuggestedPlatformLabel = "Emulation"
        });

        Assert.NotNull(draft);
        Assert.Equal("Emulation", draft.PlatformLabel);
        Assert.Contains(draft.Segments, segment => segment.AssignedRole == FilenameConventionBuilderComponentRole.NonSteamId);
        Assert.DoesNotContain(draft.Segments, segment => segment.AssignedRole == FilenameConventionBuilderComponentRole.SteamAppId);
    }

    static FilenameRulesService CreateService(
        Func<string, FilenameParseResult>? parseFileName = null,
        Func<FilenameConventionSample, FilenameConventionRule>? buildRuleFromSample = null)
    {
        return new FilenameRulesService(new FilenameRulesServiceDependencies
        {
            GetConventionRules = _ => new List<FilenameConventionRule>(),
            LoadSamples = (_, _) => new List<FilenameConventionSample>(),
            SaveConventions = (_, _) => { },
            InvalidateRules = _ => { },
            DeleteSamples = (_, _) => { },
            BuildCustomRuleFromSample = buildRuleFromSample ?? (_ => new FilenameConventionRule
            {
                ConventionId = "default_rule",
                Name = "Default Rule",
                Enabled = true,
                Priority = 1200,
                Pattern = "[title].[ext:media]",
                PatternText = "[title].[ext:media]",
                PlatformLabel = "Other",
                PlatformTagsText = string.Empty,
                TitleGroup = "title"
            }),
            ParseFileName = parseFileName ?? (_ => new FilenameParseResult { PlatformLabel = "Other" }),
            ParseTagText = value => (value ?? string.Empty)
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(tag => tag.Trim())
                .Where(tag => !string.IsNullOrWhiteSpace(tag)),
            NormalizeConsoleLabel = value =>
            {
                var normalized = (value ?? string.Empty).Trim();
                return string.IsNullOrWhiteSpace(normalized) ? "Other" : normalized;
            },
            DefaultPlatformTagsTextForLabel = value => string.Equals(value, "Steam", StringComparison.OrdinalIgnoreCase)
                ? "Steam"
                : string.Equals(value, "Emulation", StringComparison.OrdinalIgnoreCase)
                    ? "Emulation"
                    : string.Empty,
            CleanTag = value => (value ?? string.Empty).Trim()
        });
    }
}
