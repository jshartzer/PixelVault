#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;

namespace PixelVaultNative
{
    /// <summary>
    /// Shared Steam store-title resolution for import paths (PV-PLN-UI-001 Step 7).
    /// Preserves behavior: when a sync resolver is injected, it is used exclusively (no async fallback if it returns empty).
    /// </summary>
    internal static class CoverWorkflowHelpers
    {
        public static async Task<string> ResolveSteamStoreTitleForAppIdAsync(
            string appId,
            Func<string, string>? syncResolve,
            ICoverService? cover,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(appId)) return string.Empty;
            if (syncResolve != null)
                return syncResolve(appId) ?? string.Empty;
            if (cover == null) return string.Empty;
            var name = await cover.SteamNameAsync(appId, cancellationToken).ConfigureAwait(false);
            return name ?? string.Empty;
        }
    }
}
