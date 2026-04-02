namespace POS.API.Extensions;

/// <summary>
/// Extension methods for extracting custom headers from HttpContext.
/// </summary>
public static class HttpContextExtensions
{
    /// <summary>
    /// Reads the X-Timezone header sent by the frontend (IANA format).
    /// </summary>
    public static string? GetClientTimeZone(this HttpContext context)
        => context.Request.Headers["X-Timezone"].FirstOrDefault();
}
