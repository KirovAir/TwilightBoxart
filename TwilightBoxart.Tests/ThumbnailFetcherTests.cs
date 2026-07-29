using System.Net;
using System.Text;
using TwilightBoxart.Core.Index;
using TwilightBoxart.Core.Models;

namespace TwilightBoxart.Tests;

/// <summary>
/// The invariant these exist for: this fetcher may return a COMPLETE listing or nothing at all, never
/// a partial one. A partial listing reads downstream as "these covers do not exist", which is the
/// silent whole-console failure the art name resolution was built to end.
/// </summary>
[TestClass]
public class ThumbnailFetcherTests
{
    [TestMethod]
    public async Task FetchAsync_KeepsOnlyBoxartPngsAndDropsTheDirectoryPrefix()
    {
        using var handler = new ScriptedHandler(Tree("abc123",
            "Named_Boxarts/Tetris (World).png",
            "Named_Boxarts/Fidgetts, The (Japan).png",

            // Snaps and titles live in the same tree and are not box art.
            "Named_Snaps/Tetris (World).png",
            "Named_Titles/Tetris (World).png",
            "README.md"));

        using var fetcher = new ThumbnailFetcher(handler: handler);
        var listing = await fetcher.FetchAsync(ConsoleType.GameBoy);

        CollectionAssert.AreEqual(new[] { "Fidgetts, The (Japan)", "Tetris (World)" }, listing?.FileNames.ToArray());
        Assert.AreEqual("master", listing?.Branch);
        Assert.AreEqual("abc123", listing?.TreeSha);
        Assert.IsFalse(listing?.FromCache);
    }

    /// <summary>
    /// A truncated tree is a partial answer. It must never be accepted, and it must not take the build
    /// down either: it falls through to the cache like any other bad answer.
    /// </summary>
    [TestMethod]
    public async Task FetchAsync_RefusesATruncatedTreeAndFallsBackToTheCache()
    {
        var directory = TempDirectory();
        await Cache(directory, ConsoleType.GameBoy, "Tetris (World)");

        // Both branches are attempted, so both get a truncated answer.
        using var handler = new ScriptedHandler(Truncated(), Truncated());
        using var fetcher = new ThumbnailFetcher(directory, handler: handler);

        var listing = await fetcher.FetchAsync(ConsoleType.GameBoy);

        CollectionAssert.AreEqual(new[] { "Tetris (World)" }, listing?.FileNames.ToArray());
        Assert.IsTrue(listing?.FromCache);
    }

    /// <summary>A repository whose default branch moved is not a repository without art.</summary>
    [TestMethod]
    public async Task FetchAsync_FallsBackToMainWhenMasterIsGone()
    {
        using var handler = new ScriptedHandler(
            () => new HttpResponseMessage(HttpStatusCode.NotFound),
            Tree("def456", "Named_Boxarts/Tetris (World).png"));

        using var fetcher = new ThumbnailFetcher(handler: handler);
        var listing = await fetcher.FetchAsync(ConsoleType.GameBoy);

        Assert.AreEqual("main", listing?.Branch);
    }

    /// <summary>With no cache and no reachable upstream the answer is null, never an empty listing.</summary>
    [TestMethod]
    public async Task FetchAsync_ReturnsNullRatherThanAnEmptyListing()
    {
        using var handler = new ScriptedHandler(Tree("abc123", "README.md"), Tree("abc123", "README.md"));
        using var fetcher = new ThumbnailFetcher(handler: handler);

        Assert.IsNull(await fetcher.FetchAsync(ConsoleType.GameBoy));
    }

    /// <summary>Offline is what an --input build uses, and it must not touch the network at all.</summary>
    [TestMethod]
    public async Task FetchAsync_OfflineReadsTheCacheAndMakesNoRequest()
    {
        var directory = TempDirectory();
        await Cache(directory, ConsoleType.GameBoy, "Tetris (World)");

        using var handler = new ScriptedHandler();
        using var fetcher = new ThumbnailFetcher(directory, offline: true, handler: handler);

        var listing = await fetcher.FetchAsync(ConsoleType.GameBoy);

        CollectionAssert.AreEqual(new[] { "Tetris (World)" }, listing?.FileNames.ToArray());
        Assert.AreEqual(0, handler.Calls);
    }

