using System.Threading;

namespace PixelVaultNative
{
    /// <summary>Tracks overlapping foreground import / intake UI so the background agent can defer work (<c>PV-PLN-AINT-001</c> Slice 8–9).</summary>
    internal sealed class ForegroundIntakeBusyGate
    {
        int _depth;

        internal bool IsBusy => Volatile.Read(ref _depth) != 0;

        internal void Enter() => Interlocked.Increment(ref _depth);

        internal void Leave() => Interlocked.Decrement(ref _depth);
    }
}
