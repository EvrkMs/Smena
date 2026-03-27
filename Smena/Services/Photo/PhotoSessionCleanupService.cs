namespace Host.Services.Photo;

/// <summary>
/// Periodically removes expired photo sessions from the in-memory store
/// to prevent unbounded memory growth from orphaned sessions.
/// </summary>
public sealed class PhotoSessionCleanupService(
    PhotoSessionStore store,
    ILogger<PhotoSessionCleanupService> logger) : BackgroundService
{
    private static readonly TimeSpan SessionTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(CleanupInterval, stoppingToken);
                var removed = store.CleanupExpired(SessionTtl);
                if (removed > 0)
                {
                    logger.LogInformation("Cleaned up {Count} expired photo sessions.", removed);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
