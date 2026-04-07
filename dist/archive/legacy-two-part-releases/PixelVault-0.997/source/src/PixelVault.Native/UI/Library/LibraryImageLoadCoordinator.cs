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
            var cores = Environment.ProcessorCount;
            _normal = new SemaphoreSlim(Math.Max(4, Math.Min(Math.Max(cores * 2, 6), 14)));
            _priority = new SemaphoreSlim(Math.Max(3, Math.Min(Math.Max(cores + 2, 5), 8)));
        }

        public SemaphoreSlim GetLimiter(bool prioritize) => prioritize ? _priority : _normal;
    }
}
