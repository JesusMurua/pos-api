namespace POS.Domain.Settings;

/// <summary>
/// Strongly-typed bindings for the <c>AccessControl</c> configuration section.
/// Holds secrets used by the gym/access-control cryptography contracts (HMAC for
/// QR tokens, etc.). Production deployments MUST override
/// <see cref="QrTokenHmacSecret"/> via the <c>ACCESS_CONTROL_QR_TOKEN_HMAC_SECRET</c>
/// environment variable; <c>appsettings.json</c> ships an empty value so that
/// <see cref="POS.Services.IService.IHmacService"/> fail-fasts at startup
/// instead of silently using a committed-to-source secret.
/// </summary>
public class AccessControlSettings
{
    public string QrTokenHmacSecret { get; set; } = string.Empty;
}
