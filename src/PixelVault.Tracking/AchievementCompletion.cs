namespace PixelVault.Tracking;

public enum AchievementCompletionTier { None, Base, BaseAndDlc, BaseAndDlcAndPv }

public readonly record struct AchievementCompletionProgress(bool Known, bool Unlocked, bool IsDlc);

/// <summary>Only known, earned achievements qualify. Games without DLC retain the base award.</summary>
public static class AchievementCompletion
{
    public static AchievementCompletionTier Classify(IEnumerable<AchievementCompletionProgress> progress, bool markedComplete = false,
        IEnumerable<AchievementCompletionProgress> pvProgress = null)
    {
        var rows = progress?.ToArray() ?? [];
        if (rows.Length == 0) return markedComplete ? AchievementCompletionTier.Base : AchievementCompletionTier.None;
        var baseRows = rows.Where(r => !r.IsDlc).ToArray();
        if (baseRows.Length == 0 || baseRows.Any(r => !r.Known || !r.Unlocked))
            return AchievementCompletionTier.None;
        var dlc = rows.Where(r => r.IsDlc).ToArray();
        // PV is a separate, explicit set. Empty sets and legacy completion flags never award diamond.
        var pv = pvProgress?.ToArray() ?? [];
        if (dlc.All(r => r.Known && r.Unlocked) && pv.Length > 0 && pv.All(r => r.Known && r.Unlocked))
            return AchievementCompletionTier.BaseAndDlcAndPv;
        return dlc.Length > 0 && dlc.All(r => r.Known && r.Unlocked)
            ? AchievementCompletionTier.BaseAndDlc : AchievementCompletionTier.Base;
    }

    public static string Name(AchievementCompletionTier tier) => tier switch
    {
        AchievementCompletionTier.Base => "base",
        AchievementCompletionTier.BaseAndDlc => "base-dlc",
        AchievementCompletionTier.BaseAndDlcAndPv => "base-dlc-pv",
        _ => "none"
    };
}
