using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.StaticFiles;
using Serilog;
using TwilightBoxart.Data;
using TwilightBoxart.Data.Extensions;
using TwilightBoxart.Pipeline;
using TwilightBoxart.Pipeline.Caching;
using TwilightBoxart.Web.Endpoints;
using TwilightBoxart.Web.Extensions;
using TwilightBoxart.Web.Logging;
using TwilightBoxart.Web.Models;
using TwilightBoxart.Web.Services;

var builder = WebApplication.CreateBuilder(args);
builder.ConfigureLogging();

await builder.RunWithLoggingAsync(async b =>
{
    // Every environment-supplied setting this server has, read in one place. Nothing here is editable
    // over HTTP: an HTTP-writable DataPath would be an arbitrary-write primitive.
    var settings = TwilightSettings.Load(b.Configuration, b.Environment);
    b.Services.AddSingleton(settings);
    b.Services.AddSingleton(settings.Security);
    Directory.CreateDirectory(settings.DataPath);

    // The relational half of the two-database split (see AppDbContext's header): the title space and
    // the cache bookkeeping. The generated No-Intro index is replaced wholesale, never migrated.
    // The path is derived from DataPath rather than configured: the database belongs on the same
    // volume as the cache and the index - the one an operator backs up - and it has no credentials
    // for a connection string to carry.
    var connectionString = $"Data Source={settings.DatabasePath}";
    b.Configuration["ConnectionStrings:DefaultConnection"] = connectionString;
    b.Services.AddAppDbContextFactory();

    // Migrate before the service graph is built: the runtime settings below shape the singletons
    // that follow, so they must be known here rather than resolved lazily.
    using var bootstrapLoggers = LoggerFactory.Create(l => l.AddSerilog(Log.Logger));

    await using (var bootstrap = new AppDbContext(
                     new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connectionString).Options))
    {
        await bootstrap.Initialize(bootstrapLoggers.CreateLogger<AppDbContext>());
    }

    // The two cache budgets are the only behavioural setting left: disk space is the one thing that
    // genuinely differs between a NAS and a Pi. Everything else that used to be bound here is a
    // constant now - see TwilightSettings for why.
    var cacheSettings = (b.Configuration.GetSection("Twilight:Cache").Get<CacheSettings>()
                         ?? new CacheSettings()).Normalized();
    b.Services.AddSingleton(cacheSettings);

    // Core: identification, art sources, rendering.
    b.Services.AddTwilightCore(settings);

    // Caching: blobs on disk, bookkeeping in the database, both under the data volume and NOT under
    // wwwroot (see TwilightSettings.DataPath). The pipeline is given the two cache roots and nothing
    // else about the deployment, which is what lets a desktop client host the same services.
    b.Services.AddSingleton(new CachePaths(settings.OriginalsPath, settings.RendersPath));
    b.Services.AddSingleton<ArtCaches>();
    b.Services.AddSingleton<CacheAccessBuffer>();
    b.Services.AddSingleton<CacheIndex>();
    b.Services.AddHostedService<CacheAccountingService>();
    b.Services.AddHostedService<CacheEvictionService>();

    // Art pipeline. All singletons: the in-flight fetch table and the per-source politeness
    // gates have to outlive the request that created them.
    b.Services.AddSingleton<SingleFlight>();
    b.Services.AddSingleton<ArtRecordStore>();
    b.Services.AddSingleton<UpstreamMonitor>();
    b.Services.AddSingleton<ArtFetcher>();
    b.Services.AddSingleton<ArtPipeline>();

    // Index building: the index is derived entirely from public DAT files, so the server makes its
    // own - once on first boot, again from the admin panel's button.
    b.Services.AddSingleton<IIndexSource, DatIndexSource>();
    b.Services.AddSingleton<IndexBuildService>();
    b.Services.AddHostedService(sp => sp.GetRequiredService<IndexBuildService>());

    // Admin: one operator, one password (from the environment), one cookie. 401 as a status code,
    // never a redirect: the panel is a static page that reads the status itself.
    b.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
        .AddCookie(o =>
        {
            o.Cookie.Name = "twilight.admin";
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Strict;
            // Deliberate: plain-HTTP LAN self-hosts must keep working; behind a TLS proxy with
            // TrustedProxy set, X-Forwarded-Proto makes the request secure and the cookie follows.
            o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            o.ExpireTimeSpan = TimeSpan.FromHours(12);
            o.SlidingExpiration = true;
            o.Events.OnRedirectToLogin = ctx =>
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
        });
    b.Services.AddAuthorization();

    // Enums travel as their names, so a client can act on them without shipping a copy of the enum.
    b.Services.ConfigureHttpJsonOptions(o =>
    {
        o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
        o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });
    b.Services.AddProblemDetails();
    b.Services.AddTwilightCors(settings.Security);
    b.Services.AddTwilightRateLimiting();

    // Anonymous client activity, counted in memory and summarized once an hour.
    b.Services.AddSingleton<ActivityMonitor>();
    b.Services.AddHostedService<ActivitySummaryService>();

    // Deliberately NOT calling UseForwardedHeaders unless a specific proxy is named. The old
    // Startup.cs enabled ForwardedHeaders.All with KnownNetworks and KnownProxies cleared, which let
    // any client dictate its own IP, host and scheme - and rate limiting partitions on the remote IP.
    if (settings.TrustedProxy is { } proxyIp)
    {
        b.Services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            o.KnownProxies.Clear();
            o.KnownProxies.Add(proxyIp);
            o.ForwardLimit = 1;
        });
    }
    else if (!string.IsNullOrWhiteSpace(b.Configuration["Twilight:TrustedProxy"]))
    {
        // A typo here fails silently otherwise, and the failure mode is nasty: every visitor lands
        // in the proxy's one rate-limit bucket, so a single scan 429s the whole site.
        Log.Warning("Twilight:TrustedProxy '{Value}' is not a valid IP address; forwarded headers stay off",
            b.Configuration["Twilight:TrustedProxy"]);
    }

    var app = b.Build();

    // Resolve the index once at boot. It is lazy otherwise, and "the index file is missing" is a
    // message an operator wants in the startup log, not in the first user's failed lookup.
    _ = app.Services.GetRequiredService<IMetadataIndex>();

    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler(new ExceptionHandlerOptions
        {
            ExceptionHandler = async context =>
            {
                // The image routes promise no body on a non-200: a constrained client writes
                // whatever bytes arrive straight to the SD card, so a problem-details document
                // would become a corrupt .png. Everything else keeps problem details.
                var path = context.Request.Path.Value ?? "";
                if (path.StartsWith("/v2/art", StringComparison.OrdinalIgnoreCase)
                    || path.StartsWith("/api", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                await context.RequestServices.GetRequiredService<IProblemDetailsService>()
                    .WriteAsync(new ProblemDetailsContext { HttpContext = context });
            },
        });
    }

    if (settings.TrustedProxy is not null)
    {
        app.UseForwardedHeaders();
    }

    app.UseRouting();
    // After routing so the endpoint is known; before everything else so a 401 or a 429 still counts.
    app.UseActivity();
    app.UseCors();
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseRateLimiter();

    // After UseRouting so the endpoint's metadata is known, and before the endpoint runs so an
    // oversized body is rejected before model binding allocates for it.
    app.UseRequestBodyLimits();

    // The browser client is the only static content served, and it is the hand-written app that IS
    // the web root. Nothing the server generates is ever placed there.
    app.UseDefaultFiles();
    app.UseStaticFiles(new StaticFileOptions { OnPrepareResponse = SetShellCaching });

    app.MapIdentifyEndpoints();
    app.MapIndexEndpoints();
    app.MapArtEndpoints();
    app.MapFormatsEndpoints();
    app.MapAdminEndpoints();
    app.MapHealthEndpoints();
    app.MapLegacyEndpoints();

    await app.RunAsync();
});

// Cache headers for the browser client, so a deploy actually reaches people. The app shell has no
// content hashes in its file names (app.js is always app.js), so revalidation is the only thing that
// can retire an old copy. Bare UseStaticFiles() sends no Cache-Control at all, which is the worst
// case: the browser invents a freshness lifetime (typically a tenth of the file's age), so a file
// that sat unchanged for ten months may sit stale for three weeks after it changes. The service
// worker cannot rescue this: it is network-first precisely so a fix propagates, but its fetch() goes
// through the same HTTP cache. "no-cache" means "always revalidate", not "do not store"; with the
// ETag UseStaticFiles already emits, an unchanged shell costs a 304 of a few bytes. Fonts and images
// are exempt: large, effectively immutable, and a stale glyph is not a broken client.
static void SetShellCaching(StaticFileResponseContext ctx)
{
    var name = ctx.File.Name;
    var revalidate =
        name.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".css", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

    ctx.Context.Response.Headers.CacheControl = revalidate
        ? "no-cache"
        : "public, max-age=604800";
}

public partial class Program;
