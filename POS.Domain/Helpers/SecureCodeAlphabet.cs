namespace POS.Domain.Helpers;

/// <summary>
/// Single source of truth for the alphabet used by every short, human-readable
/// alphanumeric code emitted by the API (device activation codes,
/// cash-register link codes, and any future surface in this shape).
/// Crockford-style: the 36-character alphanumeric set minus the visually
/// ambiguous characters (0, O, 1, I, L, U) so operators can read codes off a
/// screen and type them on a tablet without the typical confusion class.
/// </summary>
/// <remarks>
/// Consumed exclusively via <c>POS.Services.Helpers.SecureCodeGenerator</c>.
/// That generator uses 5-bit rejection sampling against
/// <see cref="Chars"/>.<c>Length</c>: each random byte is masked with
/// <c>0x1F</c> (yielding 0-31); bytes whose result is
/// <c>&gt;= Chars.Length</c> are discarded and re-drawn. This keeps the
/// output uniformly distributed across the alphabet.
/// <para>
/// The alphabet may grow up to 32 characters without algorithmic changes;
/// growing beyond 32 requires switching to a 6-bit mask. Shrinking is always
/// safe but increases the rejection ratio.
/// </para>
/// </remarks>
public static class SecureCodeAlphabet
{
    /// <summary>
    /// 30-character Crockford-style alphabet. Order is significant — the
    /// generator indexes into this string directly using bits from the CSPRNG.
    /// </summary>
    public const string Chars = "ABCDEFGHJKMNPQRSTVWXYZ23456789";

    /// <summary>
    /// Default length for short alphanumeric codes across the API. Mirrored
    /// by the <see cref="System.ComponentModel.DataAnnotations.RegularExpressionAttribute"/>
    /// on <c>ActivateDeviceRequest.Code</c> and <c>RedeemLinkCodeRequest.Code</c>,
    /// and by the persistence column constraints on
    /// <c>DeviceActivationCode.Code</c> and <c>CashRegisterLinkCode.Code</c>.
    /// </summary>
    public const int Length = 6;
}
