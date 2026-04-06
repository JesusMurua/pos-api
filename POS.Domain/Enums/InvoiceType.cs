namespace POS.Domain.Enums;

/// <summary>
/// Type of CFDI invoice.
/// </summary>
public enum InvoiceType
{
    /// <summary>Individual invoice for a specific customer (RFC receptor).</summary>
    Individual,
    /// <summary>Global invoice consolidating multiple sales to the general public (RFC genérico XAXX010101000).</summary>
    Global
}
