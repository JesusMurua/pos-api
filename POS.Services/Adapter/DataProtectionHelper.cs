using Microsoft.AspNetCore.DataProtection;
using POS.Repository;

namespace POS.Services.Adapter;

public class DataProtectionHelper : ISeedEncryptor
{
    private readonly IDataProtector _protector;
    private const string Purpose = "DeliveryApiKey.v1";

    public DataProtectionHelper(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector(Purpose);
    }

    /// <summary>Encrypts a plain text API key for storage.</summary>
    public string Encrypt(string plainText) => _protector.Protect(plainText);

    /// <summary>Decrypts a stored API key. Returns null if decryption fails.</summary>
    public string? Decrypt(string cipherText)
    {
        try { return _protector.Unprotect(cipherText); }
        catch { return null; }
    }
}
