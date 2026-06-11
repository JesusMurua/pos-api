namespace POS.Domain.DTOs.Admin;

/// <summary>A money amount in minor units (cents) with its ISO currency. Always MXN today (OQ-10).</summary>
public sealed record MoneyDto(int AmountCents, string Currency);

/// <summary>Collected revenue for one calendar month (sum of TenantPayment by PaidAtUtc).</summary>
public sealed record RevenueMonthDto(string Month, int AmountCents, string Currency);

/// <summary>One retention period within a cohort: <c>Period</c> = months since signup.</summary>
public sealed record RetentionPeriodDto(int Period, double Rate);

/// <summary>Signup cohort (by Business.CreatedAt month) and its retention curve (from CanceledAt).</summary>
public sealed record CohortRetentionDto(string CohortMonth, int CohortSize, IReadOnlyList<RetentionPeriodDto> Periods);

/// <summary>
/// Operational health of the notification outbox. <c>Failed24h</c>/<c>Failed7d</c> window by
/// <c>FailedAtUtc</c> (the real failure time, not creation), so slow-failing rows count correctly.
/// A growing <c>OldestPendingAgeMinutes</c> signals the dispatch worker is down.
/// </summary>
public sealed record NotificationStatsDto(
    int Failed24h, int Failed7d, int FailedTotal, int Pending, int OldestPendingAgeMinutes);

/// <summary>
/// Financial snapshot for the super-admin. MRR/ARR are a current snapshot (no historical MRR
/// series); <see cref="RevenueByMonth"/> is actual collected cash; retention is reconstructed from
/// CanceledAt. All amounts MXN (OQ-10).
/// </summary>
public sealed record AdminBillingMetricsDto(
    MoneyDto CurrentMrr,
    MoneyDto CurrentArr,
    DateTime AsOf,
    int ActiveSubscriptions,
    int TrialSubscriptions,
    int PastDueSubscriptions,
    double ChurnRate30d,
    IReadOnlyList<RevenueMonthDto> RevenueByMonth,
    IReadOnlyList<CohortRetentionDto> RetentionByCohort,
    NotificationStatsDto NotificationStats);

/// <summary>An upcoming manual-rail invoice projected from Subscription.NextBillingDate.</summary>
public sealed record UpcomingInvoiceDto(
    int BusinessId,
    int SubscriptionId,
    DateTime NextBillingDate,
    int EstimatedAmountCents,
    string Currency,
    string Rail);
