using POS.Domain.Enums;
using POS.Domain.Exceptions;
using POS.Repository;
using POS.Services.IService;

namespace POS.Services.Service;

/// <summary>
/// Handles CFDI electronic invoice creation via Facturapi.
/// NOTE: Actual HTTP calls to Facturapi are mocked — this phase focuses on
/// updating Order.InvoiceStatus and persisting state in the database.
/// </summary>
public class InvoicingService : IInvoicingService
{
    private readonly IUnitOfWork _unitOfWork;

    public InvoicingService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    #region Public API Methods

    /// <summary>
    /// Creates a global invoice consolidating uninvoiced, paid, non-cancelled orders
    /// for a branch within the given date range.
    /// </summary>
    public async Task<GlobalInvoiceResult> CreateGlobalInvoiceAsync(
        DateTime startDate, DateTime endDate, int branchId)
    {
        // Fetch uninvoiced, paid, non-cancelled orders in the date range
        var orders = (await _unitOfWork.Orders.GetAsync(
            o => o.BranchId == branchId
                && o.CreatedAt >= startDate
                && o.CreatedAt <= endDate
                && o.InvoiceStatus == InvoiceStatus.None
                && o.IsPaid
                && o.CancellationReason == null))
            .ToList();

        if (orders.Count == 0)
            throw new ValidationException("No uninvoiced orders found for the specified period.");

        // TODO: Call Facturapi API to create global invoice
        // For now, generate a mock Facturapi ID
        var mockFacturapiId = $"fpi_global_{Guid.NewGuid():N}"[..30];

        var totalCents = orders.Sum(o => o.TotalCents);

        // Update all orders with the invoice reference
        foreach (var order in orders)
        {
            order.InvoiceStatus = InvoiceStatus.Pending;
            order.FacturapiId = mockFacturapiId;
            _unitOfWork.Orders.Update(order);
        }

        await _unitOfWork.SaveChangesAsync();

        return new GlobalInvoiceResult
        {
            OrderCount = orders.Count,
            TotalCents = totalCents,
            FacturapiId = mockFacturapiId,
            Status = InvoiceStatus.Pending.ToString()
        };
    }

    /// <summary>
    /// Requests an individual invoice for a specific order linked to a fiscal customer.
    /// </summary>
    public async Task<IndividualInvoiceResult> RequestIndividualInvoiceAsync(
        string orderId, int fiscalCustomerId, int branchId)
    {
        var results = await _unitOfWork.Orders.GetAsync(o => o.Id == orderId);
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

        // TODO: Call Facturapi API to create individual invoice
        // For now, generate a mock Facturapi ID
        var mockFacturapiId = $"fpi_ind_{Guid.NewGuid():N}"[..30];

        order.InvoiceStatus = InvoiceStatus.Pending;
        order.FacturapiId = mockFacturapiId;
        order.FiscalCustomerId = fiscalCustomerId;

        _unitOfWork.Orders.Update(order);
        await _unitOfWork.SaveChangesAsync();

        return new IndividualInvoiceResult
        {
            OrderId = order.Id,
            CustomerRfc = customer.Rfc,
            TotalCents = order.TotalCents,
            FacturapiId = mockFacturapiId,
            Status = InvoiceStatus.Pending.ToString()
        };
    }

    /// <summary>
    /// Gets public order details for the self-invoicing portal.
    /// Validates receipt proof via totalCents match.
    /// </summary>
    public async Task<PublicOrderInvoiceInfo> GetPublicOrderDetailsAsync(string orderId, int totalCents)
    {
        var results = await _unitOfWork.Orders.GetAsync(
            o => o.Id == orderId, "Branch,Branch.Business");
        var order = results.FirstOrDefault()
            ?? throw new NotFoundException("Order not found");

        // Receipt proof: generic message to prevent oracle attacks
        if (order.TotalCents != totalCents)
            throw new UnauthorizedException("The provided order data does not match our records.");

        var business = order.Branch?.Business;
        var invoicingEnabled = business?.InvoicingEnabled ?? false;

        var canInvoice = invoicingEnabled
            && order.InvoiceStatus == InvoiceStatus.None
            && order.CancellationReason == null
            && order.CreatedAt > DateTime.UtcNow.AddDays(-40);

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
            InvoiceUrl = order.InvoiceStatus == InvoiceStatus.Issued ? order.InvoiceUrl : null
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

        // Receipt proof
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
            customer = new Domain.Models.FiscalCustomer
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
        else
        {
            // Update email if provided and different
            if (!string.IsNullOrEmpty(request.Email) && customer.Email != request.Email)
            {
                customer.Email = request.Email;
                customer.UpdatedAt = DateTime.UtcNow;
                _unitOfWork.FiscalCustomers.Update(customer);
                await _unitOfWork.SaveChangesAsync();
            }
        }

        // Issue the invoice (reuse internal logic)
        return await RequestIndividualInvoiceAsync(order.Id, customer.Id, order.BranchId);
    }

    #endregion
}
