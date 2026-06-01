using System.ComponentModel.DataAnnotations;

namespace POS.Domain.DTOs.Admin;

/// <summary>
/// Payload for <c>PATCH /api/Admin/businesses/{id}/plan</c>. Mutates
/// <c>Business.PlanTypeId</c> directly and invalidates the feature cache
/// so the next request sees the new plan's capabilities. Intentionally
/// bypasses Stripe — admin overrides on real subscriptions will be
/// re-synced by the next Stripe webhook (the worker treats Stripe as
/// single source of truth for billed tenants).
/// </summary>
public sealed record AdminChangePlanRequest
{
    [Required]
    [Range(1, 4)]
    public int PlanTypeId { get; init; }
}
