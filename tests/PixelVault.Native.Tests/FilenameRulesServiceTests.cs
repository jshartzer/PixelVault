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
