using Microsoft.AspNetCore.SignalR;
using POS.API.Hubs;
using POS.Repository;

namespace POS.API.Workers;

/// <summary>
/// Drains the KdsEventOutbox table and broadcasts each event to the
/// corresponding SignalR group so connected KDS clients receive it in real time.
/// Runs on a short polling interval to bound latency while remaining crash-safe:
/// if the process dies between database commit and broadcast, the next run
/// picks up any unprocessed rows. Scoped services (UnitOfWork) are resolved
/// per iteration via IServiceScopeFactory because this worker is a Singleton.
/// </summary>
public class KdsEventDispatcherWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<KdsHub> _hubContext;
    private readonly ILogger<KdsEventDispatcherWorker> _logger;

    private static readonly TimeSpan PollingInterval = TimeSpan.FromMilliseconds(500);
    private const int BatchSize = 50;
    private const string EventMethodName = "PrintJobCreated";

    public KdsEventDispatcherWorker(
        IServiceScopeFactory scopeFactory,
        IHubContext<KdsHub> hubContext,
        ILogger<KdsEventDispatcherWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _hubContext = hubContext;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("KdsEventDispatcherWorker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DispatchPendingEventsAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Unhandled error in KdsEventDispatcherWorker loop");
            }

            await Task.Delay(PollingInterval, stoppingToken);
        }
    }

    private async Task DispatchPendingEventsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var pending = await unitOfWork.KdsEventOutbox.GetPendingAsync(BatchSize);
        if (pending.Count == 0) return;

        foreach (var evt in pending)
        {
            ct.ThrowIfCancellationRequested();

            var groupName = KdsHub.BuildGroupName(evt.BranchId, evt.Destination);

            try
            {
                await _hubContext.Clients
                    .Group(groupName)
                    .SendAsync(EventMethodName, evt.Payload, ct);

                evt.IsProcessed = true;
                evt.ProcessedAt = DateTime.UtcNow;
                unitOfWork.KdsEventOutbox.Update(evt);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to broadcast KDS event {EventId} to group {Group}",
                    evt.Id, groupName);
                // Leave IsProcessed = false so the next iteration retries.
            }
        }

        await unitOfWork.SaveChangesAsync();
    }
}
