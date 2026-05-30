using System.ComponentModel.DataAnnotations;

namespace POS.Domain.DTOs.Admin;

/// <summary>
/// Payload for <c>POST /api/Admin/businesses</c>. Mirrors the public
/// <c>RegisterApiRequest</c> shape with two admin-specific knobs:
/// <see cref="SuppressWelcomeEmail"/> (defaults to <c>true</c> so demo
/// tenants do not trigger Resend) and <see cref="IncludeOwnerJwt"/> (opt-in,
/// defaults to <c>false</c> to keep the owner JWT out of admin-side logs
/// unless explicitly requested).
/// </summary>
public sealed record AdminCreateBusinessRequest
{
    [Required]
    [MaxLength(100)]
    public string BusinessName { get; init; } = null!;

    [Required]
    [MaxLength(100)]
    public string OwnerName { get; init; } = null!;

    [Required]
    [EmailAddress]
    public string Email { get; init; } = null!;

    [Required]
    [MinLength(8)]
    public string Password { get; init; } = null!;

    /// <summary>
    /// Macro category id (1=FoodBeverage, 2=QuickService, 3=Retail, 4=Services).
    /// Drives POS experience, plan rules, and pricing.
    /// </summary>
    [Required]
    [Range(1, 4)]
    public int PrimaryMacroCategoryId { get; init; }

    /// <summary>FK to <c>PlanTypeCatalog.Id</c> (1=Free, 2=Basic, 3=Pro, 4=Enterprise). Defaults to Free.</summary>
    [Range(1, 4)]
    public int? PlanTypeId { get; init; }

    /// <summary>ISO 3166-1 alpha-2 country code. Defaults to "MX" when omitted.</summary>
    [MaxLength(2)]
    public string? CountryCode { get; init; }

    /// <summary>
    /// IANA timezone identifier (e.g. <c>America/Mexico_City</c>). When null,
    /// empty, or unresolvable, falls back to <c>America/Mexico_City</c>.
    /// </summary>
    [MaxLength(50)]
    public string? TimeZoneId { get; init; }

    /// <summary>
    /// Folio prefix for the matrix branch (e.g. "ANB"). Optional.
    /// </summary>
    [MaxLength(10)]
    public string? FolioPrefix { get; init; }

    /// <summary>
    /// When <c>true</c> (the admin-endpoint default), the welcome email is
    /// NOT dispatched. Most invocations create demo tenants where a welcome
    /// email would surprise the operator. Explicitly set to <c>false</c> when
    /// onboarding a real customer who should receive the email.
    /// </summary>
    public bool SuppressWelcomeEmail { get; init; } = true;

    /// <summary>
    /// When <c>true</c>, the response includes the newly-minted Owner JWT
    /// (<see cref="AdminCreateBusinessResponse.OwnerJwt"/>) so the super
    /// admin can drop straight into the new tenant's POS. Defaults to
    /// <c>false</c> to avoid the token leaking into admin-side network logs
    /// or screenshots unless explicitly opted in.
    /// </summary>
    public bool IncludeOwnerJwt { get; init; } = false;
}
