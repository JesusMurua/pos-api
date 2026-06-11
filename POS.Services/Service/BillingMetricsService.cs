using Microsoft.EntityFrameworkCore;
using POS.Domain.DTOs.Admin;
using POS.Domain.Enums;
using POS.Domain.Helpers;
using POS.Repository;
using POS.Services.IService;

namespace POS.Services.Service;

/// <inheritdoc />
public class BillingMetricsService : IBillingMetricsService
{
    private const string Currency = "MXN"; // OQ-10: single currency today; the contract carries it for the future.

    private static readonly string[] MrrStatuses =
        { StripeSubscriptionStatus.Active, StripeSubscriptionStatus.Trialing, StripeSubscriptionStatus.PastDue };

    private readonly ApplicationDbContext _context;

    public BillingMetricsService(ApplicationDbContext context) => _context = context;

    public async Task<AdminBillingMetricsDto> GetMetricsAsync(int lookbackMonths = 12, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        lookbackMonths = Math.Clamp(lookbackMonths, 1, 60);

        var mrrCents = await ComputeMrrCentsAsync(ct);
        var (active, trialing, pastDue) = await CountByStatusAsync(ct);
        var churn = await ComputeChurn30dAsync(now, ct);
        var revenue = await ComputeRevenueByMonthAsync(now, lookbackMonths, ct);
        var retention = await ComputeRetentionAsync(now, lookbackMonths, ct);
        var notif = await ComputeNotificationStatsAsync(now, ct);

        return new AdminBillingMetricsDto(
            CurrentMrr: new MoneyDto(mrrCents, Currency),
            CurrentArr: new MoneyDto(mrrCents * 12, Currency),
            AsOf: now,
            ActiveSubscriptions: active,
            TrialSubscriptions: trialing,
            PastDueSubscriptions: pastDue,
            ChurnRate30d: churn,
            RevenueByMonth: revenue,
            RetentionByCohort: retention,
            NotificationStats: notif);
    }

    public async Task<IReadOnlyList<UpcomingInvoiceDto>> GetUpcomingInvoicesAsync(int days = 30, CancellationToken ct = default)
    {
        days = Math.Clamp(days, 1, 365);
        var now = DateTime.UtcNow;
        var horizon = now.AddDays(days);

        var rails = await _context.SaaSBillingMethods.AsNoTracking().ToDictionaryAsync(m => m.Id, m => m.Code, ct);
        var stripeRailId = rails.FirstOrDefault(kv => kv.Value == "Stripe").Key;

        var subs = await _context.Subscriptions.IgnoreQueryFilters()
            .Where(s => s.NextBillingDate != null
                        && s.NextBillingDate <= horizon
                        && (s.Status == StripeSubscriptionStatus.Active || s.Status == StripeSubscriptionStatus.Trialing)
                        && s.BillingMethodId != stripeRailId)
            .Select(s => new
            {
                s.Id, s.BusinessId, s.NextBillingDate, s.BaseAmountCents, s.BillingMethodId
            })
            .OrderBy(s => s.NextBillingDate)
            .ToListAsync(ct);

        if (subs.Count == 0) return Array.Empty<UpcomingInvoiceDto>();

        var addonBySub = await ActiveAddOnCentsBySubscriptionAsync(subs.Select(s => s.Id).ToList(), ct);

        return subs.Select(s => new UpcomingInvoiceDto(
            BusinessId: s.BusinessId,
            SubscriptionId: s.Id,
            NextBillingDate: s.NextBillingDate!.Value,
            EstimatedAmountCents: (s.BaseAmountCents ?? 0) + addonBySub.GetValueOrDefault(s.Id),
            Currency: Currency,
            Rail: s.BillingMethodId == null ? "Unknown" : rails.GetValueOrDefault(s.BillingMethodId.Value, "Unknown")))
            .ToList();
    }

    #region MRR

