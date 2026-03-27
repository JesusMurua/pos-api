namespace POS.Domain.Models;

public class PushSubscription
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int BranchId { get; set; }
    public string Endpoint { get; set; } = null!;
    public string P256dh { get; set; } = null!;
    public string Auth { get; set; } = null!;
    public string? DeviceInfo { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual User User { get; set; } = null!;
    public virtual Branch Branch { get; set; } = null!;
}
