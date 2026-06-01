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

    /// <summary>
    /// Optional full sub-giro id set persisted as <c>BusinessGiro</c> rows
    /// inside the same registration transaction. Validated against the
    /// <c>BusinessTypeCatalog</c> seed; unknown ids roll the entire create
    /// back with a 400. Null skips sub-giro assignment so the customer can
    /// pick them later via <c>PUT /api/business/giro</c>.
    /// </summary>
    public IReadOnlyList<int>? SubGiroIds { get; init; }

    /// <summary>
    /// Optional free-text giro clarification persisted on
    /// <c>Business.CustomGiroDescription</c>. Used together with — or
    /// instead of — <see cref="SubGiroIds"/> when the operator picks
    /// "Otra" in the catalog.
    /// </summary>
    [MaxLength(100)]
    public string? CustomGiroDescription { get; init; }

    /// <summary>
    /// Optional fiscal configuration (RFC, tax regime, legal name, CFDI
    /// flag) persisted on the freshly created <c>Business</c> inside the
    /// same transaction. When <see cref="AdminFiscalConfigDto.InvoicingEnabled"/>
    /// is true, the CfdiInvoicing feature gate is intentionally bypassed
    /// because this path is admin provisioning, not end-user upgrade.
    /// Null leaves fiscal columns at their NULL / false defaults.
    /// </summary>
    public AdminFiscalConfigDto? FiscalConfig { get; init; }

    /// <summary>
    /// When <c>true</c>, the freshly created <c>Business</c> is marked as
    /// fully onboarded (<c>OnboardingCompleted = true</c>,
    /// <c>OnboardingStatusId = 3</c>) so the first Owner login receives a
    /// JWT with <c>onboardingCompleted = true</c> and the SPA route guard
    /// sends them straight to the dashboard instead of the wizard.
    /// </summary>
    public bool MarkOnboardingComplete { get; init; } = false;
}

/// <summary>
/// API-layer payload mirror of <c>POS.Services.IService.FiscalConfigInput</c>.
/// Lives in the Domain DTO layer so the API controller can validate /
/// document its shape with data annotations without leaking service types.
/// Mapped 1:1 to <c>FiscalConfigInput</c> inside the controller.
/// Declared as a non-positional record so MVC's data-annotation validator
/// can read the <c>MaxLength</c> attributes off the properties — positional
/// records bury the metadata on the synthesized constructor parameter and
/// the validator refuses to bind them.
/// </summary>
public sealed record AdminFiscalConfigDto
{
    [MaxLength(13)]
    public string? Rfc { get; init; }

    [MaxLength(3)]
    public string? TaxRegime { get; init; }

    [MaxLength(300)]
    public string? LegalName { get; init; }

    public bool InvoicingEnabled { get; init; }
}
