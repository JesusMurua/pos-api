namespace POS.Services.IService;

/// <summary>
/// Computes deterministic HMAC-SHA256 hashes used for equality-preserving
/// lookups (e.g. <c>Customer.QrToken</c>). Same input + same server secret
/// always yields the same output, so a scanned QR can be hashed and matched
/// against the stored hash with a single SQL <c>WHERE QrToken = @hash</c>.
/// </summary>
public interface IHmacService
{
    /// <summary>
    /// Computes an HMAC-SHA256 of <paramref name="plainText"/> and returns it
    /// as a Base64URL string without padding (URL-safe, exactly 43 chars for
    /// the 32-byte SHA256 output). Fits comfortably in 100-char columns.
    /// </summary>
    string ComputeHash(string plainText);
}
