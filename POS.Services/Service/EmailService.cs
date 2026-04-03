using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using POS.Domain.Settings;
using POS.Services.IService;

namespace POS.Services.Service;

/// <summary>
/// Sends transactional emails via Resend API (https://api.resend.com/emails).
/// </summary>
public class EmailService : IEmailService
{
    private readonly HttpClient _httpClient;
    private readonly EmailSettings _settings;
    private readonly ILogger<EmailService> _logger;

    public EmailService(
        HttpClient httpClient,
        IOptions<EmailSettings> settings,
        ILogger<EmailService> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;

        _httpClient.BaseAddress = new Uri("https://api.resend.com/");
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _settings.ApiKey);
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
    }

    /// <inheritdoc />
    public async Task SendWelcomeEmailAsync(string email, string name, string businessName)
    {
        try
        {
            var htmlBody = BuildWelcomeHtml(name, businessName);

            var payload = new
            {
                from = $"{_settings.FromName} <{_settings.FromEmail}>",
                to = new[] { email },
                subject = "Bienvenido a Kaja POS",
                html = htmlBody
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("emails", content);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Welcome email sent to {Email}", email);
            }
            else
            {
                var body = await response.Content.ReadAsStringAsync();
                _logger.LogError(
                    "Resend API failed for {Email}. Status: {Status}, Response: {Body}",
                    email, (int)response.StatusCode, body);
            }
        }
        catch (TaskCanceledException)
        {
            _logger.LogError("Welcome email to {Email} timed out after 10s", email);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error sending welcome email to {Email}", email);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error sending welcome email to {Email}", email);
        }
    }

    #region Private Helper Methods

    private static string BuildWelcomeHtml(string name, string businessName)
    {
        return $"""
            <!DOCTYPE html>
            <html lang="es">
            <head><meta charset="UTF-8"></head>
            <body style="font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; background-color: #f5f5f5; margin: 0; padding: 0;">
              <table width="100%" cellpadding="0" cellspacing="0" style="padding: 40px 20px;">
                <tr>
                  <td align="center">
                    <table width="600" cellpadding="0" cellspacing="0" style="background-color: #ffffff; border-radius: 8px; overflow: hidden;">
                      <tr>
                        <td style="background-color: #6366f1; padding: 32px; text-align: center;">
                          <h1 style="color: #ffffff; margin: 0; font-size: 28px;">Kaja POS</h1>
                        </td>
                      </tr>
                      <tr>
                        <td style="padding: 40px 32px;">
                          <h2 style="color: #1f2937; margin: 0 0 16px;">Hola, {name}!</h2>
                          <p style="color: #4b5563; font-size: 16px; line-height: 1.6; margin: 0 0 24px;">
                            Bienvenido a <strong>Kaja POS</strong>! Tu cuenta ha sido creada con exito
                            para el negocio <strong>{businessName}</strong>.
                          </p>
                          <p style="color: #4b5563; font-size: 16px; line-height: 1.6; margin: 0 0 24px;">
                            Tu periodo de prueba gratuito de <strong>3 meses</strong> ya esta activo.
                            Puedes comenzar a configurar tus productos, categorias y mesas desde la app.
                          </p>
                          <p style="color: #9ca3af; font-size: 14px; margin: 0;">
                            Si tienes alguna pregunta, responde a este correo y te ayudaremos.
                          </p>
                        </td>
                      </tr>
                      <tr>
                        <td style="background-color: #f9fafb; padding: 24px 32px; text-align: center;">
                          <p style="color: #9ca3af; font-size: 12px; margin: 0;">
                            Kaja POS &mdash; Tu punto de venta inteligente
                          </p>
                        </td>
                      </tr>
                    </table>
                  </td>
                </tr>
              </table>
            </body>
            </html>
            """;
    }

    #endregion
}