    private async Task<int> ComputeMrrCentsAsync(CancellationToken ct)
    {
        // Base plans. Accumulate in decimal and round ONCE — integer /12 per row would
        // systematically under-count a portfolio of annual subscriptions.
        var bases = await _context.Subscriptions.IgnoreQueryFilters()
            .Where(s => MrrStatuses.Contains(s.Status))
            .Select(s => new { s.BillingCycle, s.BaseAmountCents })
            .ToListAsync(ct);

        decimal mrr = bases.Sum(b => NormalizeMonthly(b.BaseAmountCents ?? 0, b.BillingCycle));

        // Active add-ons on MRR-eligible subscriptions.
        var addons = await _context.SubscriptionAddOns.IgnoreQueryFilters()
            .Where(a => a.DeactivatedAt == null && MrrStatuses.Contains(a.Subscription!.Status))
            .Select(a => new
            {
                a.Quantity,
                a.CustomPriceCents,
                Default = a.PlanAddOn!.DefaultPriceCents,
                Cycle = a.PlanAddOn.BillingCycle
            })
            .ToListAsync(ct);

        foreach (var a in addons)
        {
            var line = (a.CustomPriceCents ?? a.Default) * a.Quantity;
            mrr += NormalizeMonthlyAddOn(line, a.Cycle);
        }

        return (int)Math.Round(mrr, MidpointRounding.AwayFromZero);
    }

    private static decimal NormalizeMonthly(int cents, string billingCycle) =>
        string.Equals(billingCycle, "Annual", StringComparison.OrdinalIgnoreCase) ? cents / 12m : cents;

    private static decimal NormalizeMonthlyAddOn(int cents, PlanAddOnBillingCycle cycle) => cycle switch
    {
        PlanAddOnBillingCycle.Annual => cents / 12m,
        PlanAddOnBillingCycle.OneTime => 0m, // one-time charges are not recurring revenue
        _ => cents
    };

    #endregion

    #region Counts + churn

