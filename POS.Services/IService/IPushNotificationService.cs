using POS.Domain.Models;

namespace POS.Services.IService;

public interface IPushNotificationService
{
    Task SaveSubscriptionAsync(int userId, int branchId, PushSubscriptionDto dto);
    Task RemoveSubscriptionAsync(string endpoint);
    Task SendToBranchAsync(int branchId, string title, string body, object? data = null);
    Task SendToUserAsync(int userId, string title, string body, object? data = null);
}

public class PushSubscriptionDto
{
    public string Endpoint { get; set; } = null!;
    public string P256dh { get; set; } = null!;
    public string Auth { get; set; } = null!;
    public string? DeviceInfo { get; set; }
}
