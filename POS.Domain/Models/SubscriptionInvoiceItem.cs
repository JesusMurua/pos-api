using System.ComponentModel.DataAnnotations;
using POS.Domain.Enums;

namespace POS.Domain.Models;

/// <summary>
/// A single line on a <see cref="SubscriptionInvoice"/>. Dies with its invoice (CASCADE).
/// </summary>
public class SubscriptionInvoiceItem
{
    public int Id { get; set; }

    /// <summary>FK → SubscriptionInvoice (CASCADE).</summary>
    public int InvoiceId { get; set; }

    [Required, MaxLength(200)]
    public string Description { get; set; } = null!;

    public int Quantity { get; set; }

    public int UnitAmountCents { get; set; }

    /// <summary>Line total; negative for <see cref="SubscriptionInvoiceItemType.Discount"/>.</summary>
    public int TotalAmountCents { get; set; }

    public SubscriptionInvoiceItemType ItemType { get; set; }

    /// <summary>
    /// The add-on this line bills. Plain nullable column in PR-3 — the FK → <c>PlanAddOn</c>
    /// is added in PR-4 (that table does not exist yet, and PR-3 produces no AddOn lines).
    /// </summary>
    public int? LinkedAddOnId { get; set; }

    /// <summary>FK → PlanTypeCatalog (RESTRICT) for PlanBase lines.</summary>
    public int? LinkedPlanTypeId { get; set; }

    public SubscriptionInvoice? Invoice { get; set; }
}
