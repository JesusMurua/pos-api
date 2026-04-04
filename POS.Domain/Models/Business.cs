using System.ComponentModel.DataAnnotations;
using POS.Domain.Enums;

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

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

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

    public virtual ICollection<Branch>? Branches { get; set; }

    public virtual ICollection<User>? Users { get; set; }

    public Subscription? Subscription { get; set; }
}
