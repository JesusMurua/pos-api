using POS.Domain.Enums;
using POS.Domain.Exceptions;
using POS.Domain.Helpers;
using POS.Domain.Models;
using POS.Repository;
using POS.Services.Adapter;
using POS.Services.IService;

namespace POS.Services.Service;

/// <summary>
/// Handles CFDI electronic invoice creation via Facturapi.
/// Creates Invoice entities and links them to Orders.
/// </summary>
public class InvoicingService : IInvoicingService
{
    private const decimal DefaultTaxRate = 0.16m;

    private readonly IUnitOfWork _unitOfWork;
    private readonly IFacturapiClient _facturapiClient;

    public InvoicingService(IUnitOfWork unitOfWork, IFacturapiClient facturapiClient)
    {
        _unitOfWork = unitOfWork;
        _facturapiClient = facturapiClient;
    }

    #region Public API Methods

    /// <summary>
    /// Creates a global invoice consolidating uninvoiced, paid, non-cancelled orders
    /// for a branch within the given date range.
    /// </summary>
    public async Task<GlobalInvoiceResult> CreateGlobalInvoiceAsync(
        DateTime startDate, DateTime endDate, int branchId)
    {
        var orders = (await _unitOfWork.Orders.GetAsync(
            o => o.BranchId == branchId
                && o.CreatedAt >= startDate
                && o.CreatedAt <= endDate
                && o.InvoiceStatus == InvoiceStatus.None
                && o.InvoiceId == null
                && o.IsPaid
                && o.CancellationReason == null,
            "Items,Payments")).ToList();

        if (orders.Count == 0)
            throw new ValidationException("No uninvoiced orders found for the specified period.");

        // Build line items from all order items across all orders
        var facItems = BuildInvoiceItems(orders);

        // Call Facturapi API
        var apiResponse = await _facturapiClient.CreateGlobalInvoiceAsync(new FacturapiGlobalInvoiceRequest
        {
            Month = startDate.Month,
            Year = startDate.Year,
            Periodicity = "month",
            Items = facItems
        });

        // Calculate totals
        var totalCents = orders.Sum(o => o.TotalCents);
        var taxCents = orders
            .Where(o => o.Items != null)
            .SelectMany(o => o.Items!)
            .Sum(i => i.TaxAmountCents);
        var subtotalCents = totalCents - taxCents;

        // Create Invoice entity
        var invoice = new Invoice
        {
            BusinessId = (await _unitOfWork.Branches.GetByIdAsync(branchId))!.BusinessId,
            BranchId = branchId,
            Type = InvoiceType.Global,
            Status = InvoiceStatus.Issued,
            FacturapiId = apiResponse.Id,
            Series = apiResponse.Series,
            FolioNumber = apiResponse.FolioNumber,
            TotalCents = totalCents,
            SubtotalCents = subtotalCents,
            TaxCents = taxCents,
            PaymentForm = "01",
            PdfUrl = apiResponse.PdfUrl,
            XmlUrl = apiResponse.XmlUrl,
            IssuedAt = DateTime.UtcNow
        };

        await _unitOfWork.Invoices.AddAsync(invoice);
        await _unitOfWork.SaveChangesAsync(); // Flush to get invoice.Id

        // Link all orders to the invoice
        foreach (var order in orders)
        {
            order.InvoiceId = invoice.Id;
            order.InvoiceStatus = InvoiceStatus.Issued;
            order.FacturapiId = apiResponse.Id;
            order.InvoicedAt = DateTime.UtcNow;
            _unitOfWork.Orders.Update(order);
        }

        await _unitOfWork.SaveChangesAsync();

        return new GlobalInvoiceResult
        {
            OrderCount = orders.Count,
            TotalCents = totalCents,
            FacturapiId = apiResponse.Id,
            Status = InvoiceStatus.Issued.ToString()
        };
    }

