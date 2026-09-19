using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace PixelVaultNative
{
    sealed class AchievementGuideImportPreview
    {
        public int SchemaVersion;
        public string Provider;
        public string ProviderGameId;
        public int RequestedCount;
        public int MatchedCount;
        public int ChangedCount;
        public int UnchangedCount;
        public List<string> UnmatchedAchievementIds = new List<string>();
        public List<string> ValidationErrors = new List<string>();

        public bool IsValid => ValidationErrors.Count == 0;
        public bool CanImport => IsValid && MatchedCount > 0;
    }

    sealed class AchievementGuideImportResult
    {
        public int ImportedCount;
        public int UnchangedCount;
        public List<string> UnmatchedAchievementIds = new List<string>();
    }

    sealed class AchievementGuideImportBundle
    {
        public int SchemaVersion { get; set; }
        public string Provider { get; set; }
        public string ProviderGameId { get; set; }
        public string SourceUrl { get; set; }
        public string SourceTitle { get; set; }
        public List<AchievementGuideImportItem> Achievements { get; set; }
    }

    sealed class AchievementGuideImportItem
    {
        public string ProviderAchievementId { get; set; }
        public string GuideText { get; set; }
        public List<string> Tags { get; set; }
        public bool? IsMissable { get; set; }
        public string SourceUrl { get; set; }
        public string SourceTitle { get; set; }
        public string GroupName { get; set; }
        public int? GroupSortOrder { get; set; }
    }

    sealed class AchievementGuideImportCandidate
    {
        public long AchievementId;
        public string ProviderAchievementId;
        public string GuideText;
        public string SourceUrl;
        public string SourceTitle;
        public string Tags;
        public bool IsMissable;
        public string GroupName;
        public int GroupSortOrder;
        public bool GuideChanged;
        public bool GroupChanged;
        public bool Changed;
    }

    sealed partial class AchievementGuideService
    {
        const int GuideImportSchemaVersion = 1;
        const int GuideImportMaxCharacters = 2_000_000;

        sealed class ParsedGuideImport
        {
            public AchievementGuideImportBundle Bundle;
            public AchievementGuideImportPreview Preview;
            public List<AchievementGuideImportCandidate> Candidates = new List<AchievementGuideImportCandidate>();
        }

        public AchievementGuideImportPreview PreviewGuideImport(
            string json,
            string expectedProvider,
            string expectedProviderGameId)
        {
            using (var connection = OpenDatabase())
                return ParseAndMatchGuideImport(connection, json, expectedProvider, expectedProviderGameId).Preview;
        }

        public AchievementGuideImportResult ImportGuideBundle(
            string json,
            string expectedProvider,
            string expectedProviderGameId)
        {
            ParsedGuideImport parsed;
            using (var connection = OpenDatabase())
                parsed = ParseAndMatchGuideImport(connection, json, expectedProvider, expectedProviderGameId);

            if (!parsed.Preview.IsValid)
                throw new InvalidOperationException("The guide bundle is invalid: " + string.Join(" ", parsed.Preview.ValidationErrors));

            var changed = parsed.Candidates.Where(candidate => candidate.Changed).ToList();
            if (changed.Count > 0)
            {
                var now = DateTime.UtcNow.Ticks;
                using (var connection = OpenDatabase())
                using (var transaction = connection.BeginTransaction())
                {
                    foreach (var candidate in changed)
                    {
                        if (candidate.GuideChanged)
                        {
                            using (var command = connection.CreateCommand())
                            {
                                command.Transaction = transaction;
                                command.CommandText = @"
INSERT INTO achievement_guide
    (achievement_id, guide_text, source_url, source_title, tags, is_missable, created_utc_ticks, updated_utc_ticks)
VALUES
    ($achievement_id, $guide_text, $source_url, $source_title, $tags, $is_missable, $now, $now)
ON CONFLICT(achievement_id) DO UPDATE SET
    guide_text = excluded.guide_text,
    source_url = excluded.source_url,
    source_title = excluded.source_title,
    tags = excluded.tags,
    is_missable = excluded.is_missable,
    updated_utc_ticks = excluded.updated_utc_ticks;";
                                command.Parameters.AddWithValue("$achievement_id", candidate.AchievementId);
                                command.Parameters.AddWithValue("$guide_text", candidate.GuideText);
                                command.Parameters.AddWithValue("$source_url", candidate.SourceUrl);
                                command.Parameters.AddWithValue("$source_title", candidate.SourceTitle);
                                command.Parameters.AddWithValue("$tags", candidate.Tags);
                                command.Parameters.AddWithValue("$is_missable", candidate.IsMissable ? 1 : 0);
                                command.Parameters.AddWithValue("$now", now);
                                command.ExecuteNonQuery();
                            }
                        }

                        if (candidate.GroupChanged)
                        {
                            using (var command = connection.CreateCommand())
                            {
                                command.Transaction = transaction;
                                command.CommandText = @"
UPDATE achievement_definition
SET group_name = $group_name,
    group_sort_order = $group_sort_order
WHERE achievement_id = $achievement_id;";
                                command.Parameters.AddWithValue("$achievement_id", candidate.AchievementId);
                                command.Parameters.AddWithValue("$group_name", candidate.GroupName);
                                command.Parameters.AddWithValue("$group_sort_order", candidate.GroupSortOrder);
                                command.ExecuteNonQuery();
                            }
                        }
                    }
                    transaction.Commit();
                }
                TryCreateRollingBackup();
            }

            return new AchievementGuideImportResult
            {
                ImportedCount = changed.Count,
                UnchangedCount = parsed.Preview.UnchangedCount,
                UnmatchedAchievementIds = parsed.Preview.UnmatchedAchievementIds.ToList()
            };
        }

        ParsedGuideImport ParseAndMatchGuideImport(
            SqliteConnection connection,
            string json,
            string expectedProvider,
            string expectedProviderGameId)
        {
            var result = new ParsedGuideImport { Preview = new AchievementGuideImportPreview() };
            var raw = json ?? string.Empty;
            if (raw.Length == 0)
            {
                result.Preview.ValidationErrors.Add("The guide bundle is empty.");
                return result;
            }
            if (raw.Length > GuideImportMaxCharacters)
            {
                result.Preview.ValidationErrors.Add("The guide bundle is larger than the 2,000,000 character limit.");
                return result;
            }

            try
            {
                result.Bundle = JsonSerializer.Deserialize<AchievementGuideImportBundle>(raw, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch (JsonException ex)
            {
                result.Preview.ValidationErrors.Add("The guide bundle is not valid JSON: " + ex.Message);
                return result;
            }

            var bundle = result.Bundle;
            if (bundle == null)
            {
                result.Preview.ValidationErrors.Add("The guide bundle is empty.");
                return result;
            }

            var provider = NormalizeProvider(bundle.Provider);
            var providerGameId = (bundle.ProviderGameId ?? string.Empty).Trim();
            result.Preview.SchemaVersion = bundle.SchemaVersion;
            result.Preview.Provider = provider;
            result.Preview.ProviderGameId = providerGameId;
            result.Preview.RequestedCount = bundle.Achievements?.Count ?? 0;

            if (bundle.SchemaVersion != GuideImportSchemaVersion)
                result.Preview.ValidationErrors.Add("Unsupported schemaVersion " + bundle.SchemaVersion + "; expected 1.");
            if (provider != GameAchievementsFetchService.SteamProvider
                && provider != GameAchievementsFetchService.RetroAchievementsProvider)
                result.Preview.ValidationErrors.Add("Provider must be steam or retroachievements.");
            if (providerGameId.Length == 0)
                result.Preview.ValidationErrors.Add("providerGameId is required.");

            var expectedProviderNormalized = NormalizeProvider(expectedProvider);
            var expectedGameNormalized = (expectedProviderGameId ?? string.Empty).Trim();
            if (expectedProviderNormalized.Length > 0
                && !string.Equals(provider, expectedProviderNormalized, StringComparison.Ordinal))
                result.Preview.ValidationErrors.Add("Bundle provider does not match the open game.");
            if (expectedGameNormalized.Length > 0
                && !string.Equals(providerGameId, expectedGameNormalized, StringComparison.Ordinal))
                result.Preview.ValidationErrors.Add("Bundle providerGameId does not match the open game.");

            if (!IsOptionalHttpUrl(bundle.SourceUrl))
                result.Preview.ValidationErrors.Add("Bundle sourceUrl must be a complete http:// or https:// address.");
            if (bundle.Achievements == null || bundle.Achievements.Count == 0)
                result.Preview.ValidationErrors.Add("At least one achievement entry is required.");

            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in bundle.Achievements ?? new List<AchievementGuideImportItem>())
            {
                var id = (item?.ProviderAchievementId ?? string.Empty).Trim();
                if (id.Length == 0)
                    result.Preview.ValidationErrors.Add("Every achievement entry requires providerAchievementId.");
                else if (!seenIds.Add(id))
                    result.Preview.ValidationErrors.Add("Duplicate providerAchievementId: " + id + ".");
                var hasGuidePayload = item != null && (item.GuideText != null
                    || item.Tags != null
                    || item.IsMissable.HasValue
                    || item.SourceUrl != null
                    || item.SourceTitle != null);
                var hasGroupPayload = item != null && (item.GroupName != null || item.GroupSortOrder.HasValue);
                if (!hasGuidePayload && !hasGroupPayload)
                    result.Preview.ValidationErrors.Add("Achievement " + (id.Length == 0 ? "(missing ID)" : id) + " requires guide or group metadata.");
                if (hasGuidePayload && string.IsNullOrWhiteSpace(item?.GuideText))
                    result.Preview.ValidationErrors.Add("Achievement " + (id.Length == 0 ? "(missing ID)" : id) + " requires guideText when guide metadata is supplied.");
                if (!IsOptionalHttpUrl(item?.SourceUrl))
                    result.Preview.ValidationErrors.Add("Achievement " + (id.Length == 0 ? "(missing ID)" : id) + " has an invalid sourceUrl.");
                if (item?.GroupName != null)
                {
                    try { NormalizeGroupName(item.GroupName); }
                    catch (ArgumentException ex) { result.Preview.ValidationErrors.Add("Achievement " + (id.Length == 0 ? "(missing ID)" : id) + ": " + ex.Message); }
                }
                if (item?.GroupSortOrder.HasValue == true && (item.GroupSortOrder.Value < 0 || item.GroupSortOrder.Value > 100000))
                    result.Preview.ValidationErrors.Add("Achievement " + (id.Length == 0 ? "(missing ID)" : id) + " has groupSortOrder outside 0-100000.");
            }

            if (result.Preview.ValidationErrors.Count > 0) return result;

            var catalog = LoadEntriesForProviderGame(connection, provider, providerGameId)
                .ToDictionary(entry => entry.ProviderAchievementId, StringComparer.Ordinal);
            foreach (var item in bundle.Achievements)
            {
                var id = item.ProviderAchievementId.Trim();
                if (!catalog.TryGetValue(id, out var existing))
                {
                    result.Preview.UnmatchedAchievementIds.Add(id);
                    continue;
                }

                var hasGuidePayload = item.GuideText != null
                    || item.Tags != null
                    || item.IsMissable.HasValue
                    || item.SourceUrl != null
                    || item.SourceTitle != null;
                var guideText = hasGuidePayload ? (item.GuideText ?? string.Empty).Trim() : existing.GuideText ?? string.Empty;
                var sourceUrl = hasGuidePayload ? FirstNonBlank(item.SourceUrl, bundle.SourceUrl) : existing.SourceUrl ?? string.Empty;
                var sourceTitle = hasGuidePayload ? FirstNonBlank(item.SourceTitle, bundle.SourceTitle) : existing.SourceTitle ?? string.Empty;
                var tags = hasGuidePayload ? NormalizeTags(string.Join(", ", item.Tags ?? new List<string>())) : existing.Tags ?? string.Empty;
                var isMissable = hasGuidePayload ? item.IsMissable == true : existing.IsMissable;
                var groupName = item.GroupName == null ? existing.GroupName ?? string.Empty : NormalizeGroupName(item.GroupName);
                var groupSortOrder = item.GroupSortOrder ?? existing.GroupSortOrder;
                var guideChanged = hasGuidePayload && (!string.Equals(guideText, existing.GuideText ?? string.Empty, StringComparison.Ordinal)
                    || !string.Equals(sourceUrl, existing.SourceUrl ?? string.Empty, StringComparison.Ordinal)
                    || !string.Equals(sourceTitle, existing.SourceTitle ?? string.Empty, StringComparison.Ordinal)
                    || !string.Equals(tags, existing.Tags ?? string.Empty, StringComparison.Ordinal)
                    || isMissable != existing.IsMissable);
                var groupChanged = (item.GroupName != null || item.GroupSortOrder.HasValue)
                    && (!string.Equals(groupName, existing.GroupName ?? string.Empty, StringComparison.Ordinal)
                        || groupSortOrder != existing.GroupSortOrder);

                result.Candidates.Add(new AchievementGuideImportCandidate
                {
                    AchievementId = existing.AchievementId,
                    ProviderAchievementId = id,
                    GuideText = guideText,
                    SourceUrl = sourceUrl,
                    SourceTitle = sourceTitle,
                    Tags = tags,
                    IsMissable = isMissable,
                    GroupName = groupName,
                    GroupSortOrder = groupSortOrder,
                    GuideChanged = guideChanged,
                    GroupChanged = groupChanged,
                    Changed = guideChanged || groupChanged
                });
                result.Preview.MatchedCount++;
                if (guideChanged || groupChanged) result.Preview.ChangedCount++;
                else result.Preview.UnchangedCount++;
            }
            return result;
        }

        static bool IsOptionalHttpUrl(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return true;
            return Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        }

        static string FirstNonBlank(string primary, string fallback)
        {
            return !string.IsNullOrWhiteSpace(primary)
                ? primary.Trim()
                : (fallback ?? string.Empty).Trim();
        }

        static List<AchievementGuideEntry> LoadEntriesForProviderGame(
            SqliteConnection connection,
            string provider,
            string providerGameId)
        {
            var entries = new List<AchievementGuideEntry>();
            using (var command = CreateEntrySelectCommand(connection))
            {
                command.CommandText += @"
WHERE d.provider = $provider AND d.provider_game_id = $provider_game_id;";
                command.Parameters.AddWithValue("$provider", provider);
                command.Parameters.AddWithValue("$provider_game_id", providerGameId);
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read()) entries.Add(ReadEntry(reader));
                }
            }
            return entries;
        }
    }
}
