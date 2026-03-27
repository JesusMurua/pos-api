using System.Text.Json;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using POS.Domain.Settings;
using POS.Repository;
using POS.Services.IService;

namespace POS.Services.Service;

public class PushNotificationService : IPushNotificationService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly VapidSettings _vapidSettings;
    private readonly ILogger<PushNotificationService> _logger;

    public PushNotificationService(
        IUnitOfWork unitOfWork,
        IOptions<VapidSettings> vapidSettings,
        ILogger<PushNotificationService> logger)
    {
        _unitOfWork = unitOfWork;
        _vapidSettings = vapidSettings.Value;
        _logger = logger;
    }

    #region Public API Methods

    /// <summary>
    /// Saves or updates a push subscription (upsert by endpoint).
    /// </summary>
    public async Task SaveSubscriptionAsync(int userId, int branchId, PushSubscriptionDto dto)
    {
        var existing = await _unitOfWork.PushSubscriptions.GetByEndpointAsync(dto.Endpoint);

        if (existing != null)
        {
            existing.UserId = userId;
            existing.BranchId = branchId;
            existing.P256dh = dto.P256dh;
            existing.Auth = dto.Auth;
            existing.DeviceInfo = dto.DeviceInfo;
            _unitOfWork.PushSubscriptions.Update(existing);
        }
        else
        {
            var subscription = new Domain.Models.PushSubscription
            {
                UserId = userId,
                BranchId = branchId,
                Endpoint = dto.Endpoint,
                P256dh = dto.P256dh,
                Auth = dto.Auth,
                DeviceInfo = dto.DeviceInfo,
                CreatedAt = DateTime.UtcNow
            };
            await _unitOfWork.PushSubscriptions.AddAsync(subscription);
        }

        await _unitOfWork.SaveChangesAsync();
    }

    /// <summary>
    /// Removes a push subscription by its endpoint.
    /// </summary>
    public async Task RemoveSubscriptionAsync(string endpoint)
    {
        var subscription = await _unitOfWork.PushSubscriptions.GetByEndpointAsync(endpoint);
        if (subscription != null)
        {
            _unitOfWork.PushSubscriptions.Delete(subscription);
            await _unitOfWork.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Sends a push notification to all subscribers in a branch. Best-effort.
    /// </summary>
    public async Task SendToBranchAsync(int branchId, string title, string body, object? data = null)
    {
        var subscriptions = await _unitOfWork.PushSubscriptions.GetByBranchAsync(branchId);
        await SendToSubscriptionsAsync(subscriptions, title, body, data);
    }

    /// <summary>
    /// Sends a push notification to all devices of a specific user. Best-effort.
    /// </summary>
    public async Task SendToUserAsync(int userId, string title, string body, object? data = null)
    {
        var subscriptions = await _unitOfWork.PushSubscriptions.GetByUserAsync(userId);
        await SendToSubscriptionsAsync(subscriptions, title, body, data);
    }

    #endregion

    #region Private Helper Methods

    private async Task SendToSubscriptionsAsync(
        IEnumerable<Domain.Models.PushSubscription> subscriptions,
        string title, string body, object? data)
    {
        if (string.IsNullOrEmpty(_vapidSettings.PublicKey) || string.IsNullOrEmpty(_vapidSettings.PrivateKey))
        {
            _logger.LogWarning("VAPID keys not configured — skipping push notifications");
            return;
        }

        var client = new PushServiceClient();
        client.DefaultAuthentication = new VapidAuthentication(
            _vapidSettings.PublicKey,
            _vapidSettings.PrivateKey)
        {
            Subject = _vapidSettings.Subject
        };

        var payload = JsonSerializer.Serialize(new { title, body, data });

        foreach (var sub in subscriptions)
        {
            try
            {
                var pushSubscription = new Lib.Net.Http.WebPush.PushSubscription
                {
                    Endpoint = sub.Endpoint,
                    Keys = new Dictionary<string, string>
                    {
                        ["p256dh"] = sub.P256dh,
                        ["auth"] = sub.Auth
                    }
                };

                var message = new PushMessage(payload)
                {
                    Urgency = PushMessageUrgency.High
                };

                await client.RequestPushMessageDeliveryAsync(pushSubscription, message);
            }
            catch (PushServiceClientException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Gone)
            {
                _logger.LogInformation("Push subscription expired, removing: {Endpoint}", sub.Endpoint);
                _unitOfWork.PushSubscriptions.Delete(sub);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send push to {Endpoint}", sub.Endpoint);
            }
        }

        await _unitOfWork.SaveChangesAsync();
    }

    #endregion
}