    /// <summary>
    /// Requests an individual invoice for a specific order linked to a fiscal customer.
    /// </summary>
    public async Task<IndividualInvoiceResult> RequestIndividualInvoiceAsync(
        string orderId, int fiscalCustomerId, int branchId)
    {
        var results = await _unitOfWork.Orders.GetAsync(o => o.Id == orderId, "Items,Payments");
        var order = results.FirstOrDefault()
            ?? throw new NotFoundException($"Order with id {orderId} not found");

        if (order.BranchId != branchId)
            throw new UnauthorizedException("Order does not belong to this branch");

        if (order.CancellationReason != null)
            throw new ValidationException("Cannot invoice a cancelled order.");

        if (order.InvoiceStatus == InvoiceStatus.Issued)
            throw new ValidationException("Order has already been invoiced.");

        var customer = await _unitOfWork.FiscalCustomers.GetByIdAsync(fiscalCustomerId)
            ?? throw new NotFoundException($"Fiscal customer with id {fiscalCustomerId} not found");

        // Ensure fiscal customer exists in Facturapi
        if (string.IsNullOrEmpty(customer.FacturapiCustomerId))
        {
            var custResponse = await _facturapiClient.CreateCustomerAsync(new FacturapiCustomerRequest
            {
                LegalName = customer.BusinessName,
                Rfc = customer.Rfc,
                TaxSystem = customer.TaxRegime,
                Zip = customer.ZipCode,
                Email = customer.Email
            });
            customer.FacturapiCustomerId = custResponse.Id;
            customer.UpdatedAt = DateTime.UtcNow;
            _unitOfWork.FiscalCustomers.Update(customer);
        }

        // Build invoice items
        var facItems = BuildInvoiceItems(new[] { order });

        // Determine dominant payment form
        var paymentForm = SatPaymentForm.FromDominantMethod(
            order.Payments.Select(p => (p.Method, p.AmountCents)));

        // Call Facturapi API
        var apiResponse = await _facturapiClient.CreateInvoiceAsync(new FacturapiInvoiceRequest
        {
            CustomerId = customer.FacturapiCustomerId,
            PaymentForm = paymentForm,
            Use = customer.CfdiUse ?? "G03",
            Items = facItems
        });

        // Calculate tax breakdown
        var taxCents = order.Items?.Sum(i => i.TaxAmountCents) ?? 0;
        var subtotalCents = order.TotalCents - taxCents;

        // Create Invoice entity
        var invoice = new Invoice
        {
            BusinessId = customer.BusinessId,
            BranchId = branchId,
            Type = InvoiceType.Individual,
            Status = InvoiceStatus.Issued,
            FacturapiId = apiResponse.Id,
            FiscalCustomerId = fiscalCustomerId,
            Series = apiResponse.Series,
            FolioNumber = apiResponse.FolioNumber,
            TotalCents = order.TotalCents,
            SubtotalCents = subtotalCents,
            TaxCents = taxCents,
            PaymentForm = paymentForm,
            PdfUrl = apiResponse.PdfUrl,
            XmlUrl = apiResponse.XmlUrl,
            IssuedAt = DateTime.UtcNow
        };

        await _unitOfWork.Invoices.AddAsync(invoice);
        await _unitOfWork.SaveChangesAsync();

        // Link order to invoice
        order.InvoiceId = invoice.Id;
        order.InvoiceStatus = InvoiceStatus.Issued;
        order.FacturapiId = apiResponse.Id;
        order.FiscalCustomerId = fiscalCustomerId;
        order.InvoicedAt = DateTime.UtcNow;
        _unitOfWork.Orders.Update(order);
        await _unitOfWork.SaveChangesAsync();

        return new IndividualInvoiceResult
        {
            OrderId = order.Id,
            CustomerRfc = customer.Rfc,
            TotalCents = order.TotalCents,
            FacturapiId = apiResponse.Id,
            Status = InvoiceStatus.Issued.ToString()
        };
    }

