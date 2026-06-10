using POS.Services.IService;

namespace POS.API.Workers;

/// <summary>
/// Polls the NotificationOutbox every 60s and dispatches due rows via
/// <see cref="INotificationDispatchService"/> (which owns the retry/backoff logic). Same
/// BackgroundService pattern as the Stripe/billing workers; removed from DI in the test
/// environment, so the dispatch logic is exercised through the service directly.
/// </summary>
public class NotificationDispatchWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<NotificationDispatchWorker> _logger;
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(60);

    public NotificationDispatchWorker(IServiceScopeFactory scopeFactory, ILogger<NotificationDispatchWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("NotificationDispatchWorker started");

        using var timer = new PeriodicTimer(PollingInterval);
        do
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var dispatch = scope.ServiceProvider.GetRequiredService<INotificationDispatchService>();
                await dispatch.DispatchPendingAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Unhandled error in NotificationDispatchWorker tick");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