    /// <summary>A fetched listing is written where an offline build will find it, provenance and all.</summary>
    [TestMethod]
    public async Task FetchAsync_CachesWhatItFetched()
    {
        var directory = TempDirectory();
        using (var handler = new ScriptedHandler(Tree("abc123", "Named_Boxarts/Tetris (World).png")))
        using (var fetcher = new ThumbnailFetcher(directory, handler: handler))
        {
            await fetcher.FetchAsync(ConsoleType.GameBoy);
        }

        var snapshot = ThumbnailFetcher.ReadSnapshot(directory, ConsoleType.GameBoy);

        CollectionAssert.AreEqual(new[] { "Tetris (World)" }, snapshot?.FileNames.ToArray());
        Assert.AreEqual("abc123", snapshot?.TreeSha);
    }

    /// <summary>
    /// The other half of the no-partial-listings invariant. A snapshot cut short between lines looks
    /// exactly like a complete one, so the header carries a count and a mismatch is refused outright.
    /// </summary>
    [TestMethod]
    public async Task ReadSnapshot_RefusesASnapshotThatWasCutShort()
    {
        var directory = TempDirectory();
        var path = Path.Combine(directory, ThumbnailListing.SnapshotFileName(ConsoleType.GameBoy));

        await File.WriteAllTextAsync(path, "# master abc123 3\nTetris (World)\nHook (USA)\n");

        Assert.IsNull(ThumbnailFetcher.ReadSnapshot(directory, ConsoleType.GameBoy));
    }

    /// <summary>A listing that does not state whether it is complete has not been verified as complete.</summary>
    [TestMethod]
    public async Task FetchAsync_RefusesATreeThatDoesNotConfirmCompleteness()
    {
        var directory = TempDirectory();
        using var handler = new ScriptedHandler(
            Json("""{"sha":"abc123","tree":[{"path":"Named_Boxarts/Tetris (World).png","type":"blob"}]}"""),
            Json("""{"sha":"abc123","tree":[{"path":"Named_Boxarts/Tetris (World).png","type":"blob"}]}"""));

        using var fetcher = new ThumbnailFetcher(directory, handler: handler);

        Assert.IsNull(await fetcher.FetchAsync(ConsoleType.GameBoy));
    }

    [TestMethod]
    public void ReadSnapshot_ReturnsNullWhenThereIsNothingToRead()
    {
        Assert.IsNull(ThumbnailFetcher.ReadSnapshot(TempDirectory(), ConsoleType.GameBoy));
    }

    private static string TempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "twilight-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task Cache(string directory, ConsoleType console, params string[] names)
    {
        await File.WriteAllTextAsync(
            Path.Combine(directory, ThumbnailListing.SnapshotFileName(console)),
            $"# master cached-sha {names.Length}\n" + string.Join('\n', names) + "\n");
    }

    private static Func<HttpResponseMessage> Tree(string sha, params string[] paths)
    {
        var entries = string.Join(',', paths.Select(p => $$"""{"path":"{{p}}","type":"blob"}"""));
        return Json($$"""{"sha":"{{sha}}","truncated":false,"tree":[{{entries}}]}""");
    }

    private static Func<HttpResponseMessage> Truncated()
    {
        return Json("""{"sha":"abc123","truncated":true,"tree":[]}""");
    }

    private static Func<HttpResponseMessage> Json(string body)
    {
        return () => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }

    private sealed class ScriptedHandler(params Func<HttpResponseMessage>[] script) : HttpMessageHandler
    {
        private readonly Queue<Func<HttpResponseMessage>> _script = new(script);

        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            var next = _script.Count > 0 ? _script.Dequeue() : throw new InvalidOperationException("script exhausted");
            return Task.FromResult(next());
        }
    }
}
