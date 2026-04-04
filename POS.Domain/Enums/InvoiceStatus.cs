namespace POS.Domain.Enums;

/// <summary>
/// CFDI invoice status for an order.
/// </summary>
public enum InvoiceStatus
{
    /// <summary>Order has not been invoiced.</summary>
    None,
    /// <summary>Invoice created, waiting for SAT stamping (timbrado).</summary>
    Pending,
    /// <summary>Invoice stamped and valid (CFDI timbrado).</summary>
    Issued,
    /// <summary>Invoice cancelled at SAT.</summary>
    Cancelled
}
