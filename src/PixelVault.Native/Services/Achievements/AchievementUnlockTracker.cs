using System;
using System.Collections.Generic;
using System.Linq;

namespace PixelVaultNative
{
    // An observation snapshot, not persisted unlock history. Missing/unknown rows cannot prove a transition.
    sealed class AchievementUnlockTracker
    {
        internal static (string Provider, string Game, string Achievement) Identity(GameAchievementsFetchService.AchievementRow row) =>
            ((row.Provider ?? "").Trim().ToLowerInvariant(), (row.ProviderGameId ?? "").Trim(), (row.ProviderAchievementId ?? "").Trim());

        Dictionary<(string, string, string), bool> _previous = new Dictionary<(string, string, string), bool>();
        string _scope;

        internal HashSet<(string, string, string)> Observe(GameAchievementsFetchService.FetchResult result, string accountScope)
        {
            var earned = new HashSet<(string, string, string)>();
            if (result == null || result.IsError || !string.IsNullOrEmpty(result.ProgressErrorMessage)) return earned;
            if (!string.Equals(_scope, accountScope, StringComparison.Ordinal)) _previous.Clear();
            _scope = accountScope;
            var current = new Dictionary<(string, string, string), bool>();
            foreach (var group in (result.Rows ?? new List<GameAchievementsFetchService.AchievementRow>())
                .Where(row => row?.HasStableProviderIdentity == true).GroupBy(Identity))
            {
                if (group.Any(row => !row.ProgressKnown) || group.Select(row => row.Unlocked).Distinct().Count() != 1) continue;
                var unlocked = group.First().Unlocked;
                current[group.Key] = unlocked;
                if (unlocked && _previous.TryGetValue(group.Key, out var wasUnlocked) && !wasUnlocked) earned.Add(group.Key);
            }
            _previous = current;
            return earned;
        }
    }
}
