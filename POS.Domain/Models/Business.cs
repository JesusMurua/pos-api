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

    public virtual ICollection<Branch>? Branches { get; set; }

    public virtual ICollection<User>? Users { get; set; }

    public Subscription? Subscription { get; set; }
}
