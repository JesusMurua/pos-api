using System.ComponentModel.DataAnnotations;

namespace POS.API.Models;

/// <summary>
/// Request body for creating a Stripe Checkout session.
/// </summary>
public class CheckoutRequest
{
    [Required]
    public string PriceId { get; set; } = null!;

    [Required]
    public string SuccessUrl { get; set; } = null!;

    [Required]
    public string CancelUrl { get; set; } = null!;
}
