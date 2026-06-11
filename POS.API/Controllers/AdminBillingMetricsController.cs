using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POS.API.Auth;
using POS.Domain.DTOs.Admin;
using POS.Services.IService;

namespace POS.API.Controllers;

/// <summary>
/// Super-admin financial dashboard (PR-6). Distinct from <c>/Admin/businesses/stats</c>
/// (operational counts) — this is the financial view. Authenticated via X-Admin-Token.
/// </summary>
[ApiController]
[Route("api/Admin/billing")]
[Authorize(AuthenticationSchemes = AdminTokenAuthenticationHandler.SchemeName)]
public class AdminBillingMetricsController : ControllerBase
{
    private readonly IBillingMetricsService _metrics;

    public AdminBillingMetricsController(IBillingMetricsService metrics)
    {
        _metrics = metrics;
    }

    /// <summary>
    /// Financial snapshot: MRR/ARR (current snapshot — no historical MRR series),
    /// active/trial/past-due counts, 30-day paid-logo churn, collected revenue by month
    /// (actual cash, not MRR), retention by signup cohort (reconstructed from CanceledAt), and
    /// notification-outbox health. Non-realtime; all amounts MXN (OQ-10). <paramref name="lookback"/>
    /// months for the series/cohorts (default 12).
    /// </summary>
    [HttpGet("metrics")]
    [ProducesResponseType(typeof(AdminBillingMetricsDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Metrics([FromQuery] int lookback = 12, CancellationToken ct = default) =>
        Ok(await _metrics.GetMetricsAsync(lookback, ct));

    /// <summary>
    /// Upcoming MANUAL-rail invoices in the next <paramref name="days"/> days (default 30).
    /// Stripe-rail upcoming invoices are NOT included — the Stripe Dashboard is authoritative for
    /// that rail (it computes prorations we do not replicate here).
    /// </summary>
    [HttpGet("upcoming-invoices")]
    [ProducesResponseType(typeof(IReadOnlyList<UpcomingInvoiceDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> UpcomingInvoices([FromQuery] int days = 30, CancellationToken ct = default) =>
        Ok(await _metrics.GetUpcomingInvoicesAsync(days, ct));
}
