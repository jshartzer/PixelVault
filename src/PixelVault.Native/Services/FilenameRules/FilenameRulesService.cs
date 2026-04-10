using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace PixelVaultNative
{
    sealed class FilenameRulesEditorState
    {
        public List<FilenameConventionRule> CustomRules { get; set; } = new List<FilenameConventionRule>();
        public List<FilenameConventionRule> BuiltInRules { get; set; } = new List<FilenameConventionRule>();
        public List<FilenameConventionSample> Samples { get; set; } = new List<FilenameConventionSample>();
    }

    interface IFilenameRulesService
    {
        FilenameRulesEditorState LoadState(string root);
        FilenameConventionRule CreateNewRule();
        FilenameConventionRule CreateRuleFromSample(FilenameConventionSample sample);
        FilenameConventionBuilderDraft CreateBuilderDraftFromSample(FilenameConventionSample sample);
        FilenameConventionBuilderDraft CreateBuilderDraftFromFilePath(string filePath);
        FilenameConventionBuilderDraft CreateBuilderDraftFromRule(FilenameConventionRule rule);
        FilenameConventionRule ApplyBuilderDraft(FilenameConventionBuilderDraft draft, FilenameConventionRule existingRule);
        FilenameConventionRule EnsureDisabledOverride(FilenameConventionRule builtInRule, IList<FilenameConventionRule> customRules);
        void DismissSamples(string root, IEnumerable<long> sampleIds);
        FilenameRulesEditorState SaveRules(string root, IEnumerable<FilenameConventionRule> customRules);
    }

    sealed class FilenameRulesServiceDependencies
    {
        public Func<string, List<FilenameConventionRule>> GetConventionRules;
        public Func<string, int, List<FilenameConventionSample>> LoadSamples;
        public Action<string, IEnumerable<FilenameConventionRule>> SaveConventions;
        public Action<string> InvalidateRules;
        public Action<string, IEnumerable<long>> DeleteSamples;
        public Func<FilenameConventionSample, FilenameConventionRule> BuildCustomRuleFromSample;
        public Func<string, FilenameParseResult> ParseFileName;
        public Func<string, IEnumerable<string>> ParseTagText;
        public Func<string, string> NormalizeConsoleLabel;
        public Func<string, string> DefaultPlatformTagsTextForLabel;
        public Func<string, string> CleanTag;
    }

    sealed class FilenameRulesService : IFilenameRulesService
    {
        readonly FilenameRulesServiceDependencies dependencies;

        public FilenameRulesService(FilenameRulesServiceDependencies dependencies)
        {
            this.dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
        }

        public FilenameRulesEditorState LoadState(string root)
        {
            var allRules = (dependencies.GetConventionRules == null ? new List<FilenameConventionRule>() : dependencies.GetConventionRules(root) ?? new List<FilenameConventionRule>())
                .Where(rule => rule != null)
                .Select(CloneRule)
                .ToList();

            return new FilenameRulesEditorState
            {
                CustomRules = allRules
                    .Where(rule => !rule.IsBuiltIn)
                    .OrderByDescending(rule => rule.Priority)
                    .ThenBy(rule => rule.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                BuiltInRules = allRules
                    .Where(rule => rule.IsBuiltIn)
                    .OrderByDescending(rule => rule.Priority)
                    .ThenBy(rule => rule.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                Samples = (dependencies.LoadSamples == null ? new List<FilenameConventionSample>() : dependencies.LoadSamples(root, 80) ?? new List<FilenameConventionSample>())
                    .Where(sample => sample != null)
                    .OrderByDescending(sample => sample.LastSeenUtcTicks)
                    .ThenByDescending(sample => sample.OccurrenceCount)
                    .ToList()
            };
        }

        public FilenameConventionRule CreateNewRule()
        {
            return new FilenameConventionRule
            {
                ConventionId = "custom_" + Guid.NewGuid().ToString("N").Substring(0, 10),
                Name = "Custom Rule",
                Enabled = true,
                Priority = 1200,
                Pattern = "[title].[ext:media]",
                PatternText = "[title].[ext:media]",
                PlatformLabel = "Other",
                PlatformTagsText = string.Empty,
                ConfidenceLabel = "CustomRule",
                IsBuiltIn = false
            };
        }

        public FilenameConventionRule CreateRuleFromSample(FilenameConventionSample sample)
        {
            if (sample == null || dependencies.BuildCustomRuleFromSample == null) return null;
            var candidate = dependencies.BuildCustomRuleFromSample(sample);
            if (candidate == null || string.IsNullOrWhiteSpace(candidate.PatternText ?? candidate.Pattern)) return null;

            candidate.PatternText = FilenameParserService.GetPatternEditorText(candidate.PatternText ?? candidate.Pattern);
            candidate.Pattern = candidate.PatternText;
            candidate.PlatformLabel = NormalizeConsoleLabel(string.IsNullOrWhiteSpace(candidate.PlatformLabel) ? sample.SuggestedPlatformLabel : candidate.PlatformLabel);
            if (string.IsNullOrWhiteSpace(candidate.PlatformTagsText))
            {
                candidate.PlatformTagsText = DefaultPlatformTagsTextForLabel(candidate.PlatformLabel);
            }
            candidate.ConfidenceLabel = string.IsNullOrWhiteSpace(candidate.ConfidenceLabel) ? "CustomRule" : candidate.ConfidenceLabel;
            candidate.IsBuiltIn = false;
            if (string.IsNullOrWhiteSpace(candidate.ConventionId))
            {
                candidate.ConventionId = "custom_" + Guid.NewGuid().ToString("N").Substring(0, 10);
            }
            return candidate;
        }

        public FilenameConventionBuilderDraft CreateBuilderDraftFromSample(FilenameConventionSample sample)
        {
            if (sample == null) return null;
            var fileName = Path.GetFileName(sample.FileName ?? string.Empty);
            var parsed = ParseFileName(fileName);
            var candidate = dependencies.BuildCustomRuleFromSample == null ? null : dependencies.BuildCustomRuleFromSample(sample);
            return FilenameConventionBuilder.CreateDraftFromFileName(
                fileName,
                parsed,
                candidate,
                sample.SuggestedPlatformLabel,
                NormalizeConsoleLabel,
                DefaultPlatformTagsTextForLabel);
        }

        public FilenameConventionBuilderDraft CreateBuilderDraftFromFilePath(string filePath)
        {
            var fileName = Path.GetFileName(filePath ?? string.Empty);
            if (string.IsNullOrWhiteSpace(fileName)) return null;
            var parsed = ParseFileName(fileName);
            var sample = new FilenameConventionSample
            {
                FileName = fileName,
                SuggestedPlatformLabel = parsed.PlatformLabel
            };
            var candidate = dependencies.BuildCustomRuleFromSample == null ? null : dependencies.BuildCustomRuleFromSample(sample);
            return FilenameConventionBuilder.CreateDraftFromFileName(
                fileName,
                parsed,
                candidate,
                parsed.PlatformLabel,
                NormalizeConsoleLabel,
                DefaultPlatformTagsTextForLabel);
        }

        public FilenameConventionBuilderDraft CreateBuilderDraftFromRule(FilenameConventionRule rule)
        {
            return FilenameConventionBuilder.TryCreateDraftFromRule(
                CloneRule(rule),
                NormalizeConsoleLabel,
                DefaultPlatformTagsTextForLabel);
        }

        public FilenameConventionRule ApplyBuilderDraft(FilenameConventionBuilderDraft draft, FilenameConventionRule existingRule)
        {
            return FilenameConventionBuilder.BuildRuleFromDraft(
                draft,
                existingRule,
                CleanTag,
                NormalizeConsoleLabel,
                DefaultPlatformTagsTextForLabel);
        }

        public FilenameConventionRule EnsureDisabledOverride(FilenameConventionRule builtInRule, IList<FilenameConventionRule> customRules)
        {
            if (builtInRule == null || customRules == null) return null;
            var existing = customRules.FirstOrDefault(rule => string.Equals(rule.ConventionId, builtInRule.ConventionId, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                existing = CloneRule(builtInRule);
                existing.IsBuiltIn = false;
                customRules.Insert(0, existing);
            }

            existing.Enabled = false;
            existing.ConfidenceLabel = "CustomOverride";
            return existing;
        }

        public FilenameRulesEditorState SaveRules(string root, IEnumerable<FilenameConventionRule> customRules)
        {
            var normalized = (customRules ?? Enumerable.Empty<FilenameConventionRule>())
                .Where(rule => rule != null)
                .Select(NormalizeRuleForSave)
                .GroupBy(rule => rule.ConventionId ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Last())
                .OrderByDescending(rule => rule.Priority)
                .ThenBy(rule => rule.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (dependencies.SaveConventions != null) dependencies.SaveConventions(root, normalized);
            if (dependencies.InvalidateRules != null) dependencies.InvalidateRules(root);
            return LoadState(root);
        }

        public void DismissSamples(string root, IEnumerable<long> sampleIds)
        {
            if (dependencies.DeleteSamples != null) dependencies.DeleteSamples(root, sampleIds ?? Enumerable.Empty<long>());
        }

        FilenameConventionRule NormalizeRuleForSave(FilenameConventionRule source)
        {
            var rule = CloneRule(source);
            rule.ConventionId = CleanTag(rule.ConventionId);
            if (string.IsNullOrWhiteSpace(rule.ConventionId)) rule.ConventionId = "custom_" + Guid.NewGuid().ToString("N").Substring(0, 10);

            rule.Name = CleanTag(rule.Name);
            if (string.IsNullOrWhiteSpace(rule.Name)) throw new InvalidOperationException("Rule name is required.");

            rule.PatternText = FilenameParserService.NormalizePatternTextForStorage(rule.PatternText ?? rule.Pattern);
            if (string.IsNullOrWhiteSpace(rule.PatternText)) throw new InvalidOperationException("Rule pattern is required.");

            rule.Priority = Math.Max(-100000, Math.Min(100000, rule.Priority));
            rule.PlatformLabel = NormalizeConsoleLabel(string.IsNullOrWhiteSpace(rule.PlatformLabel) ? "Other" : rule.PlatformLabel);
            rule.PlatformTagsText = string.Join("; ", ParseTagText(string.IsNullOrWhiteSpace(rule.PlatformTagsText) ? DefaultPlatformTagsTextForLabel(rule.PlatformLabel) : rule.PlatformTagsText));
            rule.SteamAppIdGroup = CleanTag(rule.SteamAppIdGroup);
            rule.TitleGroup = CleanTag(rule.TitleGroup);
            rule.TimestampGroup = CleanTag(rule.TimestampGroup);
            rule.TimestampFormat = CleanTag(rule.TimestampFormat);
            rule.Pattern = rule.PatternText;
            rule.ConfidenceLabel = CleanTag(string.IsNullOrWhiteSpace(rule.ConfidenceLabel) ? "CustomRule" : rule.ConfidenceLabel);
            rule.IsBuiltIn = false;

            _ = new Regex(FilenameParserService.BuildRegexPattern(rule.PatternText, rule.TimestampGroup), RegexOptions.IgnoreCase);
            if (UsesTimestampTokens(rule.PatternText) && string.IsNullOrWhiteSpace(rule.TimestampFormat))
            {
                throw new InvalidOperationException("Timestamp format is required when a rule captures a filename timestamp.");
            }

            return rule;
        }

        static FilenameConventionRule CloneRule(FilenameConventionRule rule)
        {
            if (rule == null) return null;
            return new FilenameConventionRule
            {
                ConventionId = rule.ConventionId,
                Name = rule.Name,
                Enabled = rule.Enabled,
                Priority = rule.Priority,
                Pattern = rule.Pattern,
                PatternText = FilenameParserService.GetPatternEditorText(rule.PatternText ?? rule.Pattern),
                PlatformLabel = rule.PlatformLabel,
                PlatformTagsText = rule.PlatformTagsText,
                SteamAppIdGroup = rule.SteamAppIdGroup,
                TitleGroup = rule.TitleGroup,
                TimestampGroup = rule.TimestampGroup,
                TimestampFormat = rule.TimestampFormat,
                PreserveFileTimes = rule.PreserveFileTimes,
                RoutesToManualWhenMissingSteamAppId = rule.RoutesToManualWhenMissingSteamAppId,
                ConfidenceLabel = rule.ConfidenceLabel,
                IsBuiltIn = rule.IsBuiltIn
            };
        }

        bool UsesTimestampTokens(string patternText)
        {
            var value = patternText ?? string.Empty;
            return value.Contains("[M]")
                || value.Contains("[d]")
                || value.Contains("[h]")
                || value.Contains("[yyyy]")
                || value.Contains("[MM]")
                || value.Contains("[dd]")
                || value.Contains("[HH]")
                || value.Contains("[hh]")
                || value.Contains("[mm]")
                || value.Contains("[ss]")
                || value.Contains("[tt]")
                || value.Contains("[unixms]");
        }

        IEnumerable<string> ParseTagText(string value)
        {
            return dependencies.ParseTagText == null ? Enumerable.Empty<string>() : dependencies.ParseTagText(value ?? string.Empty) ?? Enumerable.Empty<string>();
        }

        FilenameParseResult ParseFileName(string value)
        {
            if (dependencies.ParseFileName == null) return new FilenameParseResult();
            return dependencies.ParseFileName(value ?? string.Empty) ?? new FilenameParseResult();
        }

        string NormalizeConsoleLabel(string value)
        {
            return dependencies.NormalizeConsoleLabel == null ? (value ?? string.Empty) : dependencies.NormalizeConsoleLabel(value ?? string.Empty);
        }

        string DefaultPlatformTagsTextForLabel(string value)
        {
            return dependencies.DefaultPlatformTagsTextForLabel == null ? string.Empty : dependencies.DefaultPlatformTagsTextForLabel(value ?? string.Empty) ?? string.Empty;
        }

        string CleanTag(string value)
        {
            return dependencies.CleanTag == null ? (value ?? string.Empty) : dependencies.CleanTag(value ?? string.Empty);
        }
    }
}
