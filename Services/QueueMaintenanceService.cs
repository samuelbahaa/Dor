namespace Dor.Api.Services;

// A background job that lives as long as the app does.
// Every 20 seconds it releases customers who were called but never showed up,
// so one absent person cannot block the whole queue.
public class QueueMaintenanceService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<QueueMaintenanceService> _log;

    public QueueMaintenanceService(IServiceScopeFactory scopes, ILogger<QueueMaintenanceService> log)
    {
        _scopes = scopes;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(20));

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    // A background service lives forever, but the database context should not,
                    // so each round gets its own short-lived scope.
                    using var scope = _scopes.CreateScope();
                    var queue = scope.ServiceProvider.GetRequiredService<QueueService>();
                    await queue.RunMaintenanceAsync();
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Queue maintenance failed; it will try again in 20 seconds.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The app is shutting down. Nothing to do.
        }
    }
}
