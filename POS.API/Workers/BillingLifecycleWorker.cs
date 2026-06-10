using POS.Services.IService;

namespace POS.API.Workers;

/// <summary>
/// Daily background worker that drives the SaaS billing lifecycle: generates due invoices on
/// manual rails, sweeps past-due invoices to Overdue, and enqueues trial-expiry reminders. The
/// per-concern logic lives in <see cref="IInvoiceGenerationService"/> and
/// <see cref="INotificationService"/> (testable without this loop); this worker only schedules.
///
/// Renamed from InvoiceLifecycleWorker in PR-5 — "Invoice" understated its scope once trial
/// notifications joined. Removed from the DI container in the integration-test environment along
/// with every other IHostedService (see CustomWebApplicationFactory).
/// </summary>
public class BillingLifecycleWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BillingLifecycleWorker> _logger;
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    public BillingLifecycleWorker(IServiceScopeFactory scopeFactory, ILogger<BillingLifecycleWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("BillingLifecycleWorker started");

        // PeriodicTimer fires every 24h; the first tick is one interval after startup,
        // which keeps boot lightweight and avoids racing the seeders on a cold start.
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var generation = scope.ServiceProvider.GetRequiredService<IInvoiceGenerationService>();
                var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();

                await generation.GenerateDueInvoicesAsync(stoppingToken);
                await generation.SweepOverdueAsync(stoppingToken);
                await notifications.EnqueueDueTrialNotificationsAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Unhandled error in BillingLifecycleWorker tick");
            }
        }
    }
}
