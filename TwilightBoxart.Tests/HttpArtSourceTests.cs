using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging.Abstractions;
using TwilightBoxart.Core.Art;
using TwilightBoxart.Core.Models;

namespace TwilightBoxart.Tests;

/// <summary>
/// The transport-failure contract of the shared HTTP source: a timeout is an outage, not a miss.
/// </summary>
/// <remarks>
/// This pins the fix for the negative-cache poisoning bug. HttpArtSource used to swallow every transport
/// error into a null (a miss), so one 20-second GameTDB timeout during a scan read as "this title has no
/// cover" and the pipeline negative-cached it for hours. A miss and an outage must stay distinguishable:
/// a real 404 returns null, an unreachable upstream throws <see cref="ArtSourceUnavailableException"/>.
/// </remarks>
[TestClass]
public class HttpArtSourceTests
{
    [TestMethod]
    public async Task TransportFailure_IsSurfacedAsAnOutage_NotSwallowedIntoAMiss()
    {
        // Every attempt times out. The old behaviour returned null here (a miss); the fix throws, so the
        // ladder above can back off for minutes instead of negative-caching the title for the full day.
        var handler = new ScriptedHandler(Timeout(), Timeout(), Timeout(), Timeout());
        var source = new TestHttpArtSource(new SingleClientFactory(handler));

        await Assert.ThrowsExactlyAsync<ArtSourceUnavailableException>(() => source.Fetch("https://art.example/cover.jpg"));
    }

    [TestMethod]
    public async Task TransportFailure_IsRetriedBeforeGivingUp()
    {
        // MaxRetries retries means MaxRetries + 1 attempts before it throws. Enough timeouts scripted to
        // outlast the retries and still throw rather than exhaust the script.
        var handler = new ScriptedHandler(Timeout(), Timeout(), Timeout(), Timeout());
        var source = new TestHttpArtSource(new SingleClientFactory(handler));

        await Assert.ThrowsExactlyAsync<ArtSourceUnavailableException>(() => source.Fetch("https://art.example/cover.jpg"));

        Assert.AreEqual(ArtSourceLimits.MaxRetries + 1, handler.Calls,
            "a transport failure must be retried, then surfaced - not retried forever, not given up on the first try");
    }

    [TestMethod]
    public async Task TransportFailure_ThatRecoversOnRetry_ReturnsTheArt()
    {
        // The whole point of the retry: a passing blip should heal within the request, not fail it.
        var handler = new ScriptedHandler(Timeout(), Image());
        var source = new TestHttpArtSource(new SingleClientFactory(handler));

        var blob = await source.Fetch("https://art.example/cover.jpg");

        Assert.IsNotNull(blob, "the second attempt succeeded, so the fetch must return the art");
        CollectionAssert.AreEqual(FakeArtSource.Png, blob.Data);
        Assert.AreEqual(2, handler.Calls);
    }

    [TestMethod]
    public async Task NotFound_IsAMiss_AndIsNotRetried()
    {
        // A 404 is the upstream ANSWERING "no art". That is a genuine miss (null), and retrying it would
        // only lean on a volunteer-run server to hear the same no twice.
        var handler = new ScriptedHandler(NotFound());
        var source = new TestHttpArtSource(new SingleClientFactory(handler));

        var blob = await source.Fetch("https://art.example/cover.jpg");

        Assert.IsNull(blob, "a 404 is a miss, not an outage");
        Assert.AreEqual(1, handler.Calls, "a miss must never be retried");
    }

    [TestMethod]
    public async Task Success_ReturnsTheImageBlob()
    {
        var handler = new ScriptedHandler(Image());
        var source = new TestHttpArtSource(new SingleClientFactory(handler));

        var blob = await source.Fetch("https://art.example/cover.jpg");

        Assert.IsNotNull(blob);
        CollectionAssert.AreEqual(FakeArtSource.Png, blob.Data);
        Assert.AreEqual("image/png", blob.ContentType);
    }

    [TestMethod]
    public async Task CallerCancellation_PropagatesAndIsNotTreatedAsAnOutage()
    {
        // A client hitting stop must surface as cancellation, never as an ArtSourceUnavailableException
        // that would negative-cache a title over a request nothing was wrong with.
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var handler = new ScriptedHandler(Timeout());
        var source = new TestHttpArtSource(new SingleClientFactory(handler));

        await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => source.Fetch("https://art.example/cover.jpg", cts.Token));
    }

    // A timeout as HttpClient raises it: a TaskCanceledException whose cancellation is the client's own,
    // not the caller's. HttpArtSource distinguishes the two by whether ITS token requested cancellation.
    private static Func<HttpResponseMessage> Timeout()
    {
        return () => throw new TaskCanceledException("timed out", new TimeoutException());
    }

    private static Func<HttpResponseMessage> NotFound()
    {
        return () => new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static Func<HttpResponseMessage> Image()
    {
        return () => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(FakeArtSource.Png)
            {
                Headers = { ContentType = new MediaTypeHeaderValue("image/png") }
            }
        };
    }

    /// <summary>Concrete HttpArtSource that exposes the protected fetch for a test.</summary>
    private sealed class TestHttpArtSource(IHttpClientFactory factory)
        : HttpArtSource(factory, NullLogger.Instance)
    {
        protected override string SourceName => "test";

        public Task<ArtBlob?> Fetch(string url, CancellationToken ct = default)
        {
            return TryGetAsync(url, ct);
        }
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new HttpClient(handler, false);
        }
    }

    /// <summary>Answers each call with the next scripted outcome; a throwing entry models a transport failure.</summary>
    private sealed class ScriptedHandler(params Func<HttpResponseMessage>[] script) : HttpMessageHandler
    {
        private readonly Queue<Func<HttpResponseMessage>> _script = new(script);

        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            var next = _script.Count > 0 ? _script.Dequeue() : throw new InvalidOperationException("script exhausted");
            try
            {
                return Task.FromResult(next());
            }
            catch (Exception ex)
            {
                // A synchronous throw from HttpClient's plumbing surfaces as a faulted task, which is what
                // GetAsync observes for a real timeout.
                return Task.FromException<HttpResponseMessage>(ex);
            }
        }
    }
}
