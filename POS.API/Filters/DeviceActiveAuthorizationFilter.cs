using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using POS.Services.IService;

namespace POS.API.Filters;

/// <summary>
/// Global MVC authorization filter that closes the 10-year device token
/// vulnerability. Runs on every action and — only when the incoming JWT carries
/// <c>type=device</c> — consults <see cref="IDeviceAuthorizationService"/> to
/// verify the device is still active. Human tokens and anonymous requests pass
/// through untouched.
/// </summary>
public class DeviceActiveAuthorizationFilter : IAsyncAuthorizationFilter
{
    private readonly IDeviceAuthorizationService _deviceAuth;
    private readonly ILogger<DeviceActiveAuthorizationFilter> _logger;

    public DeviceActiveAuthorizationFilter(
        IDeviceAuthorizationService deviceAuth,
        ILogger<DeviceActiveAuthorizationFilter> logger)
    {
        _deviceAuth = deviceAuth;
        _logger = logger;
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;

        // Anonymous endpoints and un-authenticated requests: nothing to gate.
        if (user?.Identity is null || !user.Identity.IsAuthenticated)
            return;

        var typeClaim = user.FindFirst("type")?.Value;
        if (typeClaim != "device")
            return;

        var deviceIdClaim = user.FindFirst("deviceId")?.Value;
        if (!int.TryParse(deviceIdClaim, out var deviceId))
        {
            _logger.LogWarning(
                "Device token missing or malformed deviceId claim. Remote IP: {RemoteIp}",
                context.HttpContext.Connection.RemoteIpAddress);
            context.Result = new UnauthorizedResult();
            return;
        }

        var isActive = await _deviceAuth.IsDeviceActiveAsync(deviceId);
        if (isActive is null or false)
        {
            _logger.LogInformation(
                "Rejected revoked device token. DeviceId: {DeviceId}, Path: {Path}",
                deviceId, context.HttpContext.Request.Path);
            context.Result = new UnauthorizedResult();
        }
    }
}
