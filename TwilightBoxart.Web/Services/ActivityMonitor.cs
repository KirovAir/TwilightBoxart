using System.Collections.Concurrent;
using TwilightBoxart.Web.Extensions;

namespace TwilightBoxart.Web.Services;

/// <summary>Anonymous, in-memory activity totals by client type.</summary>
public sealed class ActivityMonitor
{
    private const int MaxClients = 64;

    private readonly ConcurrentDictionary<string, State> _clients = new(StringComparer.Ordinal);

    private readonly object _admission = new();

    public void RecordRequest(string client, int statusCode)
    {
        var state = Get(client);
        state.LastSeen = DateTimeOffset.UtcNow;
        var rejected = statusCode is StatusCodes.Status401Unauthorized
            or StatusCodes.Status403Forbidden or StatusCodes.Status429TooManyRequests;
        Count(state.Totals, rejected);
        Count(Volatile.Read(ref state.Window), rejected);
    }

    public void RecordArt(string client, bool hit)
    {
        var state = Get(client);
        CountArt(state.Totals, hit);
        CountArt(Volatile.Read(ref state.Window), hit);
    }

    public void RecordIdentify(string client, int lookups, int matched)
    {
        var state = Get(client);
        CountIdentify(state.Totals, lookups, matched);
        CountIdentify(Volatile.Read(ref state.Window), lookups, matched);
    }

    public IReadOnlyList<ActivitySnapshot> Snapshot() =>
    [
        .. _clients.Select(kv => Read(kv.Key, kv.Value.Totals, kv.Value.LastSeen))
            .OrderByDescending(u => u.LastSeen)
    ];

    public IReadOnlyList<ActivitySnapshot> DrainWindow() =>
    [
        .. _clients
            .Select(kv => Read(kv.Key, Interlocked.Exchange(ref kv.Value.Window, new Counters()), kv.Value.LastSeen))
            .Where(u => u.Requests > 0 || u.Lookups > 0)
            .OrderByDescending(u => u.Requests)
    ];

    private State Get(string client)
    {
        if (_clients.TryGetValue(client, out var state))
        {
            return state;
        }

        lock (_admission)
        {
            if (_clients.Count >= MaxClients && !_clients.ContainsKey(client))
            {
                client = Activity.OtherLabel;
            }

            return _clients.GetOrAdd(client, static _ => new State());
        }
    }

    private static void Count(Counters counters, bool rejected)
    {
        Interlocked.Increment(ref counters.Requests);
        if (rejected)
        {
            Interlocked.Increment(ref counters.Rejected);
        }
    }

    private static void CountArt(Counters counters, bool hit)
    {
        if (hit)
        {
            Interlocked.Increment(ref counters.ArtHits);
        }
        else
        {
            Interlocked.Increment(ref counters.ArtMisses);
        }
    }

    private static void CountIdentify(Counters counters, int lookups, int matched)
    {
        Interlocked.Add(ref counters.Lookups, lookups);
        Interlocked.Add(ref counters.Matched, matched);
    }

    private static ActivitySnapshot Read(string client, Counters counters, DateTimeOffset lastSeen) => new(
        client,
        Interlocked.Read(ref counters.Requests),
        Interlocked.Read(ref counters.Rejected),
        Interlocked.Read(ref counters.ArtHits),
        Interlocked.Read(ref counters.ArtMisses),
        Interlocked.Read(ref counters.Lookups),
        Interlocked.Read(ref counters.Matched),
        lastSeen);

    private sealed class State
    {
        public readonly Counters Totals = new();
        public Counters Window = new();
        public DateTimeOffset LastSeen;
    }

    private sealed class Counters
    {
        public long Requests;
        public long Rejected;
        public long ArtHits;
        public long ArtMisses;
        public long Lookups;
        public long Matched;
    }
}

/// <summary>Aggregate activity without addresses or user identifiers.</summary>
public sealed record ActivitySnapshot(
    string Client,
    long Requests,
    long Rejected,
    long ArtHits,
    long ArtMisses,
    long Lookups,
    long Matched,
    DateTimeOffset LastSeen);
