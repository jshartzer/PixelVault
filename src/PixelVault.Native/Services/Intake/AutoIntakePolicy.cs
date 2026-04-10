using System;
using System.Collections.Generic;
using System.Linq;

namespace PixelVaultNative
{
    /// <summary>Which files may be imported by the background intake agent.</summary>
    internal static class AutoIntakePolicy
    {
        public static FilenameConventionRule TryResolveMatchedRule(IReadOnlyList<FilenameConventionRule> rules, FilenameParseResult parsed)
        {
            if (rules == null || parsed == null || !parsed.MatchedConvention) return null;
            var id = parsed.ConventionId ?? string.Empty;
            return rules.FirstOrDefault(r =>
                r != null
                && r.Enabled
                && string.Equals(r.ConventionId ?? string.Empty, id, StringComparison.OrdinalIgnoreCase));
        }

        public static bool IsEligibleForBackgroundAutoImport(IntakePreviewFileAnalysis analysis, FilenameConventionRule matchedRule)
        {
            if (analysis == null || matchedRule == null) return false;
            if (!analysis.CanUpdateMetadata) return false;
            var parsed = analysis.Parsed ?? new FilenameParseResult();
            if (!parsed.MatchedConvention) return false;
            if (!matchedRule.Enabled) return false;
            if (!string.Equals(parsed.ConventionId ?? string.Empty, matchedRule.ConventionId ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                return false;

            if (matchedRule.IsBuiltIn) return true;

            return string.Equals(
                FilenameAutoIntakeModes.Normalize(matchedRule.AutoIntakeMode),
                FilenameAutoIntakeModes.TrustedExactMatch,
                StringComparison.Ordinal);
        }

        /// <summary>When the file is not eligible for background import, returns a short reason for diagnostic logs; otherwise null.</summary>
        public static string TryGetIneligibilityReason(IntakePreviewFileAnalysis analysis, FilenameConventionRule matchedRule)
        {
            if (analysis == null) return "missing_analysis";
            if (!analysis.CanUpdateMetadata) return "cannot_update_metadata";
            var parsed = analysis.Parsed ?? new FilenameParseResult();
            if (!parsed.MatchedConvention) return "filename_not_matched_to_convention";
            if (matchedRule == null)
                return "no_enabled_rule_for_convention_id:" + (parsed.ConventionId ?? string.Empty);
            if (!matchedRule.Enabled) return "rule_disabled";
            if (!string.Equals(parsed.ConventionId ?? string.Empty, matchedRule.ConventionId ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                return "convention_id_mismatch";
            if (!matchedRule.IsBuiltIn
                && !string.Equals(
                    FilenameAutoIntakeModes.Normalize(matchedRule.AutoIntakeMode),
                    FilenameAutoIntakeModes.TrustedExactMatch,
                    StringComparison.Ordinal))
                return "custom_rule_not_trusted_exact_match";
            return null;
        }
    }
}
