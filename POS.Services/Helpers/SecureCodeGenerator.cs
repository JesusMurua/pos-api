using System.Security.Cryptography;
using POS.Domain.Helpers;

namespace POS.Services.Helpers;

/// <summary>
/// Shared, bias-free generator for short alphanumeric codes drawn from
/// <see cref="SecureCodeAlphabet"/>. Backed by the CSPRNG
/// (<see cref="RandomNumberGenerator"/>) and uses 5-bit rejection sampling
/// against <see cref="SecureCodeAlphabet"/>.<c>Chars.Length</c> so the output
/// distribution is provably uniform regardless of the alphabet's exact
/// cardinality.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this lives here:</b> introduced by BDD-017 to close a scope gap from
/// BDD-016. Previously each service that needed a 6-char code rolled its own
/// generator with its own ad-hoc charset; one of those (CashRegister link
/// codes) drifted into a 32-char alphabet that included the forbidden
/// <c>L</c> and <c>U</c>. Centralising here makes the alphabet impossible to
/// bypass at the call site — there is no overload that takes a charset.
/// </para>
/// <para>
/// <b>Bias-free guarantee:</b> a naive <c>byte % alphabet.Length</c> would be
/// biased whenever <c>256 % alphabet.Length != 0</c>. With a 30-char alphabet
/// (<c>256 % 30 == 16</c>) the low 16 indices would be over-represented by
/// roughly 6%. Rejection sampling sidesteps the modulo entirely: each random
/// byte's low 5 bits (<c>0..31</c>) are accepted only when the value lies
/// inside <c>[0, alphabet.Length)</c>; otherwise the byte is discarded and a
/// new one is drawn. Each accepted byte therefore corresponds to one of
/// exactly <c>alphabet.Length</c> equally-likely outcomes.
/// </para>
/// <para>
/// <b>Thread-safety:</b> <see cref="RandomNumberGenerator.Fill"/> is documented
/// thread-safe, and the generator holds no instance state. Safe for concurrent
/// callers without external locking.
/// </para>
/// <para>
/// <b>Performance profile:</b> cold path — invoked when an admin issues a new
/// code or link code. The per-byte CSPRNG draw is intentionally simple over a
/// buffered approach; expected cost is roughly <c>length × (32 / 30)</c>
/// CSPRNG byte draws per code (~6.4 for the default length of 6).
/// </para>
/// </remarks>
public static class SecureCodeGenerator
{
    /// <summary>
    /// Generates a cryptographically secure code of <paramref name="length"/>
    /// characters from <see cref="SecureCodeAlphabet"/>.<c>Chars</c>.
    /// </summary>
    /// <param name="length">
    /// Number of characters to emit. Defaults to
    /// <see cref="SecureCodeAlphabet"/>.<c>Length</c>. Must be positive.
    /// </param>
    /// <returns>A new string of exactly <paramref name="length"/> characters,
    /// each drawn uniformly from the secure alphabet.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="length"/> is non-positive.
    /// </exception>
    public static string Generate(int length = SecureCodeAlphabet.Length)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);

        Span<char> result = stackalloc char[length];
        Span<byte> buffer = stackalloc byte[1];

        for (var i = 0; i < length; i++)
        {
            int index;
            do
            {
                RandomNumberGenerator.Fill(buffer);
                index = buffer[0] & 0x1F;
            } while (index >= SecureCodeAlphabet.Chars.Length);

            result[i] = SecureCodeAlphabet.Chars[index];
        }

        return new string(result);
    }
}
