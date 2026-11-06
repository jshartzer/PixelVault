using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PixelVaultNative
{
    // Process-local, bounded cache of serialized observations, never decrypted saves.
    // One gate limits expensive analyses even when different trackers refresh together.
    sealed class SharedSaveAnalysisCache
    {
        readonly SemaphoreSlim gate = new(1, 1);
        readonly Dictionary<string, (string Value, DateTime Expires, long Used)> entries = new(StringComparer.Ordinal);
        readonly int capacity;
        readonly Func<DateTime> clock;
        long sequence;
        internal SharedSaveAnalysisCache(int capacity = 4, Func<DateTime> clock = null)
        {
            if (capacity < 1 || capacity > 16) throw new ArgumentOutOfRangeException(nameof(capacity));
            this.capacity = capacity; this.clock = clock ?? (() => DateTime.UtcNow);
        }
        internal async Task<string> GetAsync(string key, Func<CancellationToken, Task<string>> analyze, CancellationToken token)
        {
            await gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                token.ThrowIfCancellationRequested();
                var now = clock();
                if (entries.TryGetValue(key, out var entry) && entry.Expires > now)
                {
                    entries[key] = (entry.Value, entry.Expires, ++sequence);
                    return entry.Value;
                }
                entries.Remove(key);
                var value = await analyze(token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (value == null || value.Length > 4 * 1024 * 1024) throw new InvalidDataException("The shared save analysis is too large or missing.");
                foreach (var old in new List<string>(entries.Keys)) if (entries[old].Expires <= clock()) entries.Remove(old);
                if (entries.Count >= capacity)
                {
                    string oldest = null; long used = long.MaxValue;
                    foreach (var pair in entries) if (pair.Value.Used < used) { oldest = pair.Key; used = pair.Value.Used; }
                    entries.Remove(oldest);
                }
                entries[key] = (value, clock().AddMinutes(30), ++sequence);
                return value;
            }
            finally { gate.Release(); }
        }
    }
}
