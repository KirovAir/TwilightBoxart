namespace TwilightBoxart.Pipeline.Caching;

public static class PeriodicTimerExtensions
{
    /// <summary>WaitForNextTickAsync that reports cancellation as "stop looping" instead of throwing.</summary>
    public static async Task<bool> SafeWaitAsync(this PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