    /// <summary>
    /// Gets public order details for the self-invoicing portal.
    /// Validates receipt proof via totalCents match.
    /// </summary>
    public async Task<PublicOrderInvoiceInfo> GetPublicOrderDetailsAsync(string orderId, int totalCents)
    {
        var results = await _unitOfWork.Orders.GetAsync(
            o => o.Id == orderId, "Branch,Branch.Business,Invoice");
        var order = results.FirstOrDefault()
            ?? throw new NotFoundException("Order not found");

        if (order.TotalCents != totalCents)
            throw new UnauthorizedException("The provided order data does not match our records.");

        var business = order.Branch?.Business;
        var invoicingEnabled = business?.InvoicingEnabled ?? false;

        var canInvoice = invoicingEnabled
            && order.InvoiceStatus == InvoiceStatus.None
            && order.CancellationReason == null
            && order.CreatedAt > DateTime.UtcNow.AddDays(-40);

        // Pull URL from Invoice entity if available
        var invoiceUrl = order.Invoice?.PdfUrl ?? order.InvoiceUrl;

        return new PublicOrderInvoiceInfo
        {
            OrderId = order.Id,
            BusinessName = business?.Name ?? "Unknown",
            BranchName = order.Branch?.Name ?? "Unknown",
            Date = order.CreatedAt,
            TotalCents = order.TotalCents,
            InvoiceStatus = order.InvoiceStatus.ToString(),
            InvoicingEnabled = invoicingEnabled,
            CanInvoice = canInvoice,
            InvoiceUrl = order.InvoiceStatus == InvoiceStatus.Issued ? invoiceUrl : null
        };
    }

    /// <summary>
    /// Requests an invoice from the public self-invoicing portal.
    /// Creates or reuses FiscalCustomer, validates receipt proof, and issues the invoice.
    /// </summary>
    public async Task<IndividualInvoiceResult> RequestPublicInvoiceAsync(PublicInvoiceRequest request)
    {
        var results = await _unitOfWork.Orders.GetAsync(
            o => o.Id == request.OrderId, "Branch,Branch.Business");
        var order = results.FirstOrDefault()
            ?? throw new NotFoundException("Order not found");

        if (order.TotalCents != request.TotalCents)
            throw new UnauthorizedException("The provided order data does not match our records.");

        var business = order.Branch?.Business
            ?? throw new ValidationException("Business configuration not found for this order.");

        if (!business.InvoicingEnabled)
            throw new ValidationException("Electronic invoicing is not enabled for this business.");

        if (order.CancellationReason != null)
            throw new ValidationException("Cannot invoice a cancelled order.");

        if (order.InvoiceStatus == InvoiceStatus.Issued)
            throw new ValidationException("This order has already been invoiced.");

        if (order.CreatedAt <= DateTime.UtcNow.AddDays(-40))
            throw new ValidationException("The invoicing window for this order has expired (max 40 days).");

        // Upsert FiscalCustomer
        var rfc = request.Rfc.Trim().ToUpperInvariant();
        var customer = await _unitOfWork.FiscalCustomers.GetByRfcAsync(business.Id, rfc);

        if (customer == null)
        {
            customer = new FiscalCustomer
            {
                BusinessId = business.Id,
                Rfc = rfc,
                BusinessName = request.FiscalName,
                TaxRegime = request.TaxRegime,
                ZipCode = request.ZipCode,
                Email = request.Email,
                CfdiUse = request.CfdiUse ?? "G03"
            };
            await _unitOfWork.FiscalCustomers.AddAsync(customer);
            await _unitOfWork.SaveChangesAsync();
        }
        else if (!string.IsNullOrEmpty(request.Email) && customer.Email != request.Email)
        {
            customer.Email = request.Email;
            customer.UpdatedAt = DateTime.UtcNow;
            _unitOfWork.FiscalCustomers.Update(customer);
            await _unitOfWork.SaveChangesAsync();
        }

        return await RequestIndividualInvoiceAsync(order.Id, customer.Id, order.BranchId);
    }

