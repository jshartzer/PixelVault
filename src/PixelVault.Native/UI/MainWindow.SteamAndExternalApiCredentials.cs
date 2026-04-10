using System;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        string FindSteamGridDbApiTokenInEnvironment() => SettingsService.FindSteamGridDbApiTokenInEnvironment();
        string CurrentSteamGridDbApiToken()
        {
            var envToken = FindSteamGridDbApiTokenInEnvironment();
            return !string.IsNullOrWhiteSpace(envToken) ? envToken : (steamGridDbApiToken ?? string.Empty).Trim();
        }
        bool HasSteamGridDbApiToken()
        {
            return !string.IsNullOrWhiteSpace(CurrentSteamGridDbApiToken());
        }
        string CurrentSteamWebApiKey()
        {
            var env = SettingsService.FindSteamWebApiKeyInEnvironment();
            return !string.IsNullOrWhiteSpace(env) ? env : (steamWebApiKey ?? string.Empty).Trim();
        }
        bool HasSteamWebApiKey() => !string.IsNullOrWhiteSpace(CurrentSteamWebApiKey());
        string CurrentRetroAchievementsApiKey()
        {
            var env = SettingsService.FindRetroAchievementsApiKeyInEnvironment();
            return !string.IsNullOrWhiteSpace(env) ? env : (retroAchievementsApiKey ?? string.Empty).Trim();
        }
        bool HasRetroAchievementsApiKey() => !string.IsNullOrWhiteSpace(CurrentRetroAchievementsApiKey());
        string CurrentSteamUserId64()
        {
            var env = SettingsService.FindSteamUserId64InEnvironment();
            return !string.IsNullOrWhiteSpace(env) ? env : (steamUserId64 ?? string.Empty).Trim();
        }
        string CurrentRetroAchievementsUsername()
        {
            var env = SettingsService.FindRetroAchievementsUsernameInEnvironment();
            return !string.IsNullOrWhiteSpace(env) ? env : (retroAchievementsUsername ?? string.Empty).Trim();
        }
        bool IsClearedExternalIdValue(string value)
        {
            return string.Equals((value ?? string.Empty).Trim(), ClearedExternalIdSentinel, StringComparison.Ordinal);
        }
        string DisplayExternalIdValue(string value)
        {
            return IsClearedExternalIdValue(value) ? string.Empty : CleanTag(value);
        }
        string SerializeExternalIdValue(string value, bool suppressAutoResolve)
        {
            return suppressAutoResolve ? ClearedExternalIdSentinel : CleanTag(value);
        }
        bool ShouldSuppressExternalIdAutoResolve(string editedValue, string previousValue, bool previousSuppressed)
        {
            var cleanedEdited = CleanTag(editedValue);
            if (!string.IsNullOrWhiteSpace(cleanedEdited)) return false;
            return previousSuppressed || !string.IsNullOrWhiteSpace(CleanTag(previousValue));
        }
    }
}
