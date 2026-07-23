using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TwilightBoxart.Core;
using TwilightBoxart.Core.Models;
using TwilightBoxart.Core.Probe;
using TwilightBoxart.Core.Index;
using TwilightBoxart.Web.Services;

namespace TwilightBoxart.Tests;

/// <summary>
/// End-to-end tests against the real host: full middleware pipeline, real routing, real body limits.
/// The Core services are replaced with hand-written fakes - no mocking library - so a test never
/// touches the network or needs a generated index on disk.
/// </summary>
[TestClass]
public class ApiTests
{
    private TwilightWebFactory _factory = null!;
    private HttpClient _client = null!;

    [TestInitialize]
    public void Setup()
    {
        _factory = new TwilightWebFactory();
        _client = _factory.CreateClient();
    }

    [TestCleanup]
    public void Cleanup()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    #region Identify - limits

    [TestMethod]
    public async Task Identify_RejectsBatchOneOverTheItemCap()
    {
        var items = Enumerable.Range(0, IdentifyRequest.MaxItems + 1)
            .Select(i => new RomFingerprint { FileName = $"game{i}.nds" })
            .ToArray();

        var response = await _client.PostAsJsonAsync("/v2/identify", new { items });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode,
            "a batch one over the cap must be refused, not truncated");
    }

    [TestMethod]
    public async Task Identify_AcceptsBatchOfExactlyTheItemCap()
    {
        var items = Enumerable.Range(0, IdentifyRequest.MaxItems)
            .Select(i => new RomFingerprint { FileName = $"game{i}.nds" })
            .ToArray();

        var response = await _client.PostAsJsonAsync("/v2/identify", new { items });

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode,
            $"{IdentifyRequest.MaxItems} is the documented maximum, not one over it");
    }

    [TestMethod]
    public async Task Identify_RejectsBodyOverTheByteCap()
    {
        // A single fingerprint whose tag alone blows the body budget. The point is that the request
        // dies on size before anything deserialises it.
        var payload = new StringBuilder("{\"items\":[{\"fileName\":\"big.nds\",\"tag\":\"");
        payload.Append('x', IdentifyRequest.MaxItems * IdentifyRequest.MaxItemBytes + 200_000);
        payload.Append("\"}]}");

        using var content = new StringContent(payload.ToString(), Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/v2/identify", content);

        Assert.AreEqual(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [TestMethod]
    public async Task Identify_RejectsEmptyBatch()
    {
        var response = await _client.PostAsJsonAsync("/v2/identify", new { items = Array.Empty<RomFingerprint>() });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task Identify_EchoesTheClientTagOnEveryItem()
    {
        var items = new[]
        {
            new RomFingerprint { FileName = "a.nds", Tag = "first" },
            new RomFingerprint { FileName = "b.nds", Tag = "second" },
        };

        var response = await _client.PostAsJsonAsync("/v2/identify", new { items });
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<IdentifyResponseDto>();
        Assert.IsNotNull(body);
        Assert.AreEqual(2, body.Items.Count);
        CollectionAssert.AreEqual(new[] { "first", "second" }, body.Items.Select(i => i.Tag).ToArray());
    }

    [TestMethod]
    public async Task Identify_ReturnsAReadyToUseArtPathOnEveryMatch()
    {
        var items = new[] { new RomFingerprint { FileName = "Super Mario 64 DS (USA).nds" } };

        var response = await _client.PostAsJsonAsync("/v2/identify", new { items });
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<IdentifyResponseDto>();
        Assert.IsNotNull(body);
        // Clients follow this URL verbatim. They must never need to assemble it from parts -
        // that would put a frozen copy of the route scheme in every shipped binary.
        Assert.AreEqual("/v2/art/nds/ASME.png", body.Items.Single().ArtPath);
    }

    #endregion

    #region Art - clamping and keys

    [TestMethod]
    public async Task Art_ClampsAbsurdDimensions()
    {
        // The 2020 backend clamped the HEIGHT when the WIDTH was too large, so width was never
        // clamped at all - one request away from exhausting the server's memory. The Line style is
        // what keeps bt alive through Normalized(); without it the fold would hide the clamp.
        var response = await _client.GetAsync("/v2/art/nds/ASME.png?w=999999&h=888888&b=Line&bt=99");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var captured = _factory.Renderer.LastOptions;
        Assert.IsNotNull(captured, "the request must have reached the renderer");
        Assert.AreEqual(RenderOptions.MaxWidth, captured.Width);
        Assert.AreEqual(RenderOptions.MaxHeight, captured.Height);
        Assert.AreEqual(5, captured.BorderThickness, "border thickness clamps to 5");
    }

    [TestMethod]
    public async Task Art_ClampsNegativeAndZeroDimensions()
    {
        var response = await _client.GetAsync("/v2/art/nds/ASME.png?w=0&h=-4000&bt=-3");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var captured = _factory.Renderer.LastOptions;
        Assert.IsNotNull(captured);
        Assert.IsTrue(captured.Width >= 1, "width must never reach zero - it sizes a buffer");
        Assert.IsTrue(captured.Height >= 1);
        Assert.AreEqual(0, captured.BorderThickness);
    }

    [TestMethod]
    public async Task Art_RejectsUnknownPlatform()
    {
        var response = await _client.GetAsync("/v2/art/dreamcast/ASME.png");

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task Art_AcceptsTheConsoleTypeNameAsThePlatform()
    {
        // identify serialises ConsoleType as "NintendoDs". A caller that only knows that spelling
        // must not need a slug table to use the canonical route.
        var response = await _client.GetAsync("/v2/art/NintendoDs/ASME.png");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task Art_RejectsKeysOutsideTheAllowlist()
    {
        // The key becomes a path segment on disk, so anything but [A-Za-z0-9_-] is refused outright
        // rather than sanitised - the old backend's Uri.LocalPath handling was a traversal primitive.
        // (A %00 case is not listed: the server never sees it, because the HTTP stack rejects a null
        // byte in the request line before routing runs.)
        string[] keys =
        [
            "..%2f..%2fetc%2fpasswd",
            "with.dot",
            "with%20space",
            "sub%2fdir",
            new string('a', 65),
        ];

        foreach (var key in keys)
        {
            var response = await _client.GetAsync($"/v2/art/nds/{key}.png");
            Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode, $"key '{key}' must be refused");
        }
    }

    [TestMethod]
    public async Task Art_ServesPngAndAStableEtag()
    {
        var first = await _client.GetAsync("/v2/art/gba/BPEE.png");
        first.EnsureSuccessStatusCode();

        Assert.AreEqual("image/png", first.Content.Headers.ContentType?.MediaType);
        Assert.IsNotNull(first.Headers.ETag);

        using var conditional = new HttpRequestMessage(HttpMethod.Get, "/v2/art/gba/BPEE.png");
        conditional.Headers.IfNoneMatch.Add(first.Headers.ETag!);
        var second = await _client.SendAsync(conditional);

        Assert.AreEqual(HttpStatusCode.NotModified, second.StatusCode);
    }

    #endregion

    #region Resolve-and-deliver (constrained clients)

    [TestMethod]
    public async Task ArtByFingerprint_ReturnsRawPngBytesWithNoJsonEnvelope()
    {
        var response = await _client.GetAsync(
            "/v2/art.png?name=Super%20Mario%2064%20DS%20(USA).nds&w=128&h=115");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("image/png", response.Content.Headers.ContentType?.MediaType);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        CollectionAssert.AreEqual(
            new byte[] { 0x89, 0x50, 0x4E, 0x47 },
            bytes.Take(4).ToArray(),
            "the body must be the PNG itself, not a JSON wrapper around it");
    }

    [TestMethod]
    public async Task ArtByFingerprint_AdvertisesTheCanonicalUrlSoClientsCanCacheIt()
    {
        // The caller sent a file name and nothing else - no console, no serial. The server works
        // the rest out and advertises the canonical, cacheable URL. Bytes are served directly
        // rather than via a 302, because a redirect costs the slow clients this route exists for a
        // second round trip.
        var response = await _client.GetAsync(
            "/v2/art.png?name=Super%20Mario%2064%20DS%20(USA).nds&w=128&h=115");
        response.EnsureSuccessStatusCode();

        var location = response.Content.Headers.ContentLocation?.ToString();
        Assert.IsNotNull(location);
        StringAssert.StartsWith(location, "/v2/art/nds/ASME.png");
    }

    [TestMethod]
    public async Task ArtByFingerprint_CarriesTheHeaderBytesToTheIdentifier()
    {
        // An identifier that only matches when the header sample arrived, mimicking a ROM whose
        // name says nothing. This is the DSi client's contract: send the file's first bytes and
        // parse nothing on-device.
        using var factory = new TwilightWebFactory(
            identifier: new FakeIdentifier(fingerprint => fingerprint.Header is { Length: > 0 }
                ? new RomIdentity
                {
                    ConsoleType = ConsoleType.NintendoDs,
                    Key = "ASME",
                    Serial = "ASME",
                    MatchMethod = MatchMethod.HeaderSerial,
                    Tag = fingerprint.Tag,
                }
                : new RomIdentity
                {
                    ConsoleType = ConsoleType.Unknown,
                    Key = "",
                    MatchMethod = MatchMethod.None,
                    Tag = fingerprint.Tag,
                }));
        using var client = factory.CreateClient();

        var header = Uri.EscapeDataString(Convert.ToBase64String(new byte[512]));

        var withHeader = await client.GetAsync($"/v2/art.png?header={header}");
        Assert.AreEqual(HttpStatusCode.OK, withHeader.StatusCode,
            "header bytes alone must reach the ladder and identify the ROM");

        var withoutHeader = await client.GetAsync("/v2/art.png?name=unknowable.bin");
        Assert.AreEqual(HttpStatusCode.NotFound, withoutHeader.StatusCode);
    }

    [TestMethod]
    public async Task ArtByFingerprint_MissReturnsAnEmptyBody()
    {
        // A name over the text-length ceiling can never resolve. A constrained client writes
        // whatever it receives straight to disk, so an error body would become a corrupt cover
        // with no explanation. A miss is an empty 404 and nothing else.
        var name = new string('x', 600);
        var response = await _client.GetAsync($"/v2/art.png?name={name}.nds");

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.AreEqual(0, bytes.Length, "no body at all - not a ProblemDetails document");
        Assert.IsNull(response.Content.Headers.ContentType);
    }

    [TestMethod]
    public async Task ArtByFingerprint_ClampsDimensionsLikeTheCanonicalEndpoint()
    {
        var response = await _client.GetAsync("/v2/art.png?name=mario.nds&w=100000&h=100000");
        response.EnsureSuccessStatusCode();

        var captured = _factory.Renderer.LastOptions;
        Assert.IsNotNull(captured);
        Assert.AreEqual(RenderOptions.MaxWidth, captured.Width);
        Assert.AreEqual(RenderOptions.MaxHeight, captured.Height);
    }

    [TestMethod]
    public async Task ArtByFingerprint_WithoutAnyIdentifierIsNotFound()
    {
        var response = await _client.GetAsync("/v2/art.png?w=128");

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task ArtByFingerprint_CarriesTheResolvedNameToTheArtSourceOnAColdCache()
    {
        // A GB title identifies to a name-digest key, and the libretro-style sources can only
        // address art by canonical name. The one-shot route used to drop the identity it had just
        // resolved and hand the pipeline console+key alone, so a cold name-keyed title 404'd and
        // was negative-cached as a miss.
        using var factory = new TwilightWebFactory(
            identifier: new FakeIdentifier(fingerprint => new RomIdentity
            {
                ConsoleType = ConsoleType.GameBoy,
                Key = "52394050d042fd6b",
                CanonicalName = Path.GetFileNameWithoutExtension(fingerprint.FileName),
                MatchMethod = MatchMethod.Filename,
                Tag = fingerprint.Tag,
            }),
            artSource: new FakeArtSource(requireCanonicalName: true));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/v2/art.png?name=Super%20Mario%20Land%20(World).gb");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode,
            "the resolved identity's canonical name must reach the art source on a cold cache");
    }

    #endregion

    #region Health, index and the retired endpoint

    [TestMethod]
    public async Task Formats_ListsEveryScannableExtensionAsKeyEqualsCsvLines()
    {
        var response = await _client.GetAsync("/v2/formats");
        response.EnsureSuccessStatusCode();
        Assert.AreEqual("text/plain", response.Content.Headers.ContentType?.MediaType,
            "the DSi client has no JSON parser, so this endpoint must stay plain text");

        var body = await response.Content.ReadAsStringAsync();
        var lines = body.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Split('=', 2))
            .ToDictionary(p => p[0], p => p[1].Split(',', StringSplitOptions.RemoveEmptyEntries));

        // Every extension the server itself scans must be advertised, or a client that trusts this
        // endpoint over its own built-in list ends up scanning LESS than one that ignored it.
        CollectionAssert.AreEquivalent(
            SupportedFiles.Rom.ToArray(), lines["rom"],
            "the rom line must match SupportedFiles.Rom exactly");
        CollectionAssert.AreEquivalent(
            SupportedFiles.Archive.ToArray(), lines["archive"],
            "the archive line must match SupportedFiles.Archive exactly");

        Assert.IsTrue(lines["rom"].All(e => e.StartsWith('.')),
            "extensions carry their dot, so a client can compare against Path.GetExtension directly");
    }

    [TestMethod]
    public async Task Health_SaysOkAndNothingElse()
    {
        var response = await _client.GetAsync("/v2/health");
        response.EnsureSuccessStatusCode();

        var health = await response.Content.ReadFromJsonAsync<HealthDto>();
        Assert.IsNotNull(health);

        // Two values, and the caller must be able to tell them apart - that distinction is the only
        // reason this endpoint has a body at all. Which one this fixture yields is not the point.
        CollectionAssert.Contains(new[] { "ok", "degraded" }, health.Status,
            "status must be one of the two documented values");

        // The body is unauthenticated, so it must stay free of anything an operator would not want
        // published. It used to carry cache byte counts and upstream error strings for no consumer.
        var body = await response.Content.ReadAsStringAsync();
        foreach (var leaked in new[] { "caches", "upstreams", "bytes", "index", "version" })
        {
            StringAssert.DoesNotMatch(body, new Regex(leaked, RegexOptions.IgnoreCase),
                $"'{leaked}' is admin-only detail and belongs in /v2/admin/stats");
        }
    }

    /// <summary>
    /// The exact nine-field form BoxartCrawler.cs has been sending since 2020 - built as one fixed
    /// array with no conditionals, which is why the shim can require the shape. Overrides let a test
    /// vary a field without losing the rest of it.
    /// </summary>
    private static FormUrlEncodedContent V07Form(params KeyValuePair<string, string>[] overrides)
    {
        var fields = new Dictionary<string, string>
        {
            ["Filename"] = "Pokemon.zip",
            ["Sha1"] = "6b47bb75d16514b6a476aa0c73a683a2a4c18765",
            ["Header"] = Convert.ToBase64String(new byte[512]),
            ["BoxartWidth"] = "128",
            ["BoxartHeight"] = "115",
            ["KeepAspectRatio"] = "True",
            ["BoxartBorderStyle"] = "NintendoDSi",
            ["BoxartBorderColor"] = "0xFF336699",
            ["BoxartBorderThickness"] = "2",
        };

        foreach (var (key, value) in overrides)
        {
            fields[key] = value;
        }

        return new FormUrlEncodedContent(fields);
    }

    [TestMethod]
    public async Task LegacyApi_ServesArtToAVintageClient()
    {
        // Archive file name, 2020 enum spellings and all. The header sample and SHA-1 are inner-ROM
        // evidence, so the shim identifies the game regardless of the useless "Pokemon.zip" name.
        using var form = V07Form();

        var response = await _client.PostAsync("/api", form);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("image/png", response.Content.Headers.ContentType?.MediaType);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        CollectionAssert.AreEqual(
            new byte[] { 0x89, 0x50, 0x4E, 0x47 },
            bytes.Take(4).ToArray(),
            "v0.7 writes the body straight to the SD card, so it must be the PNG itself");

        var captured = _factory.Renderer.LastOptions;
        Assert.IsNotNull(captured);
        Assert.AreEqual(BoxartBorderStyle.NintendoDsi, captured.BorderStyle,
            "the 2020 spelling 'NintendoDSi' must map onto the current enum");

        // The sprite frame carries its own metrics, so the thickness and colour the form sent fold
        // flat rather than fragmenting the render cache per client.
        Assert.AreEqual(0, captured.BorderThickness);
        Assert.AreEqual(0u, captured.BorderColor);
    }

    [TestMethod]
    public async Task LegacyApi_MissIsABare404()
    {
        using var factory = new TwilightWebFactory(
            identifier: new FakeIdentifier(fingerprint => new RomIdentity
            {
                ConsoleType = ConsoleType.Unknown,
                Key = "",
                MatchMethod = MatchMethod.None,
                Tag = fingerprint.Tag,
            }));
        using var client = factory.CreateClient();

        using var form = V07Form(new KeyValuePair<string, string>("Filename", "Unknowable.zip"));

        var response = await client.PostAsync("/api", form);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.AreEqual(0, bytes.Length,
            "v0.7 treats 404 as 'no art'; a body would be written to disk as a corrupt cover");
    }

    [TestMethod]
    public async Task LegacyApi_RejectsAForeignBrowserOrigin()
    {
        // POST /api is a CORS-simple request, so a browser sends it cross-origin without a
        // preflight; the endpoint itself must refuse foreign origins. DS clients and curl send no
        // Origin header at all and are covered by LegacyApi_ServesArtToAVintageClient.
        using var form = V07Form();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api") { Content = form };
        request.Headers.Add("Origin", "https://evil.example");

        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode,
            "a foreign web page must not be able to drive a visitor's browser into the legacy path");
    }

    [TestMethod]
    public async Task LegacyApi_AllowsItsOwnOrigin()
    {
        using var form = V07Form(new KeyValuePair<string, string>("Filename", "Mario.nds"));
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api") { Content = form };
        request.Headers.Add("Origin", "http://localhost");

        var response = await _client.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode,
            "the bundled same-origin web UI must keep working without a CORS allowlist entry");
    }

    [TestMethod]
    public async Task LegacyApi_RefusesAFormThatIsNotTheShapeV07Sends()
    {
        // v0.7 posts a fixed nine-field form on every request, so a request carrying only the
        // identification fields was written by something else. Not a security boundary - the value is
        // published and the rate limiter is what bounds the damage - but it turns away a generic
        // scraper that found the endpoint without reading the client.
        using var form = new FormUrlEncodedContent([
            new KeyValuePair<string, string>("Filename", "Pokemon.zip"),
            new KeyValuePair<string, string>("Sha1", "6b47bb75d16514b6a476aa0c73a683a2a4c18765"),
        ]);

        var response = await _client.PostAsync("/api", form);

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task LegacyApi_NeedsNoApiKey()
    {
        // The whole point of the exemption: these clients were compiled in 2020 and will never send
        // a header that did not exist then. A client without the key must still get its art.
        using var factory = new TwilightWebFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Remove(ApiKey.HeaderName);

        using var form = V07Form();
        var response = await client.PostAsync("/api", form);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    #endregion

    #region API key

    [TestMethod]
    public async Task ApiKey_IsRequiredOnV1AndAbsentFromLegacyAndHealth()
    {
        using var factory = new TwilightWebFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Remove(ApiKey.HeaderName);

        // Every /v2 route that serves data to a client is gated.
        foreach (var url in new[]
                 {
                     "/v2/art/nds/ASME.png",
                     "/v2/art.png?name=Mario.nds",
                     "/v2/index/nointro.db",
                 })
        {
            var response = await client.GetAsync(url);
            Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode, $"{url} must require the key");
            Assert.AreEqual(0, (await response.Content.ReadAsByteArrayAsync()).Length,
                $"{url} must answer with no body at all - a client writes whatever arrives to the SD card");
        }

        var identify = await client.PostAsJsonAsync(
            "/v2/identify", new { items = new[] { new RomFingerprint { FileName = "Mario.nds" } } });
        Assert.AreEqual(HttpStatusCode.Unauthorized, identify.StatusCode);

        // Health is deliberately open: it is what a container orchestrator polls, and it carries no
        // data worth gating.
        var health = await client.GetAsync("/v2/health");
        Assert.AreEqual(HttpStatusCode.OK, health.StatusCode, "health must stay reachable without the key");
    }

    [TestMethod]
    public async Task ApiKey_RejectsAWrongValue()
    {
        using var factory = new TwilightWebFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Remove(ApiKey.HeaderName);
        client.DefaultRequestHeaders.TryAddWithoutValidation(ApiKey.HeaderName, "tb2_not-the-key");

        var response = await client.GetAsync("/v2/art/nds/ASME.png");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    #endregion

    #region Admin

    [TestMethod]
    public async Task Admin_FailsClosedWithoutAPassword()
    {
        // The default test host configures no admin password, so there is nothing to log in to -
        // never an open or default credential.
        var response = await _client.PostAsJsonAsync("/v2/admin/login", new { password = "anything" });

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [TestMethod]
    public async Task Admin_RejectsAWrongPasswordAndAnonymousStats()
    {
        using var factory = new TwilightWebFactory(adminPassword: "owner-secret");
        using var client = factory.CreateClient();

        var badLogin = await client.PostAsJsonAsync("/v2/admin/login", new { password = "guess" });
        Assert.AreEqual(HttpStatusCode.Unauthorized, badLogin.StatusCode);

        var stats = await client.GetAsync("/v2/admin/stats");
        Assert.AreEqual(HttpStatusCode.Unauthorized, stats.StatusCode,
            "a 401 as a status code, never a redirect to a login page");

        var rebuild = await client.PostAsync("/v2/admin/index/rebuild", content: null);
        Assert.AreEqual(HttpStatusCode.Unauthorized, rebuild.StatusCode);
    }

    [TestMethod]
    public async Task Admin_RebuildsTheIndexAndSwapsItInLive()
    {
        using var factory = new TwilightWebFactory(adminPassword: "owner-secret");
        using var client = factory.CreateClient();

        var login = await client.PostAsJsonAsync("/v2/admin/login", new { password = "owner-secret" });
        Assert.AreEqual(HttpStatusCode.NoContent, login.StatusCode);

        var before = await client.GetFromJsonAsync<AdminStatsDto>("/v2/admin/stats");
        Assert.IsNotNull(before);
        Assert.IsFalse(before.Index.Available, "the test host starts with no index file");

        var kicked = await client.PostAsync("/v2/admin/index/rebuild", content: null);
        Assert.AreEqual(HttpStatusCode.Accepted, kicked.StatusCode);

        // The build is deliberately in the background; poll like the panel does.
        AdminStatsDto? stats = null;
        for (var i = 0; i < 50; i++)
        {
            stats = await client.GetFromJsonAsync<AdminStatsDto>("/v2/admin/stats");
            if (stats!.Build.State is "succeeded" or "failed")
            {
                break;
            }

            await Task.Delay(100);
        }

        Assert.IsNotNull(stats);
        Assert.AreEqual("succeeded", stats.Build.State, stats.Build.Error ?? "");
        Assert.AreEqual("fake-index-1", stats.Index.Version,
            "the freshly built index must be swapped in without a restart");
        Assert.AreEqual(3, stats.Index.RowCount);
        Assert.IsTrue(stats.Index.Available);
    }

    #endregion

    // Local DTOs rather than the server's own records: a test that deserialises into the production
    // type cannot catch a rename of a wire field, which is the thing most likely to break a client.
    private sealed record IdentifyResponseDto(List<IdentityDto> Items, int Matched);

    private sealed record IdentityDto(string Key, string? Tag, string MatchMethod, string? ArtPath);

    private sealed record HealthDto(string Status);

    private sealed record IndexDto(string Version, int RowCount, bool Available);

    private sealed record AdminStatsDto(IndexDto Index, BuildDto Build, int Titles);

    private sealed record BuildDto(string State, string? Version, int? Rows, string? Error);
}

/// <summary>
/// Boots the real host against a throwaway data directory, with Core's services swapped for fakes.
/// </summary>
public sealed class TwilightWebFactory(
    IRomIdentifier? identifier = null,
    IArtSource? artSource = null,
    string? adminPassword = null) : WebApplicationFactory<Program>
{
    private readonly string _dataPath =
        Path.Combine(Path.GetTempPath(), "twilight-tests", Guid.NewGuid().ToString("N"));

    public FakeRenderer Renderer { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Not "Development": the production middleware pipeline is the one worth testing, and the
        // developer exception page would hide the DS endpoint's no-body contract behind an HTML page.
        builder.UseEnvironment("Testing");

        builder.UseSetting("Twilight:DataPath", _dataPath);
        // Empty unless a test asks for one: Admin_FailsClosedWithoutAPassword asserts the 503.
        // (The first-boot index build is suppressed by the "Testing" environment above, so the suite
        // never touches the network; the admin tests drive the build explicitly through a fake source.)
        builder.UseSetting("Twilight:Security:AdminPassword", adminPassword ?? "");

        builder.ConfigureTestServices(services =>
        {
            // CoreRegistration binds whatever TwilightBoxart.Core ships; replace all of it so the
            // tests never reach the network or need a generated index on disk.
            services.RemoveAll<IRomIdentifier>();
            services.RemoveAll<IBoxartRenderer>();
            services.RemoveAll<IMetadataIndex>();
            services.RemoveAll<IArtSource>();
            services.RemoveAll<IIndexSource>();

            services.AddSingleton(identifier ?? new FakeIdentifier());
            services.AddSingleton<IMetadataIndex, FakeMetadataIndex>();
            services.AddSingleton<IBoxartRenderer>(Renderer);
            services.AddSingleton(artSource ?? new FakeArtSource());
            services.AddSingleton<IIndexSource, FakeIndexSource>();
        });
    }

    /// <summary>
    /// Every client this factory hands out behaves like a first-party client and sends the API key,
    /// so the tests below are about the endpoint's own behaviour rather than about the gate. The gate
    /// itself is covered by <see cref="ApiTests.ApiKey_IsRequiredOnV1AndAbsentFromLegacyAndHealth"/>,
    /// which builds its own request without one.
    /// </summary>
    protected override void ConfigureClient(HttpClient client)
    {
        base.ConfigureClient(client);
        client.DefaultRequestHeaders.TryAddWithoutValidation(ApiKey.HeaderName, ApiKey.Value);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing || !Directory.Exists(_dataPath))
        {
            return;
        }

        try
        {
            Directory.Delete(_dataPath, recursive: true);
        }
        catch (IOException)
        {
            // A background sweep may still hold a handle; the temp directory is disposable either way.
        }
    }
}

/// <summary>
/// Matches on the file name only, which is enough to exercise the API surface. The default identity
/// is a serial-keyed DS title; pass a factory for anything else (a name-keyed GB match, say).
/// </summary>
public sealed class FakeIdentifier(Func<RomFingerprint, RomIdentity>? identify = null) : IRomIdentifier
{
    private readonly Func<RomFingerprint, RomIdentity> _identify = identify ?? (fingerprint => new RomIdentity
    {
        ConsoleType = ConsoleType.NintendoDs,
        Key = "ASME",
        Serial = "ASME",
        CanonicalName = Path.GetFileNameWithoutExtension(fingerprint.FileName),
        MatchMethod = MatchMethod.Filename,
        Tag = fingerprint.Tag,
    });

    public Task<RomIdentity> IdentifyAsync(RomFingerprint fingerprint, CancellationToken ct = default) =>
        Task.FromResult(_identify(fingerprint));

    public async Task<IReadOnlyList<RomIdentity>> IdentifyBatchAsync(
        IReadOnlyList<RomFingerprint> fingerprints, CancellationToken ct = default)
    {
        var results = new List<RomIdentity>(fingerprints.Count);
        foreach (var fingerprint in fingerprints)
        {
            results.Add(await IdentifyAsync(fingerprint, ct));
        }

        return results;
    }
}

public sealed class FakeMetadataIndex : IMetadataIndex
{
    public bool TryByCrc32(uint crc32, out IndexEntry entry)
    {
        entry = default!;
        return false;
    }

    public bool TryBySha1(string sha1, out IndexEntry entry)
    {
        entry = default!;
        return false;
    }

    public bool TryBySerial(ConsoleType console, string serial, out IndexEntry entry)
    {
        entry = new IndexEntry(console, $"Test Game ({serial})", serial, null, null);
        return true;
    }

    public IndexEntry? SearchByName(ConsoleType console, string name) => null;

    public string Version => "test-index";

    public int RowCount => 42;
}

/// <summary>
/// Always hits, with a real 2x2 PNG. With <paramref name="requireCanonicalName"/> it answers like
/// the libretro source: name or nothing.
/// </summary>
public sealed class FakeArtSource(bool requireCanonicalName = false) : IArtSource
{
    public static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAEUlEQVR4nGP4z8DwH4QZYAwAR8oH+WdZbrcAAAAASUVORK5CYII=");

    public int Order => 0;

    public bool CanHandle(RomIdentity identity) =>
        !requireCanonicalName || !string.IsNullOrEmpty(identity.CanonicalName);

    public Task<ArtBlob?> TryFetchAsync(RomIdentity identity, CancellationToken ct = default) =>
        Task.FromResult<ArtBlob?>(CanHandle(identity)
            ? new ArtBlob(Png, $"https://example.invalid/{identity.Key}.png", "image/png")
            : null);
}

/// <summary>Records the options it was handed, which is how the clamping tests observe the boundary.</summary>
public sealed class FakeRenderer : IBoxartRenderer
{
    public RenderOptions? LastOptions { get; private set; }

    public byte[] Render(ArtBlob source, RenderOptions options)
    {
        LastOptions = options;
        return source.Data;
    }
}

/// <summary>
/// Builds a real (three-row) index without the network, so the admin rebuild test exercises the
/// full lifecycle: background build, atomic swap, and the fresh version showing up in stats.
/// </summary>
public sealed class FakeIndexSource : IIndexSource
{
    public Task<BuiltIndex> BuildAsync(string outputPath, Action<string> log, CancellationToken ct)
    {
        log("building the fake index");
        var rows = IndexWriter.Write(outputPath,
        [
            new DatEntry { Console = ConsoleType.NintendoDs, Name = "Super Mario 64 DS (USA)", Serial = "ASME" },
            new DatEntry { Console = ConsoleType.GameBoy, Name = "Super Mario Land (World)" },
            new DatEntry { Console = ConsoleType.GameBoyAdvance, Name = "Metroid Fusion (USA)", Serial = "AMTE" },
        ], "fake-index-1");
        return Task.FromResult(new BuiltIndex("fake-index-1", rows));
    }
}
