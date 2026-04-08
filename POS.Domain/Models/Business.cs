using System.ComponentModel.DataAnnotations;
using POS.Domain.Enums;
using POS.Domain.Models.Catalogs;

namespace POS.Domain.Models;

public partial class Business
{
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = null!;

    public BusinessType BusinessType { get; set; } = BusinessType.General;

    public PlanType PlanType { get; set; } = PlanType.Free;

    public DateTime? TrialEndsAt { get; set; }

    public bool TrialUsed { get; set; }

    public bool OnboardingCompleted { get; set; }

    /// <summary>FK to OnboardingStatusCatalog.Id (1=Pending, 2=InProgress, 3=Completed, 4=Skipped).</summary>
    public int OnboardingStatusId { get; set; } = 1;

    /// <summary>Tracks the current onboarding step the user is on (1-based).</summary>
    public int CurrentOnboardingStep { get; set; } = 1;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>ISO 3166-1 alpha-2 country code (e.g., "MX" for Mexico).</summary>
    [MaxLength(2)]
    public string CountryCode { get; set; } = "MX";

    #region Fiscal / Invoicing Fields

    /// <summary>RFC of the business (tax ID). 12 chars for companies, 13 for individuals.</summary>
    [MaxLength(13)]
    public string? Rfc { get; set; }

    /// <summary>SAT tax regime code (e.g., "601" = General de Ley, "612" = Personas Fisicas).</summary>
    [MaxLength(3)]
    public string? TaxRegime { get; set; }

    /// <summary>Legal name exactly as registered with SAT (razon social).</summary>
    [MaxLength(300)]
    public string? LegalName { get; set; }

    /// <summary>Whether electronic invoicing (CFDI) is enabled for this business.</summary>
    public bool InvoicingEnabled { get; set; } = false;

    /// <summary>Facturapi Organization ID linked to this business.</summary>
    [MaxLength(50)]
    public string? FacturapiOrganizationId { get; set; }

    #endregion

    #region Loyalty Program Config

    /// <summary>Whether the loyalty points program is enabled for this business.</summary>
    public bool LoyaltyEnabled { get; set; } = false;

    /// <summary>Points awarded per CurrencyUnitsPerPoint cents spent. Default: 1.</summary>
    public int PointsPerCurrencyUnit { get; set; } = 1;

    /// <summary>Cents the customer must spend to earn PointsPerCurrencyUnit points. Default: 1000 ($10 MXN).</summary>
    public int CurrencyUnitsPerPoint { get; set; } = 1000;

    /// <summary>Value in cents of each point when redeemed. Default: 10 ($0.10 MXN).</summary>
    public int PointRedemptionValueCents { get; set; } = 10;

    #endregion

    public OnboardingStatusCatalog? OnboardingStatus { get; set; }

    public virtual ICollection<BusinessGiro> BusinessGiros { get; set; } = new List<BusinessGiro>();

    public virtual ICollection<Branch>? Branches { get; set; }

    public virtual ICollection<User>? Users { get; set; }

    public Subscription? Subscription { get; set; }
}
