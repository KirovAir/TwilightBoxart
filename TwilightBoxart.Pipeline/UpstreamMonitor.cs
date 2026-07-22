using System.Collections.Concurrent;

namespace TwilightBoxart.Pipeline;

/// <summary>
/// Per-source success/miss/failure counters behind <c>GET /v2/health</c>.
/// </summary>
/// <remarks>
/// Exists because of a specific 2020 failure mode: when the previous backend host stopped resolving,
/// the tool reported "Finished scan." and nothing else, so a total outage and a library of genuinely
/// unmatched ROMs looked identical from outside. Counting misses separately from failures is the
/// whole point - a miss is normal (59.6% of the DAT has no serial), a failure is not.
/// </remarks>
public sealed class UpstreamMonitor
{
    private readonly ConcurrentDictionary<string, State> _sources = new(StringComparer.Ordinal);

    public void RecordSuccess(string source)
    {
        var state = Get(source);
        Interlocked.Increment(ref state.Successes);
        state.LastSuccess = DateTimeOffset.UtcNow;
        state.LastError = null;
    }

    /// <summary>The source answered, and it has no art for this game. Normal, not an outage.</summary>
    public void RecordMiss(string source) => Interlocked.Increment(ref Get(source).Misses);

    public void RecordFailure(string source, Exception exception)
    {
        var state = Get(source);
        Interlocked.Increment(ref state.Failures);
        state.LastFailure = DateTimeOffset.UtcNow;
        // Message only: an exception's ToString can carry the full outbound URL and stack, and this
        // payload is served unauthenticated.
        state.LastError = exception.Message;
    }

    public IReadOnlyList<UpstreamHealth> Snapshot() =>
    [
        .. _sources.Select(kv => new UpstreamHealth(
            kv.Key,
            IsHealthy(kv.Value),
            Interlocked.Read(ref kv.Value.Successes),
            Interlocked.Read(ref kv.Value.Misses),
            Interlocked.Read(ref kv.Value.Failures),
            kv.Value.LastSuccess,
            kv.Value.LastFailure,
            kv.Value.LastError))
        .OrderBy(h => h.Name, StringComparer.Ordinal)
    ];

    /// <summary>
    /// A source counts as unhealthy only once it has failed and has not succeeded since. One 404 in a
    /// row of hits should not paint the dashboard red.
    /// </summary>
    private static bool IsHealthy(State state) =>
        state.LastFailure is null || (state.LastSuccess is not null && state.LastSuccess > state.LastFailure);

    private State Get(string source) => _sources.GetOrAdd(source, static _ => new State());

    private sealed class State
    {
        public long Successes;
        public long Misses;
        public long Failures;
        public DateTimeOffset? LastSuccess;
        public DateTimeOffset? LastFailure;
        public volatile string? LastError;
    }
}
