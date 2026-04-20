namespace POS.Services.IService;

/// <summary>
/// Thin service that gates device tokens against the persisted
/// <c>Device.IsActive</c> flag with a short-TTL in-memory cache. Consumed by
/// <c>DeviceActiveAuthorizationFilter</c> (MVC) and <c>DeviceActiveHubFilter</c>
/// (SignalR); both share the same cache instance so a single revocation covers
/// both pipelines simultaneously.
/// </summary>
public interface IDeviceAuthorizationService
{
    /// <summary>
    /// Returns <c>true</c> when the device exists and is active, <c>false</c>
    /// when it exists but is revoked, <c>null</c> when it does not exist. The
    /// cache holds every outcome — including negatives — for the configured TTL.
    /// </summary>
    Task<bool?> IsDeviceActiveAsync(int deviceId);

    /// <summary>
    /// Removes the cached entry for <paramref name="deviceId"/>. Called by
    /// admin mutations (<c>ToggleActiveAsync</c>, <c>UpdateDeviceAsync</c>) so
    /// revocation and operational changes propagate within the same request
    /// cycle on the toggling process rather than waiting for TTL expiry.
    /// </summary>
    void Invalidate(int deviceId);
}
