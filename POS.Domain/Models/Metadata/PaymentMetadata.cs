namespace POS.Domain.Models.Metadata;

/// <summary>
/// Strongly-typed metadata payload stored on <see cref="OrderPayment"/>. Captures
/// terminal/provider-specific data (Clip, MercadoPago, etc.) as strict typed
/// properties. Persisted as PostgreSQL <c>jsonb</c> via EF Core 9 owned-type
/// JSON mapping. Dynamic tenant-specific data lives on the parent entity via
/// <c>OrderPayment.ExtensionData</c>.
/// </summary>
public class PaymentMetadata
{
    #region Universal Provider Fields

    /// <summary>
    /// Verbatim provider payload (e.g. the entire JSON returned by Clip or
    /// MercadoPago). Preserved so receipts and reconciliation flows can render
    /// or audit the original provider response.
    /// </summary>
    public string? RawProviderJson { get; set; }

    /// <summary>
    /// Authorization or approval code returned by the card network / terminal.
    /// </summary>
    public string? AuthorizationCode { get; set; }

    /// <summary>
    /// Last four digits of the card used (when applicable). Never store the PAN.
    /// </summary>
    public string? Last4 { get; set; }

    /// <summary>
    /// Card brand (<c>Visa</c>, <c>Mastercard</c>, <c>Amex</c>, etc.) when
    /// reported by the terminal.
    /// </summary>
    public string? CardBrand { get; set; }

    #endregion
}
