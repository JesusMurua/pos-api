namespace POS.Domain.Helpers;

/// <summary>
/// Single source of truth for the alphabet used by device activation codes.
/// Crockford-style: the 36-character alphanumeric set minus the visually
/// ambiguous characters (0, O, 1, I, L, U) so operators can read codes off a
/// screen and type them on a tablet without the typical confusion class.
/// </summary>
/// <remarks>
/// The generator in <c>DeviceService.GenerateSecureActivationCode</c> uses
/// 5-bit rejection sampling against <see cref="Chars"/>.<c>Length</c>: each
/// random byte is masked with <c>0x1F</c> (yielding 0-31); bytes whose result
/// is <c>&gt;= Chars.Length</c> are discarded and re-drawn. This keeps the
/// output uniformly distributed across the alphabet.
/// <para>
/// The alphabet may grow up to 32 characters without algorithmic changes;
/// growing beyond 32 requires switching to a 6-bit mask. Shrinking is always
/// safe but increases the rejection ratio.
/// </para>
/// </remarks>
public static class DeviceActivationAlphabet
{
    /// <summary>
    /// 30-character Crockford-style alphabet. Order is significant — the
    /// generator indexes into this string directly using bits from the CSPRNG.
    /// </summary>
    public const string Chars = "ABCDEFGHJKMNPQRSTVWXYZ23456789";

    /// <summary>
    /// Length of every activation code. Mirrored by the
    /// <see cref="System.ComponentModel.DataAnnotations.RegularExpressionAttribute"/>
    /// on <c>ActivateDeviceRequest.Code</c> and by the persistence column
    /// constraints on <c>DeviceActivationCode.Code</c>.
    /// </summary>
    public const int Length = 6;
}
