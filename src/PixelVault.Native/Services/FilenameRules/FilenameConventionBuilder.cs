using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace PixelVaultNative
{
    internal static class FilenameConventionBuilder
    {
        static readonly string[] SupportedReadableTokens =
        {
            "appid",
            "title",
            "counter",
            "opt-counter",
            "unixms",
            "M",
            "d",
            "h",
            "yyyy",
            "MM",
            "dd",
            "HH",
            "mm",
            "ss",
            "tt",
            "ext",
            "ext:media",
            "ext:image",
            "ext:video"
        };

        sealed class PatternPiece
        {
            public bool IsToken { get; init; }
            public string Value { get; init; } = string.Empty;
        }

        sealed class TimestampDetection
        {
            public int Index { get; init; }
            public int Length { get; init; }
            public string Value { get; init; } = string.Empty;
            public string Format { get; init; } = string.Empty;
        }

        internal static string GetRoleLabel(FilenameConventionBuilderComponentRole role)
        {
            switch (role)
            {
                case FilenameConventionBuilderComponentRole.Title:
                    return "Title";
                case FilenameConventionBuilderComponentRole.Timestamp:
                    return "Date / Time";
                case FilenameConventionBuilderComponentRole.SteamAppId:
                    return "Steam App ID";
                case FilenameConventionBuilderComponentRole.NonSteamId:
                    return "Non-Steam ID";
                case FilenameConventionBuilderComponentRole.Counter:
                    return "Counter";
                case FilenameConventionBuilderComponentRole.Extension:
                    return "Extension";
                default:
                    return "Literal";
            }
        }

        internal static FilenameConventionBuilderComponentRole[] GetEditableRoles()
        {
            return new[]
            {
                FilenameConventionBuilderComponentRole.Literal,
                FilenameConventionBuilderComponentRole.Title,
                FilenameConventionBuilderComponentRole.Timestamp,
                FilenameConventionBuilderComponentRole.SteamAppId,
                FilenameConventionBuilderComponentRole.NonSteamId,
                FilenameConventionBuilderComponentRole.Counter,
                FilenameConventionBuilderComponentRole.Extension
            };
        }

        internal static FilenameConventionBuilderDraft CreateDraftFromFileName(
            string fileName,
            FilenameParseResult parsed,
            FilenameConventionRule candidateRule,
            string fallbackPlatformLabel,
            Func<string, string> normalizeConsoleLabel,
            Func<string, string> defaultPlatformTagsTextForLabel)
        {
            var cleanFileName = Path.GetFileName(fileName ?? string.Empty) ?? string.Empty;
            var normalizedCandidate = NormalizeRulePattern(candidateRule);
            FilenameConventionBuilderDraft draft = null;
            if (normalizedCandidate != null)
            {
                draft = TryCreateDraftFromRule(
                    normalizedCandidate,
                    normalizeConsoleLabel,
                    defaultPlatformTagsTextForLabel,
                    cleanFileName);
            }

            // `TryCreateDraftFromRule` returns a non-null "advanced only" shell (no segments) when the
            // candidate pattern is not representable as readable tokens — e.g. regex fallback from
            // `BuildCustomFilenameConventionFromSample` for filenames like `2026-04-02 09.07.04.jpg`.
            // In that case we must still build a guided draft from the basename or ApplyBuilderDraft yields an empty pattern.
            if (draft == null || draft.Segments == null || draft.Segments.Count == 0)
            {
                draft = CreateFallbackDraft(
                    cleanFileName,
                    parsed,
                    normalizedCandidate,
                    fallbackPlatformLabel,
                    normalizeConsoleLabel,
                    defaultPlatformTagsTextForLabel);
            }

            draft.FileName = cleanFileName;
            draft.PlatformLabel = normalizeConsoleLabel(
                string.IsNullOrWhiteSpace(draft.PlatformLabel)
                    ? FirstNonEmpty(parsed?.PlatformLabel, fallbackPlatformLabel, normalizedCandidate?.PlatformLabel, "Other")
                    : draft.PlatformLabel);
            draft.PlatformTagsText = string.IsNullOrWhiteSpace(draft.PlatformTagsText)
                ? defaultPlatformTagsTextForLabel(draft.PlatformLabel)
                : draft.PlatformTagsText;
            draft.PreserveFileTimes = normalizedCandidate?.PreserveFileTimes ?? parsed?.PreserveFileTimes ?? false;
            draft.RoutesToManualWhenMissingSteamAppId = normalizedCandidate?.RoutesToManualWhenMissingSteamAppId ?? parsed?.RoutesToManualWhenMissingSteamAppId ?? false;
            if (string.IsNullOrWhiteSpace(draft.RuleName))
            {
                draft.RuleName = "Custom: " + (string.IsNullOrWhiteSpace(cleanFileName) ? "New Rule" : Path.GetFileNameWithoutExtension(cleanFileName));
            }
            draft.ShapePreview = BuildPatternTextFromDraft(draft);
            return draft;
        }

        internal static FilenameConventionBuilderDraft TryCreateDraftFromRule(
            FilenameConventionRule rule,
            Func<string, string> normalizeConsoleLabel,
            Func<string, string> defaultPlatformTagsTextForLabel,
            string actualFileName = null)
        {
            if (rule == null) return null;

            var patternText = FilenameParserService.GetPatternEditorText(rule.PatternText ?? rule.Pattern);
            if (!IsRepresentableReadablePattern(patternText))
            {
                return new FilenameConventionBuilderDraft
                {
                    FileName = actualFileName ?? string.Empty,
                    RuleName = rule.Name ?? string.Empty,
                    ConventionId = rule.ConventionId ?? string.Empty,
                    Enabled = rule.Enabled,
                    Priority = rule.Priority,
                    PlatformLabel = normalizeConsoleLabel(rule.PlatformLabel),
                    PlatformTagsText = string.IsNullOrWhiteSpace(rule.PlatformTagsText)
                        ? defaultPlatformTagsTextForLabel(normalizeConsoleLabel(rule.PlatformLabel))
                        : rule.PlatformTagsText ?? string.Empty,
                    TimestampFormat = rule.TimestampFormat ?? string.Empty,
                    PreserveFileTimes = rule.PreserveFileTimes,
                    RoutesToManualWhenMissingSteamAppId = rule.RoutesToManualWhenMissingSteamAppId,
                    IsBuiltInTemplate = rule.IsBuiltIn,
                    CanRoundTripInBuilder = false,
                    FallbackReason = GetFallbackReason(patternText),
                    ShapePreview = patternText
                };
            }

            var draft = new FilenameConventionBuilderDraft
            {
                FileName = actualFileName ?? string.Empty,
                RuleName = rule.Name ?? string.Empty,
                ConventionId = rule.ConventionId ?? string.Empty,
                Enabled = rule.Enabled,
                Priority = rule.Priority,
                PlatformLabel = normalizeConsoleLabel(rule.PlatformLabel),
                PlatformTagsText = string.IsNullOrWhiteSpace(rule.PlatformTagsText)
                    ? defaultPlatformTagsTextForLabel(normalizeConsoleLabel(rule.PlatformLabel))
                    : rule.PlatformTagsText ?? string.Empty,
                TimestampFormat = rule.TimestampFormat ?? string.Empty,
                PreserveFileTimes = rule.PreserveFileTimes,
                RoutesToManualWhenMissingSteamAppId = rule.RoutesToManualWhenMissingSteamAppId,
                IsBuiltInTemplate = rule.IsBuiltIn,
                CanRoundTripInBuilder = true
            };

            var platformLabel = normalizeConsoleLabel(rule.PlatformLabel);
            foreach (var segment in BuildSegmentsFromReadablePattern(patternText, rule.TimestampFormat, platformLabel))
            {
                draft.Segments.Add(segment);
            }

            if (!string.IsNullOrWhiteSpace(actualFileName))
            {
                HydrateDraftWithActualFileName(draft, rule, actualFileName, platformLabel);
            }

            draft.ShapePreview = BuildPatternTextFromDraft(draft);
            return draft;
        }

        internal static FilenameConventionRule BuildRuleFromDraft(
            FilenameConventionBuilderDraft draft,
            FilenameConventionRule existingRule,
            Func<string, string> cleanTag,
            Func<string, string> normalizeConsoleLabel,
            Func<string, string> defaultPlatformTagsTextForLabel)
        {
            if (draft == null) return existingRule;
            var rule = existingRule ?? new FilenameConventionRule();
            rule.ConventionId = cleanTag(string.IsNullOrWhiteSpace(draft.ConventionId) ? rule.ConventionId : draft.ConventionId);
            if (string.IsNullOrWhiteSpace(rule.ConventionId))
            {
                rule.ConventionId = "custom_" + Guid.NewGuid().ToString("N").Substring(0, 10);
            }

            var patternText = FilenameParserService.NormalizePatternTextForStorage(BuildPatternTextFromDraft(draft));
            rule.Name = cleanTag(draft.RuleName);
            rule.Enabled = draft.Enabled;
            rule.Priority = draft.Priority;
            rule.PatternText = patternText;
            rule.Pattern = patternText;
            rule.PlatformLabel = normalizeConsoleLabel(draft.PlatformLabel);
            rule.PlatformTagsText = cleanTag(string.IsNullOrWhiteSpace(draft.PlatformTagsText) ? defaultPlatformTagsTextForLabel(rule.PlatformLabel) : draft.PlatformTagsText);
            rule.SteamAppIdGroup = UsesIdSegment(draft) ? "appid" : string.Empty;
            rule.TitleGroup = UsesRole(draft, FilenameConventionBuilderComponentRole.Title) ? "title" : string.Empty;
            rule.TimestampGroup = UsesRole(draft, FilenameConventionBuilderComponentRole.Timestamp) ? "stamp" : string.Empty;
            rule.TimestampFormat = UsesRole(draft, FilenameConventionBuilderComponentRole.Timestamp) ? cleanTag(draft.TimestampFormat) : string.Empty;
            rule.PreserveFileTimes = draft.PreserveFileTimes;
            rule.RoutesToManualWhenMissingSteamAppId = draft.RoutesToManualWhenMissingSteamAppId;
            rule.ConfidenceLabel = string.IsNullOrWhiteSpace(rule.ConfidenceLabel) ? "CustomRule" : cleanTag(rule.ConfidenceLabel);
            rule.IsBuiltIn = false;
            return rule;
        }

        internal static string BuildCrossSampleHintText(string activeFileName, IEnumerable<string> stagedFileNames)
        {
            var active = Path.GetFileName(activeFileName ?? string.Empty) ?? string.Empty;
            var pool = (stagedFileNames ?? Enumerable.Empty<string>())
                .Select(item => Path.GetFileName(item ?? string.Empty) ?? string.Empty)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (string.IsNullOrWhiteSpace(active) || pool.Count <= 1) return "Stage more filenames to compare shared structure.";

            var timestampCount = pool.Count(name => TryDetectTimestamp(Path.GetFileNameWithoutExtension(name), out _));
            var idPrefixCount = pool.Count(name => Regex.IsMatch(Path.GetFileNameWithoutExtension(name) ?? string.Empty, @"^\d{3,}[_\-\s]+", RegexOptions.CultureInvariant));
            var separatorSet = string.Concat(pool.SelectMany(name => (Path.GetFileNameWithoutExtension(name) ?? string.Empty).Where(ch => !char.IsLetterOrDigit(ch))).Distinct().OrderBy(ch => ch)).Trim();

            var parts = new List<string>();
            if (timestampCount > 1) parts.Add(timestampCount + "/" + pool.Count + " staged filenames include a timestamp.");
            if (idPrefixCount > 1) parts.Add(idPrefixCount + "/" + pool.Count + " staged filenames start with a numeric ID.");
            if (!string.IsNullOrWhiteSpace(separatorSet))
            {
                parts.Add("Common separators: " + string.Join(" ", separatorSet.ToCharArray().Select(ch => "'" + ch + "'")));
            }

            return parts.Count == 0
                ? "These staged filenames do not share an obvious timestamp or ID pattern yet."
                : string.Join(" ", parts);
        }

        static FilenameConventionRule NormalizeRulePattern(FilenameConventionRule source)
        {
            if (source == null) return null;
            return new FilenameConventionRule
            {
                ConventionId = source.ConventionId,
                Name = source.Name,
                Enabled = source.Enabled,
                Priority = source.Priority,
                Pattern = FilenameParserService.GetPatternEditorText(source.PatternText ?? source.Pattern),
                PatternText = FilenameParserService.GetPatternEditorText(source.PatternText ?? source.Pattern),
                PlatformLabel = source.PlatformLabel,
                PlatformTagsText = source.PlatformTagsText,
                SteamAppIdGroup = source.SteamAppIdGroup,
                TitleGroup = source.TitleGroup,
                TimestampGroup = source.TimestampGroup,
                TimestampFormat = source.TimestampFormat,
                PreserveFileTimes = source.PreserveFileTimes,
                RoutesToManualWhenMissingSteamAppId = source.RoutesToManualWhenMissingSteamAppId,
                ConfidenceLabel = source.ConfidenceLabel,
                IsBuiltIn = source.IsBuiltIn
            };
        }

        static FilenameConventionBuilderDraft CreateFallbackDraft(
            string fileName,
            FilenameParseResult parsed,
            FilenameConventionRule candidateRule,
            string fallbackPlatformLabel,
            Func<string, string> normalizeConsoleLabel,
            Func<string, string> defaultPlatformTagsTextForLabel)
        {
            var cleanFileName = Path.GetFileName(fileName ?? string.Empty) ?? string.Empty;
            var baseName = Path.GetFileNameWithoutExtension(cleanFileName) ?? string.Empty;
            var extension = Path.GetExtension(cleanFileName) ?? string.Empty;
            var normalizedPlatform = normalizeConsoleLabel(FirstNonEmpty(parsed?.PlatformLabel, fallbackPlatformLabel, candidateRule?.PlatformLabel, "Other"));

            var draft = new FilenameConventionBuilderDraft
            {
                FileName = cleanFileName,
                RuleName = candidateRule?.Name ?? ("Custom: " + (string.IsNullOrWhiteSpace(cleanFileName) ? "New Rule" : Path.GetFileNameWithoutExtension(cleanFileName))),
                ConventionId = candidateRule?.ConventionId ?? string.Empty,
                Enabled = candidateRule?.Enabled ?? true,
                Priority = candidateRule?.Priority ?? 1200,
                PlatformLabel = normalizedPlatform,
                PlatformTagsText = string.IsNullOrWhiteSpace(candidateRule?.PlatformTagsText)
                    ? defaultPlatformTagsTextForLabel(normalizedPlatform)
                    : candidateRule.PlatformTagsText,
                TimestampFormat = candidateRule?.TimestampFormat ?? string.Empty,
                PreserveFileTimes = candidateRule?.PreserveFileTimes ?? parsed?.PreserveFileTimes ?? false,
                RoutesToManualWhenMissingSteamAppId = candidateRule?.RoutesToManualWhenMissingSteamAppId ?? parsed?.RoutesToManualWhenMissingSteamAppId ?? false
            };

            TimestampDetection detection;
            var hasTimestamp = TryDetectTimestamp(baseName, out detection);
            if (hasTimestamp)
            {
                detection = CoalesceYyyyMmDdWithDotTimeTail(baseName, detection);
            }

            var prefix = hasTimestamp ? baseName.Substring(0, detection.Index) : baseName;
            var suffix = hasTimestamp ? baseName.Substring(detection.Index + detection.Length) : string.Empty;

            var idMatch = Regex.Match(prefix ?? string.Empty, @"^(?<id>\d{3,})(?<sep>[_\-\s]+)(?<rest>.+)$", RegexOptions.CultureInvariant);
            if (idMatch.Success && !LooksLikeCompactDateToken(idMatch.Groups["id"].Value))
            {
                var idRole = DetermineIdRole(idMatch.Groups["id"].Value, normalizedPlatform, parsed);
                draft.Segments.Add(new FilenameConventionBuilderSegment
                {
                    Text = idMatch.Groups["id"].Value,
                    SuggestedRole = idRole,
                    AssignedRole = idRole,
                    Hint = "Numeric prefix"
                });
                draft.Segments.Add(new FilenameConventionBuilderSegment
                {
                    Text = idMatch.Groups["sep"].Value,
                    SuggestedRole = FilenameConventionBuilderComponentRole.Literal,
                    AssignedRole = FilenameConventionBuilderComponentRole.Literal,
                    Hint = "Separator (change role if this was misread as an ID)"
                });
                AppendTitleWithTrailingLiteral(draft.Segments, idMatch.Groups["rest"].Value, hasTimestamp);
            }
            else
            {
                AppendTitleWithTrailingLiteral(draft.Segments, prefix, hasTimestamp);
            }

            if (hasTimestamp)
            {
                draft.TimestampFormat = string.IsNullOrWhiteSpace(draft.TimestampFormat) ? detection.Format : draft.TimestampFormat;
                draft.Segments.Add(new FilenameConventionBuilderSegment
                {
                    Text = detection.Value,
                    SuggestedRole = FilenameConventionBuilderComponentRole.Timestamp,
                    AssignedRole = FilenameConventionBuilderComponentRole.Timestamp,
                    Hint = "Looks like " + detection.Format
                });

                if (Regex.IsMatch(suffix ?? string.Empty, @"^[_-]\d+$", RegexOptions.CultureInvariant))
                {
                    draft.Segments.Add(new FilenameConventionBuilderSegment
                    {
                        Text = suffix,
                        SuggestedRole = FilenameConventionBuilderComponentRole.Counter,
                        AssignedRole = FilenameConventionBuilderComponentRole.Counter,
                        Hint = "Optional trailing counter"
                    });
                    suffix = string.Empty;
                }
            }

            if (!string.IsNullOrWhiteSpace(suffix))
            {
                draft.Segments.Add(new FilenameConventionBuilderSegment
                {
                    Text = suffix,
                    SuggestedRole = FilenameConventionBuilderComponentRole.Literal,
                    AssignedRole = FilenameConventionBuilderComponentRole.Literal
                });
            }

            if (!string.IsNullOrWhiteSpace(extension))
            {
                draft.Segments.Add(new FilenameConventionBuilderSegment
                {
                    Text = extension,
                    SuggestedRole = FilenameConventionBuilderComponentRole.Extension,
                    AssignedRole = FilenameConventionBuilderComponentRole.Extension,
                    Hint = "File extension"
                });
            }

            if (draft.Segments.Count == 0)
            {
                draft.Segments.Add(new FilenameConventionBuilderSegment
                {
                    Text = cleanFileName,
                    SuggestedRole = FilenameConventionBuilderComponentRole.Title,
                    AssignedRole = FilenameConventionBuilderComponentRole.Title
                });
            }

            draft.ShapePreview = BuildPatternTextFromDraft(draft);
            return draft;
        }

        static void AppendTitleWithTrailingLiteral(ICollection<FilenameConventionBuilderSegment> segments, string text, bool splitTrailingSeparators)
        {
            var value = text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value)) return;

            if (!splitTrailingSeparators)
            {
                segments.Add(new FilenameConventionBuilderSegment
                {
                    Text = value,
                    SuggestedRole = FilenameConventionBuilderComponentRole.Title,
                    AssignedRole = FilenameConventionBuilderComponentRole.Title,
                    Hint = "Likely title"
                });
                return;
            }

            var trailing = Regex.Match(value, @"[_\-\s]+$", RegexOptions.CultureInvariant);
            var title = trailing.Success ? value.Substring(0, trailing.Index) : value;
            var trailingLiteral = trailing.Success ? trailing.Value : string.Empty;

            if (!string.IsNullOrWhiteSpace(title))
            {
                segments.Add(new FilenameConventionBuilderSegment
                {
                    Text = title,
                    SuggestedRole = FilenameConventionBuilderComponentRole.Title,
                    AssignedRole = FilenameConventionBuilderComponentRole.Title,
                    Hint = "Likely title"
                });
            }

            if (!string.IsNullOrWhiteSpace(trailingLiteral))
            {
                segments.Add(new FilenameConventionBuilderSegment
                {
                    Text = trailingLiteral,
                    SuggestedRole = FilenameConventionBuilderComponentRole.Literal,
                    AssignedRole = FilenameConventionBuilderComponentRole.Literal,
                    Hint = "Trailing separator / space"
                });
            }
        }

        static FilenameConventionBuilderComponentRole DetermineIdRole(string rawId, string platformLabel, FilenameParseResult parsed)
        {
            if (!string.IsNullOrWhiteSpace(parsed?.NonSteamId) && string.Equals(parsed.NonSteamId, rawId, StringComparison.OrdinalIgnoreCase))
            {
                return FilenameConventionBuilderComponentRole.NonSteamId;
            }

            if (!string.IsNullOrWhiteSpace(parsed?.SteamAppId) && string.Equals(parsed.SteamAppId, rawId, StringComparison.OrdinalIgnoreCase))
            {
                return FilenameConventionBuilderComponentRole.SteamAppId;
            }

            if (string.Equals(platformLabel, "Steam", StringComparison.OrdinalIgnoreCase))
            {
                return FilenameConventionBuilderComponentRole.SteamAppId;
            }

            return rawId.Length >= 16
                ? FilenameConventionBuilderComponentRole.NonSteamId
                : FilenameConventionBuilderComponentRole.SteamAppId;
        }

        static bool LooksLikeCompactDateToken(string value)
        {
            return Regex.IsMatch(value ?? string.Empty, @"^\d{8}$", RegexOptions.CultureInvariant);
        }

        /// <summary>
        /// When <see cref="TryDetectTimestamp"/> returns date-only <c>yyyy-MM-dd</c> but the basename continues with
        /// phone-style <c> HH.mm.ss</c>, merge into one timestamp so the builder does not leave the time as a locked literal.
        /// </summary>
        static TimestampDetection CoalesceYyyyMmDdWithDotTimeTail(string baseName, TimestampDetection detection)
        {
            if (detection == null || string.IsNullOrEmpty(baseName)) return detection;
            if (!string.Equals(detection.Format, "yyyy-MM-dd", StringComparison.Ordinal)) return detection;
            var tailStart = detection.Index + detection.Length;
            if (tailStart >= baseName.Length) return detection;
            var tail = baseName.Substring(tailStart);
            if (!Regex.IsMatch(tail, @"^\s+\d{2}\.\d{2}\.\d{2}$", RegexOptions.CultureInvariant)) return detection;
            var combinedLen = detection.Length + tail.Length;
            return new TimestampDetection
            {
                Index = detection.Index,
                Length = combinedLen,
                Value = baseName.Substring(detection.Index, combinedLen),
                Format = "yyyy-MM-dd HH.mm.ss"
            };
        }

        static bool TryDetectTimestamp(string value, out TimestampDetection detection)
        {
            detection = null;
            var source = value ?? string.Empty;
            var candidates = new[]
            {
                (Pattern: @"\d{1,2}_\d{1,2}_\d{4}\s+\d{1,2}_\d{2}_\d{2}\s+[AP]M", Format: "M_d_yyyy h_mm_ss tt"),
                (Pattern: @"\d{4}[-_]\d{2}[-_]\d{2}[ T_-]\d{2}[-_:]\d{2}[-_:]\d{2}\s?[AP]M", Format: "yyyy-MM-dd hh-mm-ss tt"),
                (Pattern: @"\d{4}[-_]\d{2}[-_]\d{2}[ T_-]\d{2}[-_:]\d{2}[-_:]\d{2}", Format: "yyyy-MM-dd HH-mm-ss"),
                (Pattern: @"\d{4}[-_]\d{2}[-_]\d{2}\s+\d{2}\.\d{2}\.\d{2}", Format: "yyyy-MM-dd HH.mm.ss"),
                (Pattern: @"\d{14}", Format: "yyyyMMddHHmmss"),
                (Pattern: @"\d{8}", Format: "yyyyMMdd"),
                (Pattern: @"\d{4}[-_]\d{2}[-_]\d{2}", Format: "yyyy-MM-dd")
            };

            foreach (var candidate in candidates)
            {
                var match = Regex.Match(source, candidate.Pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (!match.Success) continue;
                detection = new TimestampDetection
                {
                    Index = match.Index,
                    Length = match.Length,
                    Value = match.Value,
                    Format = candidate.Format
                };
                return true;
            }

            return false;
        }

        static bool IsRepresentableReadablePattern(string patternText)
        {
            var trimmed = FilenameParserService.NormalizePatternTextForStorage(patternText);
            if (string.IsNullOrWhiteSpace(trimmed)) return false;
            if (trimmed.StartsWith("^", StringComparison.Ordinal) || trimmed.Contains("(?<", StringComparison.Ordinal) || trimmed.Contains(@"\d", StringComparison.Ordinal))
            {
                return false;
            }

            var pieces = TokenizePattern(trimmed);
            var hasToken = false;
            foreach (var piece in pieces)
            {
                if (!piece.IsToken) continue;
                hasToken = true;
                if (piece.Value.StartsWith("contains:", StringComparison.OrdinalIgnoreCase)) return false;
                if (!SupportedReadableTokens.Contains(piece.Value, StringComparer.OrdinalIgnoreCase)) return false;
            }

            return hasToken;
        }

        static string GetFallbackReason(string patternText)
        {
            var trimmed = FilenameParserService.NormalizePatternTextForStorage(patternText);
            if (string.IsNullOrWhiteSpace(trimmed)) return "This rule does not have a readable pattern yet.";
            if (trimmed.StartsWith("^", StringComparison.Ordinal) || trimmed.Contains("(?<", StringComparison.Ordinal) || trimmed.Contains(@"\d", StringComparison.Ordinal))
            {
                return "This rule uses raw regex, so it stays in Advanced mode.";
            }
            if (trimmed.Contains("[contains:", StringComparison.OrdinalIgnoreCase))
            {
                return "Contains-match rules stay in Advanced mode for now.";
            }
            return "This rule uses pattern pieces the guided builder does not support yet.";
        }

        static List<PatternPiece> TokenizePattern(string patternText)
        {
            var pieces = new List<PatternPiece>();
            var value = patternText ?? string.Empty;
            var index = 0;
            while (index < value.Length)
            {
                if (value[index] == '[')
                {
                    var end = value.IndexOf(']', index + 1);
                    if (end > index)
                    {
                        pieces.Add(new PatternPiece { IsToken = true, Value = value.Substring(index + 1, end - index - 1) });
                        index = end + 1;
                        continue;
                    }
                }

                var nextToken = value.IndexOf('[', index);
                if (nextToken < 0) nextToken = value.Length;
                pieces.Add(new PatternPiece { IsToken = false, Value = value.Substring(index, nextToken - index) });
                index = nextToken;
            }

            return pieces;
        }

        static IEnumerable<FilenameConventionBuilderSegment> BuildSegmentsFromReadablePattern(string patternText, string timestampFormat, string platformLabel)
        {
            var pieces = TokenizePattern(patternText);
            var index = 0;
            while (index < pieces.Count)
            {
                var piece = pieces[index];
                if (!piece.IsToken)
                {
                    if (!string.IsNullOrEmpty(piece.Value))
                    {
                        // Keep literals editable when they contain digits (e.g. a mistaken fixed time from an old draft).
                        var v = piece.Value;
                        var lockOnlyTinySeparator = v.Length <= 2 && v.All(ch => !char.IsDigit(ch));
                        yield return new FilenameConventionBuilderSegment
                        {
                            Text = piece.Value,
                            SuggestedRole = FilenameConventionBuilderComponentRole.Literal,
                            AssignedRole = FilenameConventionBuilderComponentRole.Literal,
                            Locked = lockOnlyTinySeparator
                        };
                    }
                    index++;
                    continue;
                }

                if (IsTimestampToken(piece.Value))
                {
                    var builder = new StringBuilder();
                    while (index < pieces.Count)
                    {
                        var current = pieces[index];
                        if (current.IsToken && IsTimestampToken(current.Value))
                        {
                            builder.Append("[").Append(current.Value).Append("]");
                            index++;
                            continue;
                        }
                        if (!current.IsToken && IsTimestampSeparator(current.Value) && index + 1 < pieces.Count && pieces[index + 1].IsToken && IsTimestampToken(pieces[index + 1].Value))
                        {
                            builder.Append(current.Value);
                            index++;
                            continue;
                        }
                        break;
                    }

                    yield return new FilenameConventionBuilderSegment
                    {
                        Text = builder.ToString(),
                        SuggestedRole = FilenameConventionBuilderComponentRole.Timestamp,
                        AssignedRole = FilenameConventionBuilderComponentRole.Timestamp,
                        Hint = string.IsNullOrWhiteSpace(timestampFormat) ? "Timestamp tokens" : timestampFormat
                    };
                    continue;
                }

                var role = TokenToRole(piece.Value, platformLabel);
                yield return new FilenameConventionBuilderSegment
                {
                    Text = "[" + piece.Value + "]",
                    SuggestedRole = role,
                    AssignedRole = role,
                    Hint = GetTokenHint(piece.Value),
                    Locked = role == FilenameConventionBuilderComponentRole.Literal
                };
                index++;
            }
        }

        static bool IsTimestampToken(string token)
        {
            switch ((token ?? string.Empty).Trim())
            {
                case "yyyy":
                case "M":
                case "d":
                case "MM":
                case "dd":
                case "HH":
                case "h":
                case "hh":
                case "mm":
                case "ss":
                case "tt":
                case "unixms":
                    return true;
                default:
                    return false;
            }
        }

        static bool IsTimestampSeparator(string value)
        {
            return !string.IsNullOrEmpty(value) && value.All(ch => ch == '-' || ch == '_' || ch == ':' || ch == ' ' || ch == 'T' || ch == '.');
        }

        static FilenameConventionBuilderComponentRole TokenToRole(string token, string platformLabel)
        {
            switch ((token ?? string.Empty).Trim())
            {
                case "title":
                    return FilenameConventionBuilderComponentRole.Title;
                case "appid":
                    return string.Equals(platformLabel, "Steam", StringComparison.OrdinalIgnoreCase)
                        ? FilenameConventionBuilderComponentRole.SteamAppId
                        : FilenameConventionBuilderComponentRole.NonSteamId;
                case "counter":
                case "opt-counter":
                    return FilenameConventionBuilderComponentRole.Counter;
                case "ext":
                case "ext:media":
                case "ext:image":
                case "ext:video":
                    return FilenameConventionBuilderComponentRole.Extension;
                default:
                    return FilenameConventionBuilderComponentRole.Literal;
            }
        }

        static string GetTokenHint(string token)
        {
            switch ((token ?? string.Empty).Trim())
            {
                case "appid":
                    return "Numeric ID";
                case "counter":
                    return "Required counter";
                case "opt-counter":
                    return "Optional counter";
                case "ext:image":
                    return "Image extension";
                case "ext:video":
                    return "Video extension";
                case "ext":
                case "ext:media":
                    return "Media extension";
                default:
                    return string.Empty;
            }
        }

        static void HydrateDraftWithActualFileName(FilenameConventionBuilderDraft draft, FilenameConventionRule rule, string actualFileName, string platformLabel)
        {
            if (draft == null || rule == null || string.IsNullOrWhiteSpace(actualFileName)) return;

            var patternText = FilenameParserService.NormalizePatternTextForStorage(rule.PatternText ?? rule.Pattern);
            if (string.IsNullOrWhiteSpace(patternText)) return;

            var regexText = FilenameParserService.BuildRegexPattern(patternText, string.IsNullOrWhiteSpace(rule.TimestampGroup) ? "stamp" : rule.TimestampGroup);
            var match = Regex.Match(actualFileName, regexText, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            var extension = Path.GetExtension(actualFileName) ?? string.Empty;
            var baseName = Path.GetFileNameWithoutExtension(actualFileName) ?? string.Empty;
            var counterMatch = Regex.Match(baseName, @"[_-]\d+$", RegexOptions.CultureInvariant);
            var counterValue = counterMatch.Success ? counterMatch.Value : string.Empty;
            var appIdValue = ReadGroup(match, string.IsNullOrWhiteSpace(rule.SteamAppIdGroup) ? "appid" : rule.SteamAppIdGroup);
            var titleValue = ReadGroup(match, string.IsNullOrWhiteSpace(rule.TitleGroup) ? "title" : rule.TitleGroup);
            var stampValue = ReadGroup(match, string.IsNullOrWhiteSpace(rule.TimestampGroup) ? "stamp" : rule.TimestampGroup);

            foreach (var segment in draft.Segments)
            {
                switch (segment.AssignedRole)
                {
                    case FilenameConventionBuilderComponentRole.SteamAppId:
                    case FilenameConventionBuilderComponentRole.NonSteamId:
                        if (!string.IsNullOrWhiteSpace(appIdValue)) segment.Text = appIdValue;
                        break;
                    case FilenameConventionBuilderComponentRole.Title:
                        if (!string.IsNullOrWhiteSpace(titleValue)) segment.Text = titleValue;
                        break;
                    case FilenameConventionBuilderComponentRole.Timestamp:
                        if (!string.IsNullOrWhiteSpace(stampValue)) segment.Text = stampValue;
                        break;
                    case FilenameConventionBuilderComponentRole.Counter:
                        if (!string.IsNullOrWhiteSpace(counterValue)) segment.Text = counterValue;
                        break;
                    case FilenameConventionBuilderComponentRole.Extension:
                        if (!string.IsNullOrWhiteSpace(extension)) segment.Text = extension;
                        break;
                }
            }

            if (!string.IsNullOrWhiteSpace(appIdValue))
            {
                var idRole = string.Equals(platformLabel, "Steam", StringComparison.OrdinalIgnoreCase)
                    ? FilenameConventionBuilderComponentRole.SteamAppId
                    : FilenameConventionBuilderComponentRole.NonSteamId;
                foreach (var segment in draft.Segments.Where(segment => segment.AssignedRole == FilenameConventionBuilderComponentRole.SteamAppId || segment.AssignedRole == FilenameConventionBuilderComponentRole.NonSteamId))
                {
                    segment.AssignedRole = idRole;
                    segment.SuggestedRole = idRole;
                }
            }
        }

        static string ReadGroup(Match match, string groupName)
        {
            if (match == null || string.IsNullOrWhiteSpace(groupName)) return string.Empty;
            var group = match.Groups[groupName];
            return group == null || !group.Success ? string.Empty : group.Value;
        }

        static bool UsesRole(FilenameConventionBuilderDraft draft, FilenameConventionBuilderComponentRole role)
        {
            return draft != null && draft.Segments.Any(segment => segment != null && segment.AssignedRole == role);
        }

        static bool UsesIdSegment(FilenameConventionBuilderDraft draft)
        {
            return UsesRole(draft, FilenameConventionBuilderComponentRole.SteamAppId)
                || UsesRole(draft, FilenameConventionBuilderComponentRole.NonSteamId);
        }

        static string BuildPatternTextFromDraft(FilenameConventionBuilderDraft draft)
        {
            if (draft == null) return string.Empty;
            var builder = new StringBuilder();
            var segments = draft.Segments.Where(segment => segment != null).ToList();
            for (var index = 0; index < segments.Count; index++)
            {
                var segment = segments[index];
                var nextSegment = index + 1 < segments.Count ? segments[index + 1] : null;
                switch (segment.AssignedRole)
                {
                    case FilenameConventionBuilderComponentRole.Title:
                        builder.Append("[title]");
                        break;
                    case FilenameConventionBuilderComponentRole.Timestamp:
                        builder.Append(TimestampTokenText(segment.Text, draft.TimestampFormat));
                        break;
                    case FilenameConventionBuilderComponentRole.SteamAppId:
                    case FilenameConventionBuilderComponentRole.NonSteamId:
                        builder.Append("[appid]");
                        break;
                    case FilenameConventionBuilderComponentRole.Counter:
                        builder.Append(DetermineCounterToken(segment.Text));
                        break;
                    case FilenameConventionBuilderComponentRole.Extension:
                        builder.Append(DetermineExtensionToken(segment.Text));
                        break;
                    default:
                        if ((segment.Text ?? string.Empty) == "."
                            && nextSegment != null
                            && nextSegment.AssignedRole == FilenameConventionBuilderComponentRole.Extension)
                        {
                            break;
                        }
                        builder.Append(segment.Text ?? string.Empty);
                        break;
                }
            }

            return builder.ToString();
        }

        static string DetermineCounterToken(string value)
        {
            var trimmed = value ?? string.Empty;
            if (trimmed.Contains("[opt-counter]", StringComparison.OrdinalIgnoreCase)) return "[opt-counter]";
            if (trimmed.Contains("[counter]", StringComparison.OrdinalIgnoreCase)) return "[counter]";
            return Regex.IsMatch(trimmed, @"^[_-]\d+$", RegexOptions.CultureInvariant) ? "[opt-counter]" : "[counter]";
        }

        static string DetermineExtensionToken(string value)
        {
            var trimmed = (value ?? string.Empty).Trim();
            if (trimmed.Contains("ext:image", StringComparison.OrdinalIgnoreCase)) return ".[ext:image]";
            if (trimmed.Contains("ext:video", StringComparison.OrdinalIgnoreCase)) return ".[ext:video]";
            if (trimmed.Contains("ext:media", StringComparison.OrdinalIgnoreCase) || trimmed.Contains("ext]", StringComparison.OrdinalIgnoreCase)) return ".[ext:media]";

            var extension = trimmed.StartsWith(".", StringComparison.Ordinal) ? trimmed : "." + trimmed.TrimStart('.');
            if (Regex.IsMatch(extension, @"^\.(png|jpe?g)$", RegexOptions.IgnoreCase)) return ".[ext:image]";
            if (Regex.IsMatch(extension, @"^\.(mp4|mkv|avi|mov|wmv|webm)$", RegexOptions.IgnoreCase)) return ".[ext:video]";
            return ".[ext:media]";
        }

        static string TimestampTokenText(string segmentText, string format)
        {
            if (!string.IsNullOrWhiteSpace(segmentText) && segmentText.Contains("[unixms]", StringComparison.OrdinalIgnoreCase))
            {
                return "[unixms]";
            }

            switch ((format ?? string.Empty).Trim())
            {
                case "yyyyMMddHHmmss":
                    return "[yyyy][MM][dd][HH][mm][ss]";
                case "yyyy-MM-dd hh-mm-ss tt":
                    return "[yyyy]-[MM]-[dd] [hh]-[mm]-[ss] [tt]";
                case "yyyy-MM-dd HH-mm-ss":
                    return "[yyyy]-[MM]-[dd] [HH]-[mm]-[ss]";
                case "yyyy-MM-dd HH.mm.ss":
                    return "[yyyy]-[MM]-[dd] [HH].[mm].[ss]";
                case "yyyy-MM-dd":
                    return "[yyyy]-[MM]-[dd]";
                case "yyyyMMdd":
                    return "[yyyy][MM][dd]";
                case "M_d_yyyy h_mm_ss tt":
                    return "[M]_[d]_[yyyy] [h]_[mm]_[ss] [tt]";
                case "unix-ms":
                    return "[unixms]";
                default:
                    TimestampDetection detection;
                    if (TryDetectTimestamp(segmentText, out detection))
                    {
                        return TimestampTokenText(segmentText, detection.Format);
                    }
                    return "[yyyy][MM][dd]";
            }
        }

        static string FirstNonEmpty(params string[] values)
        {
            return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
        }
    }
}
