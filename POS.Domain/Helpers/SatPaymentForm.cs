using POS.Domain.Enums;

namespace POS.Domain.Helpers;

/// <summary>
/// Maps the internal <see cref="PaymentMethod"/> enum to SAT "Forma de Pago"
/// catalog codes (CFDI 4.0). This is the canonical source the catalog seed uses
/// to populate <c>PaymentMethodCatalog.SatPaymentFormCode</c>. At invoice time the
/// code is read from the frozen <c>OrderPayment.SatPaymentFormCode</c> instead —
/// see <see cref="FromDominantSatCode"/>.
/// </summary>
public static class SatPaymentForm
{
    /// <summary>
    /// Returns the SAT payment form code for a given <see cref="PaymentMethod"/>.
    /// </summary>
    public static string FromPaymentMethod(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => "01",          // Efectivo
        PaymentMethod.Card => "04",          // Tarjeta de crédito
        PaymentMethod.Transfer => "03",      // Transferencia electrónica
        PaymentMethod.Clip => "04",          // Terminal Clip → Tarjeta
        PaymentMethod.MercadoPago => "04",   // MercadoPago QR → Tarjeta
        PaymentMethod.BankTerminal => "04",  // Terminal bancario → Tarjeta
        PaymentMethod.StoreCredit => "05",   // Monedero electrónico
        PaymentMethod.LoyaltyPoints => "05", // Monedero electrónico (puntos)
        _ => "99"                            // Otros → Por definir
    };

    /// <summary>
    /// Determines the dominant SAT payment form from a list of frozen payment
    /// codes. For mixed payments, returns the code with the highest total amount.
    /// </summary>
    public static string FromDominantSatCode(IEnumerable<(string SatPaymentFormCode, int AmountCents)> payments)
    {
        var dominant = payments
            .GroupBy(p => p.SatPaymentFormCode)
            .OrderByDescending(g => g.Sum(p => p.AmountCents))
            .FirstOrDefault();

        return dominant?.Key ?? "99";
    }
}
