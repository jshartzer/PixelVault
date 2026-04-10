using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace PixelVaultNative
{
    interface IFilenameParserService
    {
        List<FilenameConventionRule> GetConventionRules(string root);
        void InvalidateRules(string root);
        FilenameParseResult Parse(string file, string root);
        string GetGameTitleHint(string baseName, string root);
    }

    sealed class FilenameParserServiceDependencies
    {
        public Func<string, List<FilenameConventionRule>> LoadCustomConventions;
        public Func<string, List<GameIndexEditorRow>> LoadSavedGameIndexRows;
        public Func<string, string> NormalizeGameIndexName;
        public Func<string, IEnumerable<string>> ParseTagText;
        public Func<string, bool> IsVideo;
        public Func<string, string> NormalizeConsoleLabel;
    }

    sealed class FilenameParserService : IFilenameParserService
    {
        readonly FilenameParserServiceDependencies dependencies;
        readonly Dictionary<string, List<FilenameConventionRule>> ruleCache = new Dictionary<string, List<FilenameConventionRule>>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, Dictionary<string, KnownSteamRenameLookupEntry>> knownSteamRenameCache = new Dictionary<string, Dictionary<string, KnownSteamRenameLookupEntry>>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, Dictionary<string, KnownNonSteamLookupEntry>> knownNonSteamIdCache = new Dictionary<string, Dictionary<string, KnownNonSteamLookupEntry>>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, Regex> regexCache = new Dictionary<string, Regex>(StringComparer.Ordinal);
        readonly object sync = new object();

        public FilenameParserService(FilenameParserServiceDependencies dependencies)
        {
            this.dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
        }

        public FilenameParseResult Parse(string file, string root)
        {
            var fileName = Path.GetFileName(file ?? string.Empty);
            var baseName = Path.GetFileNameWithoutExtension(fileName ?? string.Empty);
            var result = new FilenameParseResult();
            foreach (var rule in GetRules(root))
            {
                var storedPattern = NormalizePatternTextForStorage(rule.PatternText ?? rule.Pattern);
                if (!rule.Enabled || string.IsNullOrWhiteSpace(storedPattern)) continue;
                var match = GetRegex(storedPattern, rule.TimestampGroup).Match(fileName);
                if (!match.Success) continue;
                result.MatchedConvention = true;
                result.ConventionId = rule.ConventionId ?? string.Empty;
                result.ConventionName = string.IsNullOrWhiteSpace(rule.Name) ? (rule.ConventionId ?? string.Empty) : rule.Name;
                result.ConfidenceLabel = string.IsNullOrWhiteSpace(rule.ConfidenceLabel) ? "ExplicitPattern" : rule.ConfidenceLabel;
                result.PlatformTags = ParseTagText(rule.PlatformTagsText);
                result.PlatformLabel = ResolvePrimaryPlatformLabel(rule.PlatformLabel, result.PlatformTags);
                ApplyMatchedPrimaryId(result, rule, ReadGroup(match, rule.SteamAppIdGroup));
                result.GameTitleHint = ReadGroup(match, rule.TitleGroup);
                result.CaptureTime = ParseTimestamp(ReadGroup(match, rule.TimestampGroup), rule.TimestampFormat);
                result.PreserveFileTimes = rule.PreserveFileTimes;
                result.RoutesToManualWhenMissingSteamAppId = rule.RoutesToManualWhenMissingSteamAppId;
                break;
            }

            ApplyXboxPcTrailingTimestampParse(result, fileName);

            if (result.CaptureTime == null)
            {
                result.CaptureTime = ParseGenericCaptureDate(fileName);
            }

            if (string.IsNullOrWhiteSpace(result.GameTitleHint))
            {
                result.GameTitleHint = GetGameTitleHint(baseName, root);
            }

            ApplyKnownSteamRenameFallback(result, fileName, root);
            ApplyNonSteamShortcutFallback(result, fileName, root);

            if (string.IsNullOrWhiteSpace(result.PlatformLabel) || string.Equals(result.PlatformLabel, "Other", StringComparison.OrdinalIgnoreCase))
            {
                result.PlatformLabel = ResolvePrimaryPlatformLabel(result.PlatformLabel, result.PlatformTags);
            }

            if (!result.MatchedConvention)
            {
                result.ConventionId = result.CaptureTime.HasValue ? "generic-date-match" : "unknown";
                result.ConventionName = result.CaptureTime.HasValue ? "Generic Date Match" : "Unknown";
                result.ConfidenceLabel = result.CaptureTime.HasValue ? "Heuristic" : string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(result.GameTitleHint))
                result.GameTitleHint = NormalizeColonStandinUnderscoresForGameTitle(result.GameTitleHint);

            return result;
        }

        /// <summary>
        /// Windows paths cannot contain ':'; Xbox (and similar) exports often use "_ " in place of ": " in titles.
        /// Normalize so the same game merges under one index/browser identity (e.g. "The Witcher 3_ Wild Hunt" → "The Witcher 3: Wild Hunt").
        /// </summary>
        public static string NormalizeColonStandinUnderscoresForGameTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return title ?? string.Empty;
            return Regex.Replace(title.Trim(), @"([\p{L}\p{N}])_ ", "$1: ", RegexOptions.CultureInvariant);
        }

        void ApplyXboxPcTrailingTimestampParse(FilenameParseResult result, string fileName)
        {
            if (result == null) return;
            if (result.MatchedConvention && !string.Equals(result.ConventionId, "xbox_pc_capture_ampm", StringComparison.OrdinalIgnoreCase)) return;

            string title;
            DateTime captureTime;
            if (!TryParseXboxPcCaptureFromTrailingTimestamp(fileName, out title, out captureTime)) return;

            result.MatchedConvention = true;
            result.ConventionId = "xbox_pc_capture_ampm";
            result.ConventionName = "Xbox PC Capture (Windows)";
            result.ConfidenceLabel = "ExplicitPattern";
            result.PlatformTags = ParseTagText("Platform:Xbox PC");
            result.PlatformLabel = ResolvePrimaryPlatformLabel("Xbox PC", result.PlatformTags);
            result.GameTitleHint = title;
            result.CaptureTime = captureTime;
            result.PreserveFileTimes = true;
        }

        public List<FilenameConventionRule> GetConventionRules(string root)
        {
            return GetRules(root)
                .Select(rule => new FilenameConventionRule
                {
                    ConventionId = rule.ConventionId,
                    Name = rule.Name,
                    Enabled = rule.Enabled,
                    Priority = rule.Priority,
                    Pattern = rule.Pattern,
                    PatternText = GetPatternEditorText(rule.PatternText ?? rule.Pattern),
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
                })
                .ToList();
        }

        public void InvalidateRules(string root)
        {
            var cacheKey = root ?? string.Empty;
            lock (sync)
            {
                ruleCache.Remove(cacheKey);
                knownSteamRenameCache.Remove(cacheKey);
                knownNonSteamIdCache.Remove(cacheKey);
            }
        }

        public string GetGameTitleHint(string baseName, string root)
        {
            var cleanedBaseName = baseName ?? string.Empty;
            if (string.IsNullOrWhiteSpace(cleanedBaseName)) return string.Empty;

            string xboxPcTitle;
            DateTime _;
            if (TryParseXboxPcCaptureFromTrailingTimestamp(cleanedBaseName, out xboxPcTitle, out _))
            {
                return NormalizeColonStandinUnderscoresForGameTitle(xboxPcTitle);
            }

            var match = Regex.Match(cleanedBaseName, "^(?<game>.+?)_(?<ts>\\d{14})(?:[_-]\\d+)?$");
            if (match.Success) return NormalizeColonStandinUnderscoresForGameTitle(match.Groups["game"].Value);

            match = Regex.Match(cleanedBaseName, "^(?<game>.+?)_(?<ts>\\d{8,})(?:[_-]\\d+)?$");
            if (match.Success) return NormalizeColonStandinUnderscoresForGameTitle(match.Groups["game"].Value);

            match = Regex.Match(cleanedBaseName, "^(?<game>.+?)-(?<year>20\\d{2})[_-](?<mon>\\d{2})[_-](?<day>\\d{2}).*$");
            if (match.Success) return NormalizeColonStandinUnderscoresForGameTitle(match.Groups["game"].Value);

            if (cleanedBaseName.Contains("_")) return NormalizeColonStandinUnderscoresForGameTitle(cleanedBaseName.Split('_')[0]);

            match = Regex.Match(cleanedBaseName, "^(?<game>.+?)-20\\d{2}.*$");
            if (match.Success) return NormalizeColonStandinUnderscoresForGameTitle(match.Groups["game"].Value);

            return NormalizeColonStandinUnderscoresForGameTitle(cleanedBaseName);
        }

        List<FilenameConventionRule> GetRules(string root)
        {
            var cacheKey = root ?? string.Empty;
            lock (sync)
            {
                List<FilenameConventionRule> cached;
                if (ruleCache.TryGetValue(cacheKey, out cached)) return cached;
                var mergedById = new Dictionary<string, FilenameConventionRule>(StringComparer.OrdinalIgnoreCase);
                var merged = new List<FilenameConventionRule>();
                foreach (var builtIn in GetBuiltInRules())
                {
                    if (builtIn == null) continue;
                    if (!string.IsNullOrWhiteSpace(builtIn.ConventionId)) mergedById[builtIn.ConventionId] = builtIn;
                    else merged.Add(builtIn);
                }
                if (!string.IsNullOrWhiteSpace(root) && dependencies.LoadCustomConventions != null)
                {
                    var customRules = dependencies.LoadCustomConventions(root) ?? new List<FilenameConventionRule>();
                    foreach (var customRule in customRules.Where(rule => rule != null))
                    {
                        if (!string.IsNullOrWhiteSpace(customRule.ConventionId))
                        {
                            mergedById[customRule.ConventionId] = customRule;
                        }
                        else
                        {
                            merged.Add(customRule);
                        }
                    }
                }
                merged.AddRange(mergedById.Values);
                cached = merged
                    .Where(rule => rule != null)
                    .OrderByDescending(rule => rule.Priority)
                    .ThenBy(rule => rule.ConventionId ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                ruleCache[cacheKey] = cached;
                return cached;
            }
        }

        Regex GetRegex(string patternText, string timestampGroup)
        {
            var compiledPattern = BuildRegexPattern(patternText, timestampGroup);
            lock (sync)
            {
                Regex cached;
                if (regexCache.TryGetValue(compiledPattern, out cached)) return cached;
                cached = new Regex(compiledPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
                regexCache[compiledPattern] = cached;
                return cached;
            }
        }

        List<FilenameConventionRule> GetBuiltInRules()
        {
            return new List<FilenameConventionRule>
            {
                new FilenameConventionRule
                {
                    ConventionId = "steam_screenshot_appid",
                    Name = "Steam Screenshot (AppID + Timestamp)",
                    Priority = 1000,
                    Pattern = "[appid]_[yyyy][MM][dd][HH][mm][ss][opt-counter].[ext:media]",
                    PatternText = "[appid]_[yyyy][MM][dd][HH][mm][ss][opt-counter].[ext:media]",
                    PlatformLabel = "Steam",
                    PlatformTagsText = "Steam",
                    SteamAppIdGroup = "appid",
                    TimestampGroup = "stamp",
                    TimestampFormat = "yyyyMMddHHmmss",
                    ConfidenceLabel = "ExplicitPattern",
                    IsBuiltIn = true
                },
                new FilenameConventionRule
                {
                    ConventionId = "steam_manual_export",
                    Name = "Steam Manual Export",
                    Priority = 950,
                    Pattern = "[yyyy][MM][dd][HH][mm][ss][opt-counter].[ext:media]",
                    PatternText = "[yyyy][MM][dd][HH][mm][ss][opt-counter].[ext:media]",
                    PlatformLabel = "Steam",
                    PlatformTagsText = "Steam",
                    TimestampGroup = "stamp",
                    TimestampFormat = "yyyyMMddHHmmss",
                    RoutesToManualWhenMissingSteamAppId = true,
                    ConfidenceLabel = "ExplicitPattern",
                    IsBuiltIn = true
                },
                new FilenameConventionRule
                {
                    ConventionId = "steam_legacy_date",
                    Name = "Steam Screenshot (Legacy Date)",
                    Priority = 900,
                    Pattern = "[title]_[yyyy]-[MM]-[dd]_[counter].[ext:media]",
                    PatternText = "[title]_[yyyy]-[MM]-[dd]_[counter].[ext:media]",
                    PlatformLabel = "Steam",
                    PlatformTagsText = "Steam",
                    TitleGroup = "title",
                    TimestampGroup = "stamp",
                    TimestampFormat = "yyyy-MM-dd",
                    ConfidenceLabel = "ExplicitPattern",
                    IsBuiltIn = true
                },
                new FilenameConventionRule
                {
                    ConventionId = "steam_renamed_title_timestamp",
                    Name = "Steam Screenshot (Renamed Title + Timestamp)",
                    Priority = 895,
                    Pattern = "[title]_[yyyy][MM][dd][HH][mm][ss]_[counter].[ext:media]",
                    PatternText = "[title]_[yyyy][MM][dd][HH][mm][ss]_[counter].[ext:media]",
                    PlatformLabel = "Steam",
                    PlatformTagsText = "Steam",
                    TitleGroup = "title",
                    TimestampGroup = "stamp",
                    TimestampFormat = "yyyyMMddHHmmss",
                    ConfidenceLabel = "Heuristic",
                    IsBuiltIn = true
                },
                new FilenameConventionRule
                {
                    ConventionId = "steam_clip_unix",
                    Name = "Steam Clip",
                    Priority = 890,
                    Pattern = "clip_[unixms].[ext:video]",
                    PatternText = "clip_[unixms].[ext:video]",
                    PlatformLabel = "Steam",
                    PlatformTagsText = "Steam",
                    TimestampGroup = "stamp",
                    TimestampFormat = "unix-ms",
                    ConfidenceLabel = "ExplicitPattern",
                    IsBuiltIn = true
                },
                new FilenameConventionRule
                {
                    ConventionId = "ps5_share",
                    Name = "PS5 Share",
                    Priority = 850,
                    Pattern = "[title]_[yyyy][MM][dd][HH][mm][ss].[ext:media]",
                    PatternText = "[title]_[yyyy][MM][dd][HH][mm][ss].[ext:media]",
                    PlatformLabel = "PS5",
                    PlatformTagsText = "PS5;PlayStation",
                    TitleGroup = "title",
                    TimestampGroup = "stamp",
                    TimestampFormat = "yyyyMMddHHmmss",
                    ConfidenceLabel = "ExplicitPattern",
                    IsBuiltIn = true
                },
                new FilenameConventionRule
                {
                    ConventionId = "ps5_share_segmented_fractional",
                    Name = "PS5 Share (Segmented/Fractional)",
                    Priority = 848,
                    Pattern = @"^(?<title>.+?)(?:_[^_]+)*_(?<stamp>\d{14})(?:\d{2})?\.(png|jpe?g|mp4|mkv|avi|mov|wmv|webm)$",
                    PatternText = @"^(?<title>.+?)(?:_[^_]+)*_(?<stamp>\d{14})(?:\d{2})?\.(png|jpe?g|mp4|mkv|avi|mov|wmv|webm)$",
                    PlatformLabel = "PS5",
                    PlatformTagsText = "PS5;PlayStation",
                    TitleGroup = "title",
                    TimestampGroup = "stamp",
                    TimestampFormat = "yyyyMMddHHmmss",
                    ConfidenceLabel = "ExplicitPattern",
                    IsBuiltIn = true
                },
                new FilenameConventionRule
                {
                    ConventionId = "xbox_capture",
                    Name = "Xbox Capture",
                    Priority = 840,
                    Pattern = "[title]-[yyyy]_[MM]_[dd]-[HH]_[mm]_[ss].[ext:media]",
                    PatternText = "[title]-[yyyy]_[MM]_[dd]-[HH]_[mm]_[ss].[ext:media]",
                    PlatformLabel = "Xbox",
                    PlatformTagsText = "Xbox",
                    TitleGroup = "title",
                    TimestampGroup = "stamp",
                    TimestampFormat = "yyyy_MM_dd-HH_mm_ss",
                    PreserveFileTimes = false,
                    ConfidenceLabel = "ExplicitPattern",
                    IsBuiltIn = true
                },
                new FilenameConventionRule
                {
                    ConventionId = "xbox_capture_hyphen_time",
                    Name = "Xbox Capture (Hyphen Time)",
                    Priority = 838,
                    Pattern = "[title]-[yyyy]_[MM]_[dd]-[HH]-[mm]-[ss].[ext:media]",
                    PatternText = "[title]-[yyyy]_[MM]_[dd]-[HH]-[mm]-[ss].[ext:media]",
                    PlatformLabel = "Xbox",
                    PlatformTagsText = "Xbox",
                    TitleGroup = "title",
                    TimestampGroup = "stamp",
                    TimestampFormat = "yyyy_MM_dd-HH-mm-ss",
                    PreserveFileTimes = false,
                    ConfidenceLabel = "ExplicitPattern",
                    IsBuiltIn = true
                },
                new FilenameConventionRule
                {
                    ConventionId = "xbox_pc_capture_ampm",
                    Name = "Xbox PC Capture (Windows)",
                    Priority = 836,
                    Pattern = @"^(?<title>.+?)\s+(?<stamp>\d{1,2}_\d{1,2}_\d{4}\s+\d{1,2}_\d{2}_\d{2}\s+[AP]M)\.(png|jpe?g|mp4|mkv|avi|mov|wmv|webm)$",
                    PatternText = @"^(?<title>.+?)\s+(?<stamp>\d{1,2}_\d{1,2}_\d{4}\s+\d{1,2}_\d{2}_\d{2}\s+[AP]M)\.(png|jpe?g|mp4|mkv|avi|mov|wmv|webm)$",
                    PlatformLabel = "Xbox PC",
                    PlatformTagsText = "Platform:Xbox PC",
                    TitleGroup = "title",
                    TimestampGroup = "stamp",
                    TimestampFormat = "M_d_yyyy h_mm_ss tt",
                    PreserveFileTimes = true,
                    ConfidenceLabel = "ExplicitPattern",
                    IsBuiltIn = true
                },
                new FilenameConventionRule
                {
                    ConventionId = "ps5_literal_token",
                    Name = "PS5 Token",
                    Priority = 120,
                    Pattern = "[contains:PS5]",
                    PatternText = "[contains:PS5]",
                    PlatformLabel = "PS5",
                    PlatformTagsText = "PS5;PlayStation",
                    ConfidenceLabel = "Heuristic",
                    IsBuiltIn = true
                },
                new FilenameConventionRule
                {
                    ConventionId = "playstation_literal_token",
                    Name = "PlayStation Token",
                    Priority = 110,
                    Pattern = "[contains:PlayStation]",
                    PatternText = "[contains:PlayStation]",
                    PlatformLabel = "PlayStation",
                    PlatformTagsText = "PlayStation",
                    ConfidenceLabel = "Heuristic",
                    IsBuiltIn = true
                }
            };
        }

        string[] ParseTagText(string value)
        {
            if (dependencies.ParseTagText == null) return new string[0];
            return (dependencies.ParseTagText(value ?? string.Empty) ?? Enumerable.Empty<string>())
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        string ResolvePrimaryPlatformLabel(string explicitLabel, string[] tags)
        {
            var normalizedExplicit = NormalizeConsoleLabel(explicitLabel);
            if (!string.IsNullOrWhiteSpace(normalizedExplicit) && !string.Equals(normalizedExplicit, "Other", StringComparison.OrdinalIgnoreCase))
            {
                return normalizedExplicit;
            }
            foreach (var tag in tags ?? new string[0])
            {
                var normalized = NormalizeConsoleLabel(tag);
                if (string.Equals(normalized, "Xbox PC", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(normalized, "Xbox", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(normalized, "Steam", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(normalized, "Emulation", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(normalized, "PS5", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(normalized, "PlayStation", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(normalized, "PC", StringComparison.OrdinalIgnoreCase))
                {
                    return normalized;
                }
            }
            return string.IsNullOrWhiteSpace(normalizedExplicit) ? "Other" : normalizedExplicit;
        }

        void ApplyKnownSteamRenameFallback(FilenameParseResult result, string fileName, string root)
        {
            if (result == null) return;
            if (string.IsNullOrWhiteSpace(root) || !result.CaptureTime.HasValue) return;
            if (!LooksLikeRenamedSteamScreenshot(fileName)) return;

            var resolvedPlatform = ResolvePrimaryPlatformLabel(result.PlatformLabel, result.PlatformTags);
            var needsPlatformResolution = string.Equals(resolvedPlatform, "Other", StringComparison.OrdinalIgnoreCase);
            var needsAppIdResolution = string.Equals(resolvedPlatform, "Steam", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(result.SteamAppId);
            if (!needsPlatformResolution && !needsAppIdResolution) return;

            var normalizedTitle = NormalizeGameIndexName(result.GameTitleHint);
            if (string.IsNullOrWhiteSpace(normalizedTitle)) return;

            var knownSteamRows = GetKnownSteamRenameLookup(root);
            KnownSteamRenameLookupEntry lookupEntry;
            if (!knownSteamRows.TryGetValue(normalizedTitle, out lookupEntry)) return;
            if (lookupEntry.HasConflictingPlatform || !lookupEntry.HasSteamMatch) return;

            if (needsPlatformResolution)
            {
                result.MatchedConvention = true;
                result.ConventionId = "steam_known_title_timestamp";
                result.ConventionName = "Steam Screenshot (Known Title + Timestamp)";
                result.ConfidenceLabel = "KnownGameIndex";
                result.PlatformLabel = "Steam";
                result.PlatformTags = MergePlatformTags(result.PlatformTags, "Steam");
            }

            if (needsAppIdResolution && string.IsNullOrWhiteSpace(result.SteamAppId))
            {
                result.SteamAppId = lookupEntry.SteamAppId ?? string.Empty;
            }
        }

        void ApplyNonSteamShortcutFallback(FilenameParseResult result, string fileName, string root)
        {
            if (result == null) return;
            if (!string.Equals(result.ConventionId, "steam_screenshot_appid", StringComparison.OrdinalIgnoreCase)) return;

            var candidateId = CleanNumericId(result.SteamAppId);
            if (!LooksLikeNonSteamShortcutId(candidateId)) return;

            result.NonSteamId = candidateId;
            result.SteamAppId = string.Empty;
            result.PlatformLabel = "Emulation";
            result.PlatformTags = new[] { "Emulation" };
            result.ConventionId = "steam_screenshot_nonsteam_id";
            result.ConventionName = "Steam Screenshot (Non-Steam Shortcut)";
            result.ConfidenceLabel = "Heuristic";

            if (!string.IsNullOrWhiteSpace(root))
            {
                KnownNonSteamLookupEntry lookupEntry;
                if (GetKnownNonSteamLookup(root).TryGetValue(candidateId, out lookupEntry) && !string.IsNullOrWhiteSpace(lookupEntry.Name))
                {
                    result.GameTitleHint = lookupEntry.Name ?? string.Empty;
                    result.RoutesToManualWhenMissingSteamAppId = false;
                    return;
                }
            }

            if (string.Equals(CleanTag(result.GameTitleHint), candidateId, StringComparison.Ordinal))
            {
                result.GameTitleHint = string.Empty;
            }

            // Keep unknown shortcut IDs in the manual flow so the user can name the game once,
            // while we still preserve the shortcut ID for the new master row.
            result.RoutesToManualWhenMissingSteamAppId = true;
        }

        Dictionary<string, KnownSteamRenameLookupEntry> GetKnownSteamRenameLookup(string root)
        {
            var cacheKey = root ?? string.Empty;
            lock (sync)
            {
                Dictionary<string, KnownSteamRenameLookupEntry> cached;
                if (knownSteamRenameCache.TryGetValue(cacheKey, out cached)) return cached;
            }

            var lookup = new Dictionary<string, KnownSteamRenameLookupEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in LoadSavedGameIndexRows(root).Where(row => row != null))
            {
                var normalizedTitle = NormalizeGameIndexName(row.Name);
                if (string.IsNullOrWhiteSpace(normalizedTitle)) continue;

                KnownSteamRenameLookupEntry existing;
                lookup.TryGetValue(normalizedTitle, out existing);
                var updated = existing;
                if (IsKnownSteamRow(row))
                {
                    updated.HasSteamMatch = true;
                    if (string.IsNullOrWhiteSpace(updated.SteamAppId) && !string.IsNullOrWhiteSpace(row.SteamAppId))
                    {
                        updated.SteamAppId = row.SteamAppId ?? string.Empty;
                    }
                }
                else if (!string.Equals(NormalizeConsoleLabel(row.PlatformLabel), "Other", StringComparison.OrdinalIgnoreCase))
                {
                    updated.HasConflictingPlatform = true;
                }
                lookup[normalizedTitle] = updated;
            }

            lock (sync)
            {
                knownSteamRenameCache[cacheKey] = lookup;
                return lookup;
            }
        }

        Dictionary<string, KnownNonSteamLookupEntry> GetKnownNonSteamLookup(string root)
        {
            var cacheKey = root ?? string.Empty;
            lock (sync)
            {
                Dictionary<string, KnownNonSteamLookupEntry> cached;
                if (knownNonSteamIdCache.TryGetValue(cacheKey, out cached)) return cached;
            }

            var lookup = new Dictionary<string, KnownNonSteamLookupEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in LoadSavedGameIndexRows(root).Where(row => row != null && !string.IsNullOrWhiteSpace(row.NonSteamId)))
            {
                var nonSteamId = CleanNumericId(row.NonSteamId);
                if (string.IsNullOrWhiteSpace(nonSteamId)) continue;
                if (lookup.ContainsKey(nonSteamId)) continue;

                lookup[nonSteamId] = new KnownNonSteamLookupEntry
                {
                    Name = NormalizeGameIndexName(row.Name)
                };
            }

            lock (sync)
            {
                knownNonSteamIdCache[cacheKey] = lookup;
                return lookup;
            }
        }

        List<GameIndexEditorRow> LoadSavedGameIndexRows(string root)
        {
            if (dependencies.LoadSavedGameIndexRows == null) return new List<GameIndexEditorRow>();
            return dependencies.LoadSavedGameIndexRows(root ?? string.Empty) ?? new List<GameIndexEditorRow>();
        }

        string NormalizeGameIndexName(string value)
        {
            if (dependencies.NormalizeGameIndexName != null) return dependencies.NormalizeGameIndexName(value ?? string.Empty);
            return Regex.Replace((value ?? string.Empty).Trim(), "\\s+", " ");
        }

        bool IsKnownSteamRow(GameIndexEditorRow row)
        {
            if (row == null) return false;
            return string.Equals(NormalizeConsoleLabel(row.PlatformLabel), "Steam", StringComparison.OrdinalIgnoreCase)
                || !string.IsNullOrWhiteSpace(row.SteamAppId);
        }

        static bool LooksLikeNonSteamShortcutId(string value)
        {
            return !string.IsNullOrWhiteSpace(value)
                && value.Length >= 16
                && value.All(char.IsDigit);
        }

        void ApplyMatchedPrimaryId(FilenameParseResult result, FilenameConventionRule rule, string rawValue)
        {
            if (result == null || string.IsNullOrWhiteSpace(rawValue)) return;
            var normalizedPlatform = NormalizeConsoleLabel(rule == null ? string.Empty : rule.PlatformLabel);
            if (string.Equals(normalizedPlatform, "Steam", StringComparison.OrdinalIgnoreCase))
            {
                result.SteamAppId = rawValue;
                return;
            }

            if (LooksLikeNonSteamShortcutId(rawValue))
            {
                result.NonSteamId = rawValue;
                return;
            }

            result.SteamAppId = rawValue;
        }

        static string CleanNumericId(string value)
        {
            return new string((value ?? string.Empty).Where(char.IsDigit).ToArray());
        }

        static string CleanTag(string value)
        {
            return (value ?? string.Empty).Trim();
        }

        static bool LooksLikeRenamedSteamScreenshot(string fileName)
        {
            var baseName = Path.GetFileNameWithoutExtension(fileName ?? string.Empty);
            return Regex.IsMatch(baseName, "^.+?_\\d{14}(?:[_-]\\d+)?$", RegexOptions.CultureInvariant);
        }

        static string[] MergePlatformTags(IEnumerable<string> tags, params string[] additionalTags)
        {
            return (tags ?? Enumerable.Empty<string>())
                .Concat(additionalTags ?? Array.Empty<string>())
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        string NormalizeConsoleLabel(string value)
        {
            return dependencies.NormalizeConsoleLabel == null ? (value ?? string.Empty).Trim() : dependencies.NormalizeConsoleLabel(value ?? string.Empty);
        }

        static string ReadGroup(Match match, string groupName)
        {
            if (match == null || string.IsNullOrWhiteSpace(groupName)) return string.Empty;
            var group = match.Groups[groupName];
            return group == null || !group.Success ? string.Empty : group.Value;
        }

        static DateTime? ParseTimestamp(string rawValue, string format)
        {
            if (string.IsNullOrWhiteSpace(rawValue) || string.IsNullOrWhiteSpace(format)) return null;
            if (string.Equals(format, "unix-ms", StringComparison.OrdinalIgnoreCase))
            {
                long unixMilliseconds;
                if (long.TryParse(rawValue.Replace(",", string.Empty), out unixMilliseconds))
                {
                    try
                    {
                        return DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds).ToLocalTime().DateTime;
                    }
                    catch
                    {
                        return null;
                    }
                }
                return null;
            }

            DateTime parsed;
            return DateTime.TryParseExact(rawValue, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed)
                ? DateTime.SpecifyKind(parsed, DateTimeKind.Local)
                : (DateTime?)null;
        }

        static bool TryParseXboxPcCaptureFromTrailingTimestamp(string fileName, out string title, out DateTime captureTime)
        {
            title = string.Empty;
            captureTime = default;

            var candidate = fileName ?? string.Empty;
            var extension = Path.GetExtension(candidate);
            if (!string.IsNullOrWhiteSpace(extension)
                && !Regex.IsMatch(extension, @"^\.(png|jpe?g|mp4|mkv|avi|mov|wmv|webm)$", RegexOptions.IgnoreCase))
            {
                return false;
            }

            var baseName = string.IsNullOrWhiteSpace(extension)
                ? Path.GetFileName(candidate)
                : Path.GetFileNameWithoutExtension(candidate);
            var stampMatch = Regex.Match(
                baseName ?? string.Empty,
                @"(?<stamp>\d{1,2}_\d{1,2}_\d{4}\s+\d{1,2}_\d{2}_\d{2}\s+[AP]M)$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!stampMatch.Success) return false;

            var parsed = ParseTimestamp(stampMatch.Groups["stamp"].Value, "M_d_yyyy h_mm_ss tt");
            if (!parsed.HasValue) return false;

            var derivedTitle = (baseName ?? string.Empty).Substring(0, stampMatch.Index).TrimEnd();
            if (string.IsNullOrWhiteSpace(derivedTitle)) return false;

            title = derivedTitle;
            captureTime = parsed.Value;
            return true;
        }

        static DateTime? ParseGenericCaptureDate(string fileName)
        {
            DateTime parsed;
            if (string.IsNullOrEmpty(fileName)) return null;

            var fullStamp = Regex.Match(fileName, @"(?:^|_)(?<stamp>\d{14})(?:[_-]|(?=\.[^.]+$))", RegexOptions.IgnoreCase);
            if (fullStamp.Success && DateTime.TryParseExact(fullStamp.Groups["stamp"].Value, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
            {
                return DateTime.SpecifyKind(parsed, DateTimeKind.Local);
            }

            // ISO date at start of name (e.g. 2011-05-16_00001.jpg) or after underscore (e.g. IMG_2011-05-16.jpg)
            var dateOnly = Regex.Match(fileName, @"(?:^|_)(?<stamp>\d{4}-\d{2}-\d{2})(?:_|(?=\.[^.]+$))", RegexOptions.IgnoreCase);
            if (dateOnly.Success && DateTime.TryParseExact(dateOnly.Groups["stamp"].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
            {
                return DateTime.SpecifyKind(parsed, DateTimeKind.Local);
            }

            var dateDots = Regex.Match(fileName, @"(?:^|_)(?<stamp>\d{4}\.\d{2}\.\d{2})(?:_|(?=\.[^.]+$))", RegexOptions.IgnoreCase);
            if (dateDots.Success && DateTime.TryParseExact(dateDots.Groups["stamp"].Value, "yyyy.MM.dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
            {
                return DateTime.SpecifyKind(parsed, DateTimeKind.Local);
            }

            var dateUnder = Regex.Match(fileName, @"(?:^|_)(?<stamp>\d{4}_\d{2}_\d{2})(?:_|(?=\.[^.]+$))", RegexOptions.IgnoreCase);
            if (dateUnder.Success && DateTime.TryParseExact(dateUnder.Groups["stamp"].Value, "yyyy_MM_dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
            {
                return DateTime.SpecifyKind(parsed, DateTimeKind.Local);
            }

            // Compact yyyyMMdd at start or after separator (e.g. 20110516_1.jpg, photo_20110516.png)
            var compactDate = Regex.Match(fileName, @"(?:^|_)(?<stamp>\d{8})(?:_|(?=\.[^.]+$))", RegexOptions.IgnoreCase);
            if (compactDate.Success && DateTime.TryParseExact(compactDate.Groups["stamp"].Value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
            {
                return DateTime.SpecifyKind(parsed, DateTimeKind.Local);
            }

            var xboxStamp = Regex.Match(fileName, @"[-–—](?<stamp>\d{4}_\d{2}_\d{2}[-_]\d{2}[-_]\d{2}[-_]\d{2})(?=\.[^.]+$)", RegexOptions.IgnoreCase);
            if (xboxStamp.Success && DateTime.TryParseExact(xboxStamp.Groups["stamp"].Value, "yyyy_MM_dd-HH_mm_ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
            {
                return DateTime.SpecifyKind(parsed, DateTimeKind.Local);
            }
            if (xboxStamp.Success && DateTime.TryParseExact(xboxStamp.Groups["stamp"].Value, "yyyy_MM_dd_HH_mm_ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
            {
                return DateTime.SpecifyKind(parsed, DateTimeKind.Local);
            }

            var steamClipUnix = Regex.Match(fileName, @"^clip_(?<stamp>[\d,]{13,17})(?=\.[^.]+$)", RegexOptions.IgnoreCase);
            if (steamClipUnix.Success)
            {
                return ParseTimestamp(steamClipUnix.Groups["stamp"].Value, "unix-ms");
            }

            return null;
        }

        internal static string GetPatternEditorText(string pattern)
        {
            var trimmed = (pattern ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) return string.Empty;
            string readable;
            if (KnownRegexPatterns.TryGetValue(trimmed, out readable)) return readable;
            return trimmed;
        }

        internal static string NormalizePatternTextForStorage(string patternText)
        {
            return (patternText ?? string.Empty).Trim();
        }

        internal static string BuildRegexPattern(string patternText, string timestampGroup)
        {
            var trimmed = NormalizePatternTextForStorage(patternText);
            if (string.IsNullOrWhiteSpace(trimmed)) return string.Empty;
            if (!LooksLikeReadablePattern(trimmed)) return trimmed;

            var containsMatch = Regex.Match(trimmed, @"^\[contains:(?<value>.+)\]$", RegexOptions.IgnoreCase);
            if (containsMatch.Success)
            {
                return ".*" + Regex.Escape(containsMatch.Groups["value"].Value) + ".*";
            }

            var segments = TokenizeReadablePattern(trimmed);
            var builder = new StringBuilder("^");
            var stampGroupName = string.IsNullOrWhiteSpace(timestampGroup) ? "stamp" : timestampGroup.Trim();
            var stampOpen = false;

            for (var i = 0; i < segments.Count; i++)
            {
                var segment = segments[i];
                if (segment.IsToken)
                {
                    if (IsTimestampToken(segment.Value))
                    {
                        if (!stampOpen)
                        {
                            builder.Append("(?<").Append(stampGroupName).Append(">");
                            stampOpen = true;
                        }
                        builder.Append(RegexForToken(segment.Value));
                        if (!NextSegmentKeepsTimestampOpen(segments, i + 1))
                        {
                            builder.Append(")");
                            stampOpen = false;
                        }
                        continue;
                    }

                    if (stampOpen)
                    {
                        builder.Append(")");
                        stampOpen = false;
                    }

                    builder.Append(RegexForToken(segment.Value));
                    continue;
                }

                if (stampOpen && IsTimestampSeparator(segment.Value) && NextSegmentStartsTimestamp(segments, i + 1))
                {
                    builder.Append(Regex.Escape(segment.Value));
                    continue;
                }

                if (stampOpen)
                {
                    builder.Append(")");
                    stampOpen = false;
                }

                builder.Append(Regex.Escape(segment.Value));
            }

            if (stampOpen) builder.Append(")");
            builder.Append("$");
            return builder.ToString();
        }

        static bool LooksLikeReadablePattern(string patternText)
        {
            if (string.IsNullOrWhiteSpace(patternText)) return false;
            if (patternText.StartsWith("^", StringComparison.Ordinal) || patternText.Contains("(?<") || patternText.Contains(@"\d") || patternText.Contains(@".*"))
            {
                return false;
            }

            var segments = TokenizeReadablePattern(patternText);
            return segments.Any(segment => segment.IsToken)
                && segments
                    .Where(segment => segment.IsToken)
                    .All(segment => IsKnownReadableToken(segment.Value));
        }

        static List<ReadablePatternSegment> TokenizeReadablePattern(string patternText)
        {
            var segments = new List<ReadablePatternSegment>();
            var index = 0;
            while (index < patternText.Length)
            {
                if (patternText[index] == '[')
                {
                    var closeIndex = patternText.IndexOf(']', index + 1);
                    if (closeIndex > index)
                    {
                        segments.Add(new ReadablePatternSegment(true, patternText.Substring(index + 1, closeIndex - index - 1)));
                        index = closeIndex + 1;
                        continue;
                    }
                }

                var nextToken = patternText.IndexOf('[', index);
                if (nextToken < 0) nextToken = patternText.Length;
                segments.Add(new ReadablePatternSegment(false, patternText.Substring(index, nextToken - index)));
                index = nextToken;
            }

            return segments;
        }

        static bool IsTimestampToken(string token)
        {
            switch ((token ?? string.Empty).Trim())
            {
                case "M":
                case "d":
                case "h":
                case "yyyy":
                case "MM":
                case "dd":
                case "HH":
                case "hh":
                case "mm":
                case "ss":
                case "tt":
                    return true;
                default:
                    return false;
            }
        }

        static bool IsTimestampSeparator(string value)
        {
            return !string.IsNullOrEmpty(value) && value.All(ch => ch == '-' || ch == '_' || ch == ':' || ch == ' ' || ch == 'T');
        }

        static bool NextSegmentStartsTimestamp(List<ReadablePatternSegment> segments, int startIndex)
        {
            for (var i = startIndex; i < segments.Count; i++)
            {
                var segment = segments[i];
                if (segment.IsToken) return IsTimestampToken(segment.Value);
                if (!string.IsNullOrEmpty(segment.Value)) return false;
            }
            return false;
        }

        static bool NextSegmentKeepsTimestampOpen(List<ReadablePatternSegment> segments, int startIndex)
        {
            if (startIndex >= segments.Count) return false;
            var next = segments[startIndex];
            if (next.IsToken) return IsTimestampToken(next.Value);
            return IsTimestampSeparator(next.Value) && NextSegmentStartsTimestamp(segments, startIndex + 1);
        }

        static string RegexForToken(string token)
        {
            switch ((token ?? string.Empty).Trim())
            {
                case "appid":
                    return @"(?<appid>\d{3,})";
                case "title":
                    return @"(?<title>.+?)";
                case "counter":
                    return @"\d+";
                case "opt-counter":
                    return @"(?:[_-]\d+)?";
                case "unixms":
                    return @"[\d,]{13,17}";
                case "M":
                case "d":
                case "h":
                    return @"\d{1,2}";
                case "yyyy":
                    return @"\d{4}";
                case "MM":
                case "dd":
                case "HH":
                case "hh":
                case "mm":
                case "ss":
                    return @"\d{2}";
                case "tt":
                    return @"[AP]M";
                case "ext:media":
                case "ext":
                    return @"(png|jpe?g|mp4|mkv|avi|mov|wmv|webm)";
                case "ext:image":
                    return @"(png|jpe?g)";
                case "ext:video":
                    return @"(mp4|mkv|avi|mov|wmv|webm)";
                default:
                    throw new InvalidOperationException("Unknown filename-rule token [" + token + "].");
            }
        }

        static bool IsKnownReadableToken(string token)
        {
            var trimmed = (token ?? string.Empty).Trim();
            return trimmed.StartsWith("contains:", StringComparison.OrdinalIgnoreCase)
                || trimmed == "appid"
                || trimmed == "title"
                || trimmed == "counter"
                || trimmed == "opt-counter"
                || trimmed == "unixms"
                || trimmed == "M"
                || trimmed == "d"
                || trimmed == "h"
                || trimmed == "yyyy"
                || trimmed == "MM"
                || trimmed == "dd"
                || trimmed == "HH"
                || trimmed == "hh"
                || trimmed == "mm"
                || trimmed == "ss"
                || trimmed == "tt"
                || trimmed == "ext"
                || trimmed == "ext:media"
                || trimmed == "ext:image"
                || trimmed == "ext:video";
        }

        static readonly Dictionary<string, string> KnownRegexPatterns = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { @"^(?<appid>\d{3,})_(?<stamp>\d{14})(?:[_-]\d+)?\.(png|jpe?g|mp4|mkv|avi|mov|wmv|webm)$", "[appid]_[yyyy][MM][dd][HH][mm][ss][opt-counter].[ext:media]" },
            { @"^(?<stamp>\d{14})(?:[_-]\d+)?\.(png|jpe?g|mp4|mkv|avi|mov|wmv|webm)$", "[yyyy][MM][dd][HH][mm][ss][opt-counter].[ext:media]" },
            { @"^(?<title>.+?)_(?<stamp>\d{4}-\d{2}-\d{2})_\d+\.(png|jpe?g|mp4|mkv|avi|mov|wmv|webm)$", "[title]_[yyyy]-[MM]-[dd]_[counter].[ext:media]" },
            { @"^clip_(?<stamp>[\d,]{13,17})\.(mp4|mkv|avi|mov|wmv|webm)$", "clip_[unixms].[ext:video]" },
            { @"^(?<title>.+?)_(?<stamp>\d{14})\.(png|jpe?g|mp4|mkv|avi|mov|wmv|webm)$", "[title]_[yyyy][MM][dd][HH][mm][ss].[ext:media]" },
            { @"^(?<title>.+?)[-–—](?<stamp>\d{4}_\d{2}_\d{2}[-_]\d{2}[-_]\d{2}[-_]\d{2})\.(png|jpe?g|mp4|mkv|avi|mov|wmv|webm)$", "[title]-[yyyy]_[MM]_[dd]-[HH]_[mm]_[ss].[ext:media]" },
            { @"^(?<title>.+?)\s+(?<stamp>\d{1,2}_\d{1,2}_\d{4}\s+\d{1,2}_\d{2}_\d{2}\s+[AP]M)\.(png|jpe?g|mp4|mkv|avi|mov|wmv|webm)$", "[title] [M]_[d]_[yyyy] [h]_[mm]_[ss] [tt].[ext:media]" },
            { @".*PS5.*", "[contains:PS5]" },
            { @".*PlayStation.*", "[contains:PlayStation]" }
        };

        readonly struct ReadablePatternSegment
        {
            public ReadablePatternSegment(bool isToken, string value)
            {
                IsToken = isToken;
                Value = value ?? string.Empty;
            }

            public bool IsToken { get; }
            public string Value { get; }
        }

        struct KnownSteamRenameLookupEntry
        {
            public bool HasSteamMatch;
            public bool HasConflictingPlatform;
            public string SteamAppId;
        }

        struct KnownNonSteamLookupEntry
        {
            public string Name;
        }
    }
}
