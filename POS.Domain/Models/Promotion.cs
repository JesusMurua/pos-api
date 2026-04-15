using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using POS.Domain.Enums;
using POS.Domain.Models.Catalogs;

using POS.Domain.Interfaces;

namespace POS.Domain.Models;

public partial class Promotion : IBranchScoped
{
    public int Id { get; set; }

    public int BranchId { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = null!;

    [MaxLength(255)]
    public string? Description { get; set; }

    /// <summary>FK to PromotionTypeCatalog.Id (1=Percentage, 2=Fixed, 3=Bogo, 4=Bundle, 5=OrderDiscount, 6=FreeProduct).</summary>
    public int PromotionTypeId { get; set; }

    public PromotionScope AppliesTo { get; set; }

    public int Value { get; set; }

    public int? MinQuantity { get; set; }
    public int? PaidQuantity { get; set; }
    public int? FreeProductId { get; set; }
    public int? CategoryId { get; set; }
    public int? ProductId { get; set; }

    public int? DaysOfWeek { get; set; }

    public DateTime? StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }

    public int? MinOrderCents { get; set; }
    public int? MaxUsesTotal { get; set; }
    public int? MaxUsesPerDay { get; set; }

    [MaxLength(50)]
    public string? CouponCode { get; set; }

    public bool IsStackable { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public PromotionTypeCatalog? PromotionTypeCatalog { get; set; }

    public virtual Branch Branch { get; set; } = null!;
    public virtual ICollection<PromotionUsage> Usages { get; set; } = new List<PromotionUsage>();

    [NotMapped]
    public bool IsCurrentlyActive
    {
        get
        {
            if (!IsActive) return false;

            var now = DateTime.UtcNow;

            if (StartsAt.HasValue && now < StartsAt.Value) return false;
            if (EndsAt.HasValue && now > EndsAt.Value) return false;

            if (DaysOfWeek.HasValue)
            {
                var todayBit = now.DayOfWeek switch
                {
                    System.DayOfWeek.Monday => 1,
                    System.DayOfWeek.Tuesday => 2,
                    System.DayOfWeek.Wednesday => 4,
                    System.DayOfWeek.Thursday => 8,
                    System.DayOfWeek.Friday => 16,
                    System.DayOfWeek.Saturday => 32,
                    System.DayOfWeek.Sunday => 64,
                    _ => 0
                };
                if ((DaysOfWeek.Value & todayBit) == 0) return false;
            }

            return true;
        }
    }
}
