namespace POS.Domain.Settings;

public class EmailSettings
{
    public string ApiKey { get; set; } = null!;
    public string FromEmail { get; set; } = "noreply@postactil.com";
    public string FromName { get; set; } = "Kaja POS";
}
