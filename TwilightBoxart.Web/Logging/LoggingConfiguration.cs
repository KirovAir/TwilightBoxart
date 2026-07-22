using System.Reflection;
using Serilog;
using Serilog.Events;

namespace TwilightBoxart.Web.Logging;

/// <summary>
/// Serilog wiring. Console always; Seq only when <c>Seq:ServerUrl</c> is configured, which keeps the
/// shipped appsettings.json free of any endpoint or key.
/// </summary>
public static class LoggingConfiguration
{
    public static WebApplicationBuilder ConfigureLogging(this WebApplicationBuilder builder)
    {
        builder.Logging.ClearProviders();
        var appName = Assembly.GetEntryAssembly()?.GetName().Name ?? "TwilightBoxart.Web";
        var environment = builder.Environment.EnvironmentName;

        var logConfig = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .Enrich.WithProperty("Application", appName)
            .Enrich.WithProperty("Environment", environment)
            .WriteTo.Console();

        var seqUrl = builder.Configuration["Seq:ServerUrl"];
        if (!string.IsNullOrEmpty(seqUrl))
        {
            logConfig = logConfig.WriteTo.Seq(seqUrl, apiKey: builder.Configuration["Seq:ApiKey"]);
        }

        Log.Logger = logConfig.CreateLogger();
        builder.Logging.AddSerilog(Log.Logger, true);
        Log.Information("Initialized logger for {Application} ({Environment})", appName, environment);
        return builder;
    }

    /// <summary>
    /// Runs the whole startup path inside a try/finally so a crash before <c>app.Run()</c> is logged
    /// and flushed rather than vanishing into a silent non-zero exit.
    /// </summary>
    public static async Task RunWithLoggingAsync(this WebApplicationBuilder builder,
        Func<WebApplicationBuilder, Task> configure)
    {
        var finished = false;
        try
        {
            await configure(builder);
            finished = true;
        }
        catch (Exception ex) when (!IsHostControlSignal(ex))
        {
            Log.Fatal(ex, "Application startup failed");
            // A supervisor (Docker, systemd) restarts on a non-zero exit; exiting 0 here would
            // report a crashed boot as a clean shutdown.
            Environment.ExitCode = 1;
            finished = true;
        }
        finally
        {
            // Only flush when the host really is done. A host-control signal means someone stopped
            // the host at Build time and still intends to use it - closing the logger there would
            // silence every message the caller is about to produce.
            if (finished)
            {
                await Log.CloseAndFlushAsync();
            }
        }
    }

    /// <summary>
    /// True for the exceptions the hosting infrastructure throws to take control of an entry point
    /// rather than to report a failure: <c>HostAbortedException</c> from <c>dotnet ef</c>, and the
    /// internal StopTheHostException that WebApplicationFactory throws out of <c>Build()</c> to grab
    /// the host for a test server. Swallowing either breaks the caller.
    /// </summary>
    private static bool IsHostControlSignal(Exception exception) =>
        exception is HostAbortedException
        || string.Equals(exception.GetType().Name, "StopTheHostException", StringComparison.Ordinal);
}
