using System;
using System.Threading;

namespace PixelVaultNative
{
    /// <summary>
    /// Concurrency limits for background bitmap decode (normal vs priority). Used by <see cref="MainWindow.QueueImageLoad"/>.
    /// </summary>
    internal sealed class LibraryImageLoadCoordinator
    {
        readonly SemaphoreSlim _normal;
        readonly SemaphoreSlim _priority;

        public LibraryImageLoadCoordinator()
        {
            _normal = new SemaphoreSlim(Math.Max(2, Math.Min(Environment.ProcessorCount, 6)));
            _priority = new SemaphoreSlim(Math.Max(1, Math.Min(Environment.ProcessorCount, 3)));
        }

        public SemaphoreSlim GetLimiter(bool prioritize) => prioritize ? _priority : _normal;
    }
}
