namespace POS.Domain.Settings;

public class SupabaseSettings
{
    public string Url { get; set; } = string.Empty;
    public string ServiceKey { get; set; } = string.Empty;
    public string Bucket { get; set; } = "product-images";
}
