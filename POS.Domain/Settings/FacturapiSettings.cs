namespace POS.Domain.Settings;

/// <summary>
/// Configuration settings for Facturapi CFDI 4.0 integration.
/// Bound from appsettings.json "Facturapi" section.
/// </summary>
public class FacturapiSettings
{
    /// <summary>Master API key for the Facturapi platform account.</summary>
    public string ApiKey { get; set; } = null!;

    /// <summary>Secret used to validate incoming webhooks from Facturapi.</summary>
    public string WebhookSecret { get; set; } = string.Empty;

    /// <summary>When true, uses Facturapi sandbox environment for testing.</summary>
    public bool IsSandbox { get; set; } = true;
}
