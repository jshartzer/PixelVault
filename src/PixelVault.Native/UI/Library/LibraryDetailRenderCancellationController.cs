using System;
using System.Threading;

namespace PixelVaultNative
{
    internal sealed class LibraryDetailRenderCancellationController : IDisposable
    {
        readonly object gate = new object();
        CancellationTokenSource currentSource;
        int currentRenderVersion;

        public CancellationToken BeginRender(int renderVersion)
        {
            var next = new CancellationTokenSource();
            CancellationTokenSource previous;
            lock (gate)
            {
                previous = currentSource;
                currentSource = next;
                currentRenderVersion = renderVersion;
            }

            CancelAndDispose(previous);
            return next.Token;
        }

        public bool IsCurrent(int renderVersion, CancellationToken cancellationToken)
        {
            if (!cancellationToken.CanBeCanceled || cancellationToken.IsCancellationRequested) return false;
            lock (gate)
            {
                return currentSource != null
                    && currentRenderVersion == renderVersion
                    && cancellationToken.Equals(currentSource.Token);
            }
        }

        public void CancelCurrent()
        {
            CancellationTokenSource previous;
            lock (gate)
            {
                previous = currentSource;
                currentSource = null;
                currentRenderVersion = 0;
            }

            CancelAndDispose(previous);
        }

        public void Dispose()
        {
            CancelCurrent();
        }

        static void CancelAndDispose(CancellationTokenSource source)
        {
            if (source == null) return;
            try
            {
                source.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
            finally
            {
                source.Dispose();
            }
        }
    }
}