    private async Task<(int Active, int Trialing, int PastDue)> CountByStatusAsync(CancellationToken ct)
    {
        var counts = await _context.Subscriptions.IgnoreQueryFilters()
            .GroupBy(s => s.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        int C(string s) => counts.FirstOrDefault(c => c.Status == s)?.Count ?? 0;
        return (C(StripeSubscriptionStatus.Active), C(StripeSubscriptionStatus.Trialing), C(StripeSubscriptionStatus.PastDue));
    }

    private async Task<double> ComputeChurn30dAsync(DateTime now, CancellationToken ct)
    {
        var periodStart = now.AddDays(-30);

        // Paid-only churn: only subscriptions that ever received a payment count (a trial that
        // cancels before paying never produced revenue, so it is not revenue lost).
        var paidSubIds = (await _context.TenantPayments
            .Select(p => p.Invoice!.SubscriptionId).Distinct().ToListAsync(ct)).ToHashSet();

        var subs = await _context.Subscriptions.IgnoreQueryFilters()
            .Select(s => new { s.Id, s.CanceledAt, BizCreated = s.Business!.CreatedAt })
            .ToListAsync(ct);

        var denom = subs.Count(s => paidSubIds.Contains(s.Id)
                                    && (s.CanceledAt == null || s.CanceledAt >= periodStart)
                                    && s.BizCreated <= periodStart);
        var numer = subs.Count(s => paidSubIds.Contains(s.Id)
                                    && s.CanceledAt != null && s.CanceledAt >= periodStart && s.CanceledAt <= now);

        return denom == 0 ? 0d : (double)numer / denom;
    }

    #endregion

    #region Revenue + retention

    private async Task<IReadOnlyList<RevenueMonthDto>> ComputeRevenueByMonthAsync(
        DateTime now, int lookbackMonths, CancellationToken ct)
    {
        var firstMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var since = firstMonth.AddMonths(-(lookbackMonths - 1));

        var rows = await _context.TenantPayments
            .Where(p => p.PaidAtUtc >= since)
            .Select(p => new { p.PaidAtUtc, p.AmountCents })
            .ToListAsync(ct);

        var byMonth = rows
            .GroupBy(r => new DateTime(r.PaidAtUtc.Year, r.PaidAtUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc))
            .ToDictionary(g => g.Key, g => g.Sum(x => x.AmountCents));

        var result = new List<RevenueMonthDto>();
        for (var m = since; m <= firstMonth; m = m.AddMonths(1))
            result.Add(new RevenueMonthDto(m.ToString("yyyy-MM"), byMonth.GetValueOrDefault(m), Currency));

        return result;
    }

    private async Task<IReadOnlyList<CohortRetentionDto>> ComputeRetentionAsync(
        DateTime now, int lookbackMonths, CancellationToken ct)
    {
        var firstMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var since = firstMonth.AddMonths(-(lookbackMonths - 1));

        // Signup cohorts: businesses with a subscription, grouped by Business.CreatedAt month.
        var members = await _context.Subscriptions.IgnoreQueryFilters()
            .Select(s => new { BizCreated = s.Business!.CreatedAt, s.CanceledAt })
            .Where(s => s.BizCreated >= since)
            .ToListAsync(ct);

        var cohorts = members
            .GroupBy(s => new DateTime(s.BizCreated.Year, s.BizCreated.Month, 1, 0, 0, 0, DateTimeKind.Utc))
            .OrderBy(g => g.Key);

        var result = new List<CohortRetentionDto>();
        foreach (var cohort in cohorts)
        {
            var size = cohort.Count();
            var periods = new List<RetentionPeriodDto>();
            // Triangular: only periods whose end has already occurred.
            for (var n = 0; cohort.Key.AddMonths(n) <= firstMonth; n++)
            {
                var asOf = cohort.Key.AddMonths(n);
                var retained = cohort.Count(s => s.CanceledAt == null || s.CanceledAt >= asOf);
                periods.Add(new RetentionPeriodDto(n, size == 0 ? 0d : (double)retained / size));
            }
            result.Add(new CohortRetentionDto(cohort.Key.ToString("yyyy-MM"), size, periods));
        }

        return result;
    }

    #endregion

    #region Notification stats + helpers

    private async Task<NotificationStatsDto> ComputeNotificationStatsAsync(DateTime now, CancellationToken ct)
    {
        var q = _context.NotificationOutbox;
        var failed24h = await q.CountAsync(n => n.Status == NotificationStatus.Failed
                                                && n.FailedAtUtc != null && n.FailedAtUtc >= now.AddHours(-24), ct);
        var failed7d = await q.CountAsync(n => n.Status == NotificationStatus.Failed
                                               && n.FailedAtUtc != null && n.FailedAtUtc >= now.AddDays(-7), ct);
        var failedTotal = await q.CountAsync(n => n.Status == NotificationStatus.Failed, ct);
        var pending = await q.CountAsync(n => n.Status == NotificationStatus.Pending, ct);

        var oldestPending = await q.Where(n => n.Status == NotificationStatus.Pending)
            .Select(n => (DateTime?)n.CreatedAtUtc).MinAsync(ct);
        var oldestMinutes = oldestPending == null ? 0 : (int)Math.Max(0, (now - oldestPending.Value).TotalMinutes);

        return new NotificationStatsDto(failed24h, failed7d, failedTotal, pending, oldestMinutes);
    }

    private async Task<Dictionary<int, int>> ActiveAddOnCentsBySubscriptionAsync(List<int> subIds, CancellationToken ct)
    {
        var rows = await _context.SubscriptionAddOns.IgnoreQueryFilters()
            .Where(a => a.DeactivatedAt == null && subIds.Contains(a.SubscriptionId))
            .Select(a => new { a.SubscriptionId, a.Quantity, a.CustomPriceCents, Default = a.PlanAddOn!.DefaultPriceCents })
            .ToListAsync(ct);

        return rows
            .GroupBy(a => a.SubscriptionId)
            .ToDictionary(g => g.Key, g => g.Sum(a => (a.CustomPriceCents ?? a.Default) * a.Quantity));
    }

    #endregion
}
