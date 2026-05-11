using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using POS.Domain.Settings;
using POS.Services.IService;

namespace POS.Services.Service;

/// <summary>
/// HMAC-SHA256 implementation of <see cref="IHmacService"/> backed by the
/// server-side secret configured at <c>AccessControl:QrTokenHmacSecret</c>.
/// The constructor fail-fasts if the secret is missing or shorter than 32
/// bytes (RFC 2104 recommends key length ≥ output length for SHA-256).
/// </summary>
public class HmacService : IHmacService
{
    private const int MinSecretByteLength = 32;

    private readonly byte[] _secretKey;

    public HmacService(IOptions<AccessControlSettings> settings)
    {
        var secret = settings.Value.QrTokenHmacSecret;

        if (string.IsNullOrEmpty(secret))
        {
            throw new InvalidOperationException(
                "AccessControl:QrTokenHmacSecret is required. Set the " +
                "ACCESS_CONTROL_QR_TOKEN_HMAC_SECRET environment variable.");
        }

        var byteCount = Encoding.UTF8.GetByteCount(secret);
        if (byteCount < MinSecretByteLength)
        {
            throw new InvalidOperationException(
                $"AccessControl:QrTokenHmacSecret must be at least " +
                $"{MinSecretByteLength} bytes (got {byteCount}). HMAC-SHA256 " +
                "is cryptographically weak with shorter keys.");
        }

        _secretKey = Encoding.UTF8.GetBytes(secret);
    }

    /// <inheritdoc />
    public string ComputeHash(string plainText)
    {
        var inputBytes = Encoding.UTF8.GetBytes(plainText);
        var hash = HMACSHA256.HashData(_secretKey, inputBytes);

        // Manual Base64URL transform (no padding): keeps the dependency footprint
        // identical to the rest of POS.Services and produces 43-char output for
        // the 32-byte SHA256 result.
        return Convert.ToBase64String(hash)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
