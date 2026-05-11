using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;

namespace POS.Services.Adapter;

/// <summary>
/// Symmetric encryption adapter for <c>Customer.BiometricTemplate</c>. Wraps
/// <see cref="IDataProtector"/> with a versioned purpose string so future key
/// rotations can target this specific column without affecting other ciphers
/// (delivery API keys, payment provider tokens). Encryption is recoverable —
/// templates must be decrypted in-memory for fingerprint matching at the
/// access-control endpoint in Milestone 2.
/// </summary>
public class BiometricDataProtector
{
    private const string Purpose = "BiometricTemplate.v1";

    private readonly IDataProtector _protector;
    private readonly ILogger<BiometricDataProtector> _logger;

    public BiometricDataProtector(
        IDataProtectionProvider provider,
        ILogger<BiometricDataProtector> logger)
    {
        _protector = provider.CreateProtector(Purpose);
        _logger = logger;
    }

    /// <summary>Encrypts a raw biometric template prior to persistence.</summary>
    public byte[] Encrypt(byte[] plainText) => _protector.Protect(plainText);

    /// <summary>
    /// Decrypts a stored biometric template. Re-throws
    /// <see cref="CryptographicException"/> on failure so callers default to
    /// deny-by-default — silently returning <c>null</c> would let a key-rotation
    /// outage be misinterpreted as "no template configured" and grant access.
    /// </summary>
    public byte[] Decrypt(byte[] cipherText)
    {
        try
        {
            return _protector.Unprotect(cipherText);
        }
        catch (CryptographicException ex)
        {
            _logger.LogError(ex,
                "Failed to decrypt BiometricTemplate. Possible key rotation, " +
                "data corruption, or version mismatch.");
            throw;
        }
    }
}
