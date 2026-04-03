namespace POS.Services.IService;

/// <summary>
/// Provides email sending operations via Resend.
/// </summary>
public interface IEmailService
{
    /// <summary>
    /// Sends a welcome email to a newly registered user. Best-effort — never throws.
    /// </summary>
    Task SendWelcomeEmailAsync(string email, string name, string businessName);
}
