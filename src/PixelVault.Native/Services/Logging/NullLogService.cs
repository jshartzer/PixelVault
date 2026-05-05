#nullable enable

using System.Collections.Generic;

namespace PixelVaultNative
{
    /// <summary>Discards main-log lines (tests and minimal harnesses).</summary>
    internal sealed class NullLogService : ILogService
    {
        public static readonly NullLogService Instance = new NullLogService();

        NullLogService() { }

        public string AppendMainLine(string? message) => string.Empty;

        public string[] AppendMainLines(IEnumerable<string?> messages) => System.Array.Empty<string>();
    }
}
