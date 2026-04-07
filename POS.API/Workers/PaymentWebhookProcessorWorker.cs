using POS.Domain.Helpers;
using POS.Domain.Models;
using POS.Repository;
using POS.Services.IService;

namespace POS.API.Workers;

/// <summary>
/// Background worker that polls the PaymentWebhookInbox for pending events and processes them.
/// Dispatches to provider-specific services. Marks events as Processed or Failed.
/// </summary>
public class PaymentWebhookProcessorWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PaymentWebhookProcessorWorker> _logger;
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(5);
    private const int BatchSize = 50;

    public PaymentWebhookProcessorWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<PaymentWebhookProcessorWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PaymentWebhookProcessorWorker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingEventsAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Unhandled error in PaymentWebhookProcessorWorker loop");
            }

            await Task.Delay(PollingInterval, stoppingToken);
        }
    }

    private async Task ProcessPendingEventsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var pendingEvents = await unitOfWork.PaymentWebhookInbox.GetPendingEventsAsync(BatchSize);
        if (pendingEvents.Count == 0) return;

        _logger.LogInformation("Processing {Count} pending payment webhook events", pendingEvents.Count);

        foreach (var inboxEvent in pendingEvents)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                await ProcessEventAsync(inboxEvent, scope.ServiceProvider);

                inboxEvent.Status = WebhookInboxStatus.Processed;
                inboxEvent.ProcessedAt = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                inboxEvent.Status = WebhookInboxStatus.Failed;
                inboxEvent.ProcessedAt = DateTime.UtcNow;
                inboxEvent.ErrorMessage = ex.Message.Length > 2000
                    ? ex.Message[..2000]
                    : ex.Message;

                _logger.LogError(ex, "Failed to process payment webhook {Provider}/{EventId} ({EventType})",
                    inboxEvent.Provider, inboxEvent.ExternalEventId, inboxEvent.EventType);
            }

            unitOfWork.PaymentWebhookInbox.Update(inboxEvent);
            await unitOfWork.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Dispatches event processing to the appropriate provider service.
    /// </summary>
    private Task ProcessEventAsync(PaymentWebhookInbox inboxEvent, IServiceProvider services)
    {
        return inboxEvent.Provider switch
        {
            "mercadopago" => services.GetRequiredService<IMercadoPagoService>()
                .ProcessWebhookAsync(inboxEvent),
            "clip" => services.GetRequiredService<IClipService>()
                .ProcessWebhookAsync(inboxEvent),
            _ => throw new InvalidOperationException($"Unknown payment provider: {inboxEvent.Provider}")
        };
    }
}
