using Microsoft.Extensions.Caching.Memory;
using POS.Repository;
using POS.Services.IService;

namespace POS.Services.Service;

/// <summary>
/// In-memory implementation of <see cref="IDeviceAuthorizationService"/>.
/// Cache entries live for <see cref="CacheTtl"/> and are invalidated
/// explicitly by admin mutations that touch a device.
/// </summary>
public class DeviceAuthorizationService : IDeviceAuthorizationService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);
    private const string KeyPrefix = "device:active:";

    private readonly IUnitOfWork _unitOfWork;
    private readonly IMemoryCache _cache;

    public DeviceAuthorizationService(IUnitOfWork unitOfWork, IMemoryCache cache)
    {
        _unitOfWork = unitOfWork;
        _cache = cache;
    }

    /// <inheritdoc />
    public async Task<bool?> IsDeviceActiveAsync(int deviceId)
    {
        var key = KeyPrefix + deviceId.ToString();

        if (_cache.TryGetValue(key, out bool? cached))
            return cached;

        var fromDb = await _unitOfWork.Devices.GetIsActiveAsync(deviceId);

        _cache.Set(key, fromDb, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = CacheTtl
        });

        return fromDb;
    }

    /// <inheritdoc />
    public void Invalidate(int deviceId)
    {
        _cache.Remove(KeyPrefix + deviceId.ToString());
    }
}
