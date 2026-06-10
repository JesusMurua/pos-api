using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using POS.Domain.Enums;
using POS.Domain.Settings;
using POS.Services.IService;

namespace POS.Services.Service;

/// <summary>
/// Sends transactional emails via the Resend API (https://api.resend.com/emails).
/// Pure send layer — called only by the dispatcher; queuing/retries live in the outbox.
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
    public async Task<EmailSendResult> SendNowAsync(string toEmail, string subject, string bodyHtml, string bodyText)
    {
        try
        {
            var payload = new
            {
                from = $"{_settings.FromName} <{_settings.FromEmail}>",
                to = new[] { toEmail },
                subject,
                html = bodyHtml,
                text = bodyText
            };

            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync("emails", content);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Email '{Subject}' sent to {Email}", subject, toEmail);
                return EmailSendResult.Sent;
            }

            var body = await response.Content.ReadAsStringAsync();
            _logger.LogError("Resend failed for {Email}. Status: {Status}, Response: {Body}",
                toEmail, (int)response.StatusCode, body);

            // IMPORTANT: do NOT collapse all 4xx into PermanentError — HTTP 429 (rate limit)
            // is a 4xx but MUST be retried (the trap). Only genuinely client-side errors
            // (bad recipient / invalid payload, i.e. other 4xx) are permanent.
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                return EmailSendResult.TransientError;

            return (int)response.StatusCode >= 500
                ? EmailSendResult.TransientError
                : EmailSendResult.PermanentError;
        }
        catch (TaskCanceledException)
        {
            _logger.LogError("Email to {Email} timed out after 10s", toEmail);
            return EmailSendResult.TransientError;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error sending email to {Email}", toEmail);
            return EmailSendResult.TransientError;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error sending email to {Email}", toEmail);
            return EmailSendResult.TransientError;
        }
    }
}
