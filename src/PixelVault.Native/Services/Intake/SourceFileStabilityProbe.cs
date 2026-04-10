using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PixelVaultNative
{
    /// <summary>Waits until a file’s length and last write time stop changing for <paramref name="quietMilliseconds"/>.</summary>
    internal static class SourceFileStabilityProbe
    {
        public static async Task<bool> WaitUntilStableAsync(
            string path,
            int quietMilliseconds,
            TimeSpan maxWait,
            CancellationToken cancellationToken,
            int pollMilliseconds = 250)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            if (quietMilliseconds < 1) quietMilliseconds = 1;
            if (pollMilliseconds < 50) pollMilliseconds = 50;

            var deadline = DateTime.UtcNow + maxWait;
            long lastLen = -1;
            DateTime lastWriteUtc = DateTime.MinValue;
            var stableSinceUtc = DateTime.UtcNow;

            while (DateTime.UtcNow <= deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!File.Exists(path)) return false;

                try
                {
                    var info = new FileInfo(path);
                    info.Refresh();
                    var len = info.Length;
                    var w = info.LastWriteTimeUtc;
                    if (len != lastLen || w != lastWriteUtc)
                    {
                        lastLen = len;
                        lastWriteUtc = w;
                        stableSinceUtc = DateTime.UtcNow;
                    }
                    else if ((DateTime.UtcNow - stableSinceUtc).TotalMilliseconds >= quietMilliseconds)
                        return true;
                }
                catch
                {
                    return false;
                }

                await Task.Delay(pollMilliseconds, cancellationToken).ConfigureAwait(false);
            }

            return false;
        }
    }
}
