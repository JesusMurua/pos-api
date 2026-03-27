using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using POS.Domain.Settings;
using POS.Services.IService;

namespace POS.Services.Service;

public class SupabaseStorageService : IStorageService
{
    private readonly SupabaseSettings _settings;
    private readonly HttpClient _httpClient;
    private readonly ILogger<SupabaseStorageService> _logger;

    public SupabaseStorageService(
        IOptions<SupabaseSettings> settings,
        HttpClient httpClient,
        ILogger<SupabaseStorageService> logger)
    {
        _settings = settings.Value;
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// Uploads a file to Supabase Storage and returns the public URL.
    /// </summary>
    public async Task<string> UploadAsync(Stream file, string fileName, string contentType)
    {
        var path = $"{DateTime.UtcNow:yyyy-MM}/{Guid.NewGuid()}-{fileName}";
        var url = $"{_settings.Url}/storage/v1/object/{_settings.Bucket}/{path}";

        using var content = new StreamContent(file);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.ServiceKey);
        request.Content = content;

        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        return $"{_settings.Url}/storage/v1/object/public/{_settings.Bucket}/{path}";
    }

    /// <summary>
    /// Deletes a file from Supabase Storage by its public URL.
    /// </summary>
    public async Task DeleteAsync(string publicUrl)
    {
        var publicPrefix = $"{_settings.Url}/storage/v1/object/public/{_settings.Bucket}/";
        if (!publicUrl.StartsWith(publicPrefix))
        {
            _logger.LogWarning("Cannot delete URL not matching Supabase pattern: {Url}", publicUrl);
            return;
        }

        var path = publicUrl[publicPrefix.Length..];
        var url = $"{_settings.Url}/storage/v1/object/{_settings.Bucket}/{path}";

        using var request = new HttpRequestMessage(HttpMethod.Delete, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.ServiceKey);

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            _logger.LogWarning("Failed to delete from storage: {StatusCode} {Path}", response.StatusCode, path);
    }
}