    /// <summary>
    /// Cancels an issued invoice via Facturapi. Unlinks all orders so they can be re-invoiced.
    /// </summary>
    public async Task CancelInvoiceAsync(int invoiceId, string motive)
    {
        var invoice = await _unitOfWork.Invoices.GetByIdAsync(invoiceId)
            ?? throw new NotFoundException($"Invoice with id {invoiceId} not found.");

        if (invoice.Status == InvoiceStatus.Cancelled)
            throw new ValidationException("Invoice is already cancelled.");

        if (string.IsNullOrEmpty(invoice.FacturapiId))
            throw new ValidationException("Invoice has no Facturapi ID — cannot cancel at SAT.");

        // Call Facturapi to cancel at SAT
        await _facturapiClient.CancelInvoiceAsync(invoice.FacturapiId, motive);

        // Update invoice status
        invoice.Status = InvoiceStatus.Cancelled;
        invoice.CancellationReason = motive;
        invoice.CancelledAt = DateTime.UtcNow;
        invoice.UpdatedAt = DateTime.UtcNow;
        _unitOfWork.Invoices.Update(invoice);

        // Unlink all orders so they can be re-invoiced
        var linkedOrders = (await _unitOfWork.Orders.GetAsync(
            o => o.InvoiceId == invoiceId)).ToList();

        foreach (var order in linkedOrders)
        {
            order.InvoiceId = null;
            order.InvoiceStatus = InvoiceStatus.None;
            order.FacturapiId = null;
            order.InvoiceUrl = null;
            order.InvoicedAt = null;
            _unitOfWork.Orders.Update(order);
        }

        await _unitOfWork.SaveChangesAsync();
    }

    /// <summary>
    /// Processes a Facturapi webhook event. Updates Invoice status and PDF/XML URLs.
    /// </summary>
    public async Task ProcessWebhookAsync(string eventType, string rawJson)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(rawJson);
        var root = doc.RootElement;

        // Extract the invoice ID from the data payload
        if (!root.TryGetProperty("data", out var data)) return;
        if (!data.TryGetProperty("id", out var idProp)) return;
        var facturapiId = idProp.GetString();
        if (string.IsNullOrEmpty(facturapiId)) return;

        // Find the invoice in our DB by FacturapiId
        var invoices = await _unitOfWork.Invoices.GetAsync(i => i.FacturapiId == facturapiId);
        var invoice = invoices.FirstOrDefault();
        if (invoice == null) return;

        var status = data.TryGetProperty("status", out var s) ? s.GetString() : null;

        switch (eventType)
        {
            case "invoice.status_updated" when status == "valid":
                invoice.Status = InvoiceStatus.Issued;
                invoice.IssuedAt ??= DateTime.UtcNow;

                if (data.TryGetProperty("pdf_custom_section", out var pdf))
                    invoice.PdfUrl = pdf.GetString();
                if (data.TryGetProperty("xml", out var xml))
                    invoice.XmlUrl = xml.GetString();

                invoice.UpdatedAt = DateTime.UtcNow;
                _unitOfWork.Invoices.Update(invoice);

                // Also update linked orders
                var issuedOrders = (await _unitOfWork.Orders.GetAsync(
                    o => o.InvoiceId == invoice.Id)).ToList();
                foreach (var order in issuedOrders)
                {
                    order.InvoiceStatus = InvoiceStatus.Issued;
                    order.InvoiceUrl = invoice.PdfUrl;
                    order.InvoicedAt = invoice.IssuedAt;
                    _unitOfWork.Orders.Update(order);
                }
                break;

            case "invoice.status_updated" when status == "canceled":
            case "invoice.canceled":
                invoice.Status = InvoiceStatus.Cancelled;
                invoice.CancelledAt ??= DateTime.UtcNow;
                invoice.UpdatedAt = DateTime.UtcNow;
                _unitOfWork.Invoices.Update(invoice);

                var cancelledOrders = (await _unitOfWork.Orders.GetAsync(
                    o => o.InvoiceId == invoice.Id)).ToList();
                foreach (var order in cancelledOrders)
                {
                    order.InvoiceId = null;
                    order.InvoiceStatus = InvoiceStatus.None;
                    order.FacturapiId = null;
                    order.InvoiceUrl = null;
                    order.InvoicedAt = null;
                    _unitOfWork.Orders.Update(order);
                }
                break;
        }

