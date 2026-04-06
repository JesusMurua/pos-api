using POS.Domain.Enums;

namespace POS.Domain.Helpers;

/// <summary>
/// Maps internal PaymentMethod enum to SAT "Forma de Pago" catalog codes.
/// Required for CFDI 4.0 invoicing.
/// </summary>
public static class SatPaymentForm
{
    /// <summary>
    /// Returns the SAT payment form code for a given PaymentMethod.
    /// </summary>
    public static string FromPaymentMethod(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => "01",          // Efectivo
        PaymentMethod.Card => "04",          // Tarjeta de crédito
        PaymentMethod.Transfer => "03",      // Transferencia electrónica
        PaymentMethod.Clip => "04",          // Terminal Clip → Tarjeta
        PaymentMethod.MercadoPago => "04",   // MercadoPago QR → Tarjeta
        PaymentMethod.BankTerminal => "04",  // Terminal bancario → Tarjeta
        PaymentMethod.StoreCredit => "99",   // Por definir (crédito interno)
        PaymentMethod.LoyaltyPoints => "99", // Por definir (puntos internos)
        _ => "99"                            // Otros → Por definir
    };

    /// <summary>
    /// Determines the dominant SAT payment form from a list of payment methods.
    /// For mixed payments, returns the method with the highest total amount.
    /// </summary>
    public static string FromDominantMethod(IEnumerable<(PaymentMethod Method, int AmountCents)> payments)
    {
        var dominant = payments
            .GroupBy(p => FromPaymentMethod(p.Method))
            .OrderByDescending(g => g.Sum(p => p.AmountCents))
            .FirstOrDefault();

        return dominant?.Key ?? "99";
    }
}
