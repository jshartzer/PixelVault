#nullable enable

using System;

namespace PixelVaultNative
{
    /// <summary>v1 <see cref="ILogService"/> backed by <see cref="TroubleshootingLog"/> (same file + line shape as before A.6).</summary>
    internal sealed class TroubleshootingLogService : ILogService
    {
        readonly TroubleshootingLog _troubleshootingLog;

        public TroubleshootingLogService(TroubleshootingLog troubleshootingLog)
        {
            _troubleshootingLog = troubleshootingLog ?? throw new ArgumentNullException(nameof(troubleshootingLog));
        }

        public string AppendMainLine(string? message) => _troubleshootingLog.AppendMainLine(message);
    }
}
