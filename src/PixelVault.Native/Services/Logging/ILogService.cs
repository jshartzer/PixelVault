#nullable enable

namespace PixelVaultNative
{
    /// <summary>
    /// Application session log (PV-PLN-EXT-002 A.6). v1 delegates to <see cref="TroubleshootingLog.AppendMainLine"/>;
    /// WPF <c>logBox</c> mirroring stays on <see cref="MainWindow"/>.
    /// </summary>
    internal interface ILogService
    {
        /// <inheritdoc cref="TroubleshootingLog.AppendMainLine"/>
        string AppendMainLine(string? message);
    }
}