        await _unitOfWork.SaveChangesAsync();
    }

    /// <summary>
    /// Gets an invoice by its internal ID.
    /// </summary>
    public async Task<InvoiceDetailResult> GetInvoiceByIdAsync(int invoiceId)
    {
        var invoice = await _unitOfWork.Invoices.GetByIdAsync(invoiceId)
            ?? throw new NotFoundException($"Invoice with id {invoiceId} not found.");

        return MapToDetailResult(invoice);
    }

    /// <summary>
    /// Gets all invoices linked to a specific order.
    /// </summary>
    public async Task<IEnumerable<InvoiceDetailResult>> GetInvoicesByOrderAsync(string orderId)
    {
        var order = (await _unitOfWork.Orders.GetAsync(o => o.Id == orderId, "Invoice"))
            .FirstOrDefault()
            ?? throw new NotFoundException($"Order {orderId} not found.");

        if (order.Invoice == null)
            return Enumerable.Empty<InvoiceDetailResult>();

        return new[] { MapToDetailResult(order.Invoice) };
    }

    /// <summary>
    /// Returns the download URL for a specific format (pdf/xml).
    /// </summary>
    public async Task<string> GetInvoiceDownloadUrlAsync(int invoiceId, string format)
    {
        var invoice = await _unitOfWork.Invoices.GetByIdAsync(invoiceId)
            ?? throw new NotFoundException($"Invoice with id {invoiceId} not found.");

        var url = format.ToLowerInvariant() switch
        {
            "pdf" => invoice.PdfUrl,
            "xml" => invoice.XmlUrl,
            _ => throw new ValidationException($"Invalid format '{format}'. Use 'pdf' or 'xml'.")
        };

        if (string.IsNullOrEmpty(url))
            throw new ValidationException($"No {format.ToUpperInvariant()} available for this invoice.");

        return url;
    }

    #endregion

    #region Private Helpers

    private static InvoiceDetailResult MapToDetailResult(Invoice invoice)
    {
        return new InvoiceDetailResult
        {
            Id = invoice.Id,
            Type = invoice.Type.ToString(),
            Status = invoice.Status.ToString(),
            FacturapiId = invoice.FacturapiId,
            Series = invoice.Series,
            FolioNumber = invoice.FolioNumber,
            TotalCents = invoice.TotalCents,
            SubtotalCents = invoice.SubtotalCents,
            TaxCents = invoice.TaxCents,
            PaymentForm = invoice.PaymentForm,
            PdfUrl = invoice.PdfUrl,
            XmlUrl = invoice.XmlUrl,
            CancellationReason = invoice.CancellationReason,
            IssuedAt = invoice.IssuedAt,
            CancelledAt = invoice.CancelledAt,
            CreatedAt = invoice.CreatedAt
        };
    }

    /// <summary>
    /// Builds Facturapi invoice line items from order items, using frozen SAT fields.
    /// </summary>
    private static List<FacturapiInvoiceItem> BuildInvoiceItems(IEnumerable<Order> orders)
    {
        return orders
            .Where(o => o.Items != null)
            .SelectMany(o => o.Items!)
            .Select(item => new FacturapiInvoiceItem
            {
                ProductCode = item.SatProductCode ?? "01010101", // Fallback: "No identificado"
                UnitCode = item.SatUnitCode ?? "H87",           // Fallback: "Pieza"
                Description = item.ProductName,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPriceCents / 100m,
                TaxRate = item.TaxRatePercent ?? DefaultTaxRate
            })
            .ToList();
    }

    #endregion
}
