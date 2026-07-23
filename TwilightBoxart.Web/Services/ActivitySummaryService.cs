using TwilightBoxart.Pipeline.Caching;

namespace TwilightBoxart.Web.Services;

/// <summary>Writes hourly aggregate activity to the log.</summary>
public sealed class ActivitySummaryService(
    ActivityMonitor activity,
    ILogger<ActivitySummaryService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.SafeWaitAsync(stoppingToken))
        {
            Summarize();
        }

        Summarize();
    }

    private void Summarize()
    {
        foreach (var client in activity.DrainWindow())
        {
            logger.LogInformation(
                "Client activity: {Client} sent {Requests} request(s) ({Matched}/{Lookups} ROMs identified, {ArtHits} art served, {ArtMisses} art missed, {Rejected} rejected)",
                client.Client, client.Requests, client.Matched,
                client.Lookups, client.ArtHits, client.ArtMisses, client.Rejected);
        }
    }
}
