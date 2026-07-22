using System.Collections.Concurrent;

namespace TwilightBoxart.Pipeline;

/// <summary>
/// Collapses concurrent work on the same key into one execution.
/// </summary>
/// <remarks>
/// The stampede this prevents is real and it points outward: a client starting a scan fires hundreds
/// of art requests at once, and a popular title arrives many times in the same second. Without
/// coalescing, every one of those becomes a separate GameTDB fetch of the same 700 KB JPEG. This is
/// per-process; a second instance would need a cross-instance lock, which is one of the stated
/// triggers for moving off SQLite.
/// </remarks>
public sealed class SingleFlight
{
    private readonly ConcurrentDictionary<string, Lazy<Task<object?>>> _inFlight = new(StringComparer.Ordinal);

    /// <summary>
    /// Runs <paramref name="factory"/> once per key, sharing the result with everyone who joins.
    /// </summary>
    /// <param name="key">Identifies the work. Concurrent callers with the same key share one execution.</param>
    /// <param name="factory">
    /// The shared work. Receives a token that is NOT any caller's; see the remarks.
    /// </param>
    /// <param name="ct">
    /// The CALLER's token. It governs how long this caller waits, not how long the shared work runs.
    /// </param>
    /// <remarks>
    /// That separation is the whole point, and getting it wrong is subtle. The factory is deliberately
    /// NOT given a caller's token: whoever arrives first would otherwise own the fetch for everybody,
    /// so a single client hitting stop during a library scan would cancel the shared task and hand an
    /// <see cref="OperationCanceledException"/> to every other caller waiting on that title: an HTTP
    /// 500 each, for a request nothing was wrong with. The shared work is instead bounded by the
    /// upstream HTTP timeout, while each caller observes only its own token via
    /// <c>Task.WaitAsync</c>.
    /// </remarks>
    public async Task<T> RunAsync<T>(string key, Func<CancellationToken, Task<T>> factory, CancellationToken ct = default)
    {
        var created = new Lazy<Task<object?>>(
            async () => (object?)await factory(CancellationToken.None).ConfigureAwait(false),
            LazyThreadSafetyMode.ExecutionAndPublication);

        var lazy = _inFlight.GetOrAdd(key, created);
        if (ReferenceEquals(lazy, created))
        {
            // De-register when the SHARED task completes - not when any one caller's wait does.
            // A caller that cancels out early must leave the entry in place, or the next arrival
            // would start a duplicate fetch alongside the one still running. Conditional remove of
            // this exact Lazy, because by then a later caller may have installed a fresh one.
            _ = lazy.Value.ContinueWith(
                _ => _inFlight.TryRemove(new KeyValuePair<string, Lazy<Task<object?>>>(key, lazy)),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        var result = await lazy.Value.WaitAsync(ct).ConfigureAwait(false);
        return (T)result!;
    }
}
