using System.Collections.Concurrent;
using TwilightBoxart.Data.Extensions;

namespace TwilightBoxart.Pipeline.Caching;

/// <summary>
/// Buffers cache-hit accounting so the request path never writes bookkeeping.
/// </summary>
/// <remarks>
/// The memory half of the cache accounting design - see <see cref="Data.Entities.CacheEntry"/> for
/// the hot-path rule. Keyed on the entry's CacheKey so the drained batch is already in the shape
/// <see cref="CacheEntryExtensions.FlushHitsAsync"/> wants.
/// </remarks>
public sealed class CacheAccessBuffer
{
    private readonly ConcurrentDictionary<string, Counter> _pending = new(StringComparer.Ordinal);

    public void Record(string cacheKey)
    {
        var counter = _pending.GetOrAdd(cacheKey, static _ => new Counter());
        Interlocked.Increment(ref counter.Hits);
        Interlocked.Exchange(ref counter.LastAccessTicks, DateTime.UtcNow.Ticks);
    }

    /// <summary>Empties the buffer and returns what was in it, ready to hand to the flush.</summary>
    public IReadOnlyDictionary<string, BufferedHits> Drain()
    {
        var result = new Dictionary<string, BufferedHits>(StringComparer.Ordinal);
        foreach (var key in _pending.Keys)
        {
            if (!_pending.TryRemove(key, out var counter))
            {
                continue;
            }

            result[key] = new BufferedHits(
                Interlocked.Read(ref counter.Hits),
                new DateTime(Interlocked.Read(ref counter.LastAccessTicks), DateTimeKind.Utc));
        }

        return result;
    }

    private sealed class Counter
    {
        public long Hits;
        public long LastAccessTicks;
    }
}
