using System.ComponentModel.DataAnnotations;

namespace POS.API.Models;

/// <summary>
/// Request body for creating a Stripe Checkout session.
/// </summary>
public class CheckoutRequest
{
    /// <summary>Target catalog plan (1=Free, 2=Basic, 3=Pro, 4=Enterprise). Enterprise is rejected (contact-sales).</summary>
    [Required]
    [Range(1, 4)]
    public int PlanTypeId { get; set; }

    /// <summary>"Monthly" | "Annual". The pricing group is derived from the business macro category.</summary>
    [Required]
    public string BillingCycle { get; set; } = "Monthly";

    [Required]
    public string SuccessUrl { get; set; } = null!;

    [Required]
    public string CancelUrl { get; set; } = null!;
}
