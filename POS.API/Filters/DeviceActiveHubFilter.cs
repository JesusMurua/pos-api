using Microsoft.AspNetCore.SignalR;
using POS.Services.IService;

namespace POS.API.Filters;

/// <summary>
/// SignalR hub filter that mirrors <see cref="DeviceActiveAuthorizationFilter"/>
/// for hub connections and invocations. Shares the same
/// <see cref="IDeviceAuthorizationService"/> instance (and therefore the same
/// <c>IMemoryCache</c>) as the MVC filter, so a single admin revocation covers
/// both pipelines on the next call.
/// </summary>
public class DeviceActiveHubFilter : IHubFilter
{
    private readonly IDeviceAuthorizationService _deviceAuth;
    private readonly ILogger<DeviceActiveHubFilter> _logger;

    public DeviceActiveHubFilter(
        IDeviceAuthorizationService deviceAuth,
        ILogger<DeviceActiveHubFilter> logger)
    {
        _deviceAuth = deviceAuth;
        _logger = logger;
    }

    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        await GateAsync(invocationContext.Context.User, invocationContext.HubMethodName);
        return await next(invocationContext);
    }

    public async Task OnConnectedAsync(
        HubLifetimeContext context,
        Func<HubLifetimeContext, Task> next)
    {
        await GateAsync(context.Context.User, hubMethod: null);
        await next(context);
    }

    public Task OnDisconnectedAsync(
        HubLifetimeContext context,
        Exception? exception,
        Func<HubLifetimeContext, Exception?, Task> next)
    {
        // Disconnection is not gated — let the underlying hub tear down normally
        // even if the device was revoked in-flight.
        return next(context, exception);
    }

    private async Task GateAsync(System.Security.Claims.ClaimsPrincipal? user, string? hubMethod)
    {
        if (user?.Identity is null || !user.Identity.IsAuthenticated)
            return;

        var typeClaim = user.FindFirst("type")?.Value;
        if (typeClaim != "device")
            return;

        var deviceIdClaim = user.FindFirst("deviceId")?.Value;
        if (!int.TryParse(deviceIdClaim, out var deviceId))
        {
            _logger.LogWarning("SignalR device token missing or malformed deviceId claim.");
            throw new HubException("Invalid device token");
        }

        var isActive = await _deviceAuth.IsDeviceActiveAsync(deviceId);
        if (isActive is null or false)
        {
            _logger.LogInformation(
                "Rejected revoked device on SignalR. DeviceId: {DeviceId}, HubMethod: {HubMethod}",
                deviceId, hubMethod ?? "<connect>");
            throw new HubException("Device revoked");
        }
    }
}
