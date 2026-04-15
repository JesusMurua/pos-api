using POS.Domain.Interfaces;

namespace POS.Domain.Models;

public class DeviceActivationCode : IBranchScoped
{
    public int Id { get; set; }
    public string Code { get; set; } = null!;
    public int BusinessId { get; set; }
    public int BranchId { get; set; }
    public string Mode { get; set; } = null!;
    public int CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public bool IsUsed { get; set; }

    public virtual Business Business { get; set; } = null!;
    public virtual Branch Branch { get; set; } = null!;
    public virtual User Creator { get; set; } = null!;
}
