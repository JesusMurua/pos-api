using POS.Services.IService;

namespace POS.API.Workers;

/// <summary>
/// Daily background worker that drives the SaaS invoice lifecycle on MANUAL rails:
/// generates due invoices and sweeps past-due ones to Overdue. The logic lives in
/// <see cref="IInvoiceGenerationService"/> (testable without this loop); this worker only
/// schedules it. Stripe-rail invoices are produced by the Stripe webhook path, not here.
///
/// Removed from the DI container in the integration-test environment along with every other
/// IHostedService (see CustomWebApplicationFactory).
/// </summary>
public class InvoiceLifecycleWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<InvoiceLifecycleWorker> _logger;
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    public InvoiceLifecycleWorker(IServiceScopeFactory scopeFactory, ILogger<InvoiceLifecycleWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("InvoiceLifecycleWorker started");

        // PeriodicTimer fires every 24h; the first tick is one interval after startup,
        // which keeps boot lightweight and avoids racing the seeders on a cold start.
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var generation = scope.ServiceProvider.GetRequiredService<IInvoiceGenerationService>();
                await generation.GenerateDueInvoicesAsync(stoppingToken);
                await generation.SweepOverdueAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Unhandled error in InvoiceLifecycleWorker tick");
            }
        }
    }
}
