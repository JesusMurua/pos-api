using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace POS.Services.Adapter;

/// <summary>
/// HTTP client for Facturapi REST API (https://docs.facturapi.io).
/// Uses IHttpClientFactory via named HttpClient "Facturapi".
/// API Key is set as Bearer token via DI configuration.
/// </summary>
public class FacturapiClient : IFacturapiClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<FacturapiClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public FacturapiClient(HttpClient httpClient, ILogger<FacturapiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<FacturapiCustomerResponse> CreateCustomerAsync(FacturapiCustomerRequest request)
    {
        var payload = new
        {
            legal_name = request.LegalName,
            tax_id = request.Rfc,
            tax_system = request.TaxSystem,
            address = new { zip = request.Zip },
            email = request.Email
        };

        var response = await _httpClient.PostAsJsonAsync("v2/customers", payload, JsonOptions);
        await EnsureSuccessOrThrow(response, "CreateCustomer");

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        return new FacturapiCustomerResponse
        {
            Id = result.GetProperty("id").GetString()!
        };
    }

    /// <inheritdoc />
    public async Task<FacturapiInvoiceResponse> CreateInvoiceAsync(FacturapiInvoiceRequest request)
    {
        var payload = new
        {
            type = "I",
            customer = request.CustomerId,
            payment_form = request.PaymentForm,
            payment_method = request.PaymentMethod,
            use = request.Use,
            items = request.Items.Select(i => new
            {
                product = new
                {
                    description = i.Description,
                    product_key = i.ProductCode,
                    unit_key = i.UnitCode,
                    unit_name = "Pieza",
                    price = i.UnitPrice,
                    tax_included = true,
                    taxes = new[]
                    {
                        new { type = "IVA", rate = i.TaxRate }
                    }
                },
                quantity = i.Quantity
            })
        };

        var response = await _httpClient.PostAsJsonAsync("v2/invoices", payload, JsonOptions);
        await EnsureSuccessOrThrow(response, "CreateInvoice");

        return await ParseInvoiceResponse(response);
    }

    /// <inheritdoc />
    public async Task<FacturapiInvoiceResponse> CreateGlobalInvoiceAsync(FacturapiGlobalInvoiceRequest request)
    {
        var payload = new
        {
            type = "I",
            customer = new
            {
                legal_name = "PUBLICO EN GENERAL",
                tax_id = "XAXX010101000",
                tax_system = "616",
                address = new { zip = "00000" }
            },
            payment_form = "01",
            payment_method = "PUE",
            use = "S01",
            global_invoice = new
            {
                periodicity = request.Periodicity,
                months = request.Month.ToString("D2"),
                year = request.Year
            },
            items = request.Items.Select(i => new
            {
                product = new
                {
                    description = i.Description,
                    product_key = i.ProductCode,
                    unit_key = i.UnitCode,
                    unit_name = "Pieza",
                    price = i.UnitPrice,
                    tax_included = true,
                    taxes = new[]
                    {
                        new { type = "IVA", rate = i.TaxRate }
                    }
                },
                quantity = i.Quantity
            })
        };

        var response = await _httpClient.PostAsJsonAsync("v2/invoices", payload, JsonOptions);
        await EnsureSuccessOrThrow(response, "CreateGlobalInvoice");

        return await ParseInvoiceResponse(response);
    }

    /// <inheritdoc />
    public async Task<FacturapiInvoiceResponse> CancelInvoiceAsync(
        string facturapiInvoiceId, string cancellationReason)
    {
        var response = await _httpClient.DeleteAsync(
            $"v2/invoices/{facturapiInvoiceId}?motive={cancellationReason}");
        await EnsureSuccessOrThrow(response, "CancelInvoice");

        return await ParseInvoiceResponse(response);
    }

    #region Private Helpers

    private async Task EnsureSuccessOrThrow(HttpResponseMessage response, string operation)
    {
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            _logger.LogError("Facturapi {Operation} failed: {StatusCode} — {Body}",
                operation, response.StatusCode, body);
            throw new HttpRequestException(
                $"Facturapi {operation} failed with status {(int)response.StatusCode}: {body}");
        }
    }

    private static async Task<FacturapiInvoiceResponse> ParseInvoiceResponse(HttpResponseMessage response)
    {
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        return new FacturapiInvoiceResponse
        {
            Id = json.GetProperty("id").GetString()!,
            Status = json.TryGetProperty("status", out var s) ? s.GetString() : null,
            Series = json.TryGetProperty("series", out var sr) ? sr.GetString() : null,
            FolioNumber = json.TryGetProperty("folio_number", out var fn) ? fn.ToString() : null,
            Total = json.TryGetProperty("total", out var t) ? t.GetDecimal() : null,
            PdfUrl = json.TryGetProperty("pdf_custom_section", out var pdf) ? pdf.GetString() : null,
            XmlUrl = json.TryGetProperty("xml", out var xml) ? xml.GetString() : null
        };
    }

    #endregion
}
