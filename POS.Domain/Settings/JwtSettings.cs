namespace POS.Domain.Settings;

public class JwtSettings
{
    public string Secret { get; set; } = null!;

    public string Issuer { get; set; } = null!;

    public string Audience { get; set; } = null!;

    public int OwnerExpirationDays { get; set; } = 30;

    public int PinExpirationHours { get; set; } = 12;
}
