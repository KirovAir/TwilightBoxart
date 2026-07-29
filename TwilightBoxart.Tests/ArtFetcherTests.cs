using Microsoft.Extensions.Logging.Abstractions;
using TwilightBoxart.Core.Art;
using TwilightBoxart.Core.Models;
using TwilightBoxart.Pipeline;

namespace TwilightBoxart.Tests;

/// <summary>
/// The ladder's miss-vs-outage verdict, which the pipeline turns into a short or a long back-off.
/// </summary>
/// <remarks>
/// A miss (every source answered "no art") and an outage (a source could not be reached) must not
/// collapse into one null the way the 2020 backend collapsed every download problem into one exception.
/// A miss earns the long negative-cache; an outage earns minutes, because it is not evidence the art is
/// absent.
/// </remarks>
[TestClass]
public class ArtFetcherTests
{
    private static readonly RomIdentity Identity = new()
    {
        ConsoleType = ConsoleType.NintendoDs,
        Key = "ASME",
        Serial = "ASME",
        MatchMethod = MatchMethod.HeaderSerial,
    };

    private static ArtFetcher Fetcher(params IArtSource[] sources) =>
        new(sources, new UpstreamMonitor(), NullLogger<ArtFetcher>.Instance);

    [TestMethod]
    public async Task EverySourceMisses_IsAMiss()
    {
        var result = await Fetcher(
            StubArtSource.Miss(order: 0),
            StubArtSource.Miss(order: 10)).TryFetchAsync(Identity);

        Assert.AreEqual(FetchStatus.Miss, result.Status);
        Assert.IsNull(result.Art);
    }

    [TestMethod]
    public async Task ASourceThatCouldNotBeReached_WithNoOtherHit_IsAnOutage()
    {
        var result = await Fetcher(
            StubArtSource.Unavailable(order: 0),
            StubArtSource.Miss(order: 10)).TryFetchAsync(Identity);

        Assert.AreEqual(FetchStatus.Unavailable, result.Status,
            "a real outage among the sources must not read as a clean miss");
        Assert.IsNull(result.Art);
    }

    [TestMethod]
    public async Task EverySourceUnreachable_IsAnOutage_NotAMiss()
    {
        // The DSi shape: GameTDB is the only source, and it timed out. This must NOT read as "no art"
        // (a 12-hour negative cache); it is a passing outage that the next scan retries.
        var result = await Fetcher(
            StubArtSource.Unavailable(order: 0),
            StubArtSource.Unavailable(order: 10)).TryFetchAsync(Identity);

        Assert.AreEqual(FetchStatus.Unavailable, result.Status);
        Assert.IsNull(result.Art);
    }

    [TestMethod]
    public async Task AnUnexpectedSourceError_IsTreatedAsAnOutage_NotAMiss()
    {
        // A source throwing something other than ArtSourceUnavailableException (a genuine bug) must not
        // stop the ladder AND must not be mistaken for "no art": an unexpected failure is no more
        // evidence the art is absent than a timeout is, so it earns the short back-off, never the 12-hour
        // one. The exception is still logged at Warning, which is where a real bug is meant to surface.
        var source = new StubArtSource(order: 0, (_, _) => throw new InvalidOperationException("boom"));

        var result = await Fetcher(source).TryFetchAsync(Identity);

        Assert.AreEqual(FetchStatus.Unavailable, result.Status);
        Assert.IsNull(result.Art);
    }

    [TestMethod]
    public async Task ABrokenSource_DoesNotStopALaterSourceFromAnswering()
    {
        // GameTDB being down must still let libretro answer for a GBA title: the outage is recorded but
        // a hit further down the ladder wins outright.
        var result = await Fetcher(
            StubArtSource.Unavailable(order: 0),
            StubArtSource.Hit(order: 10)).TryFetchAsync(Identity);

        Assert.AreEqual(FetchStatus.Hit, result.Status);
        Assert.IsNotNull(result.Art);
        CollectionAssert.AreEqual(FakeArtSource.Png, result.Art.Blob.Data);
    }

    [TestMethod]
    public async Task AHit_CarriesTheArtAndItsSourceName()
    {
        var result = await Fetcher(StubArtSource.Hit(order: 0)).TryFetchAsync(Identity);

        Assert.AreEqual(FetchStatus.Hit, result.Status);
        Assert.IsNotNull(result.Art);
        Assert.AreEqual("stub", result.Art.Source, "the source label is derived from the type name");
    }

    [TestMethod]
    public async Task CallerCancellation_Propagates_AndIsNotSwallowedIntoAnOutage()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var source = new StubArtSource(order: 0, (_, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<ArtBlob?>(null);
        });

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => Fetcher(source).TryFetchAsync(Identity, cts.Token));
    }
}

/// <summary>A hand-written <see cref="IArtSource"/> with scripted behaviour, shared across the fetch tests.</summary>
public sealed class StubArtSource(
    int order,
    Func<RomIdentity, CancellationToken, Task<ArtBlob?>> fetch,
    Func<RomIdentity, bool>? canHandle = null) : IArtSource
{
    public int Order => order;

    public bool CanHandle(RomIdentity identity) => canHandle?.Invoke(identity) ?? true;

    public Task<ArtBlob?> TryFetchAsync(RomIdentity identity, CancellationToken ct = default) => fetch(identity, ct);

    /// <summary>The source answers "no art".</summary>
    public static StubArtSource Miss(int order) =>
        new(order, (_, _) => Task.FromResult<ArtBlob?>(null));

    /// <summary>The source cannot be reached, even after its own retries.</summary>
    public static StubArtSource Unavailable(int order) =>
        new(order, (_, _) => throw new ArtSourceUnavailableException("stub", "https://stub.invalid/cover", new TimeoutException()));

    /// <summary>The source has art.</summary>
    public static StubArtSource Hit(int order) =>
        new(order, (id, _) => Task.FromResult<ArtBlob?>(
            new ArtBlob(FakeArtSource.Png, $"https://stub.invalid/{id.Key}.png", "image/png")));
}
