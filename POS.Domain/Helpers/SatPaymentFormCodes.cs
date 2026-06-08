namespace POS.Domain.Helpers;

/// <summary>
/// Whitelist of valid SAT CFDI 4.0 "Forma de Pago" (<c>c_FormaPago</c>) codes.
/// Hardcoded because the SAT catalog changes very rarely; admin create/edit
/// validates membership (which implies the 2-char format). A test asserts the set.
/// </summary>
public static class SatPaymentFormCodes
{
    public static readonly IReadOnlySet<string> Valid = new HashSet<string>
    {
        "01", // Efectivo
        "02", // Cheque nominativo
        "03", // Transferencia electrónica de fondos
        "04", // Tarjeta de crédito
        "05", // Monedero electrónico
        "06", // Dinero electrónico
        "08", // Vales de despensa
        "12", // Dación en pago
        "13", // Pago por subrogación
        "14", // Pago por consignación
        "15", // Condonación
        "17", // Compensación
        "23", // Novación
        "24", // Confusión
        "25", // Remisión de deuda
        "26", // Prescripción o caducidad
        "27", // A satisfacción del acreedor
        "28", // Tarjeta de débito
        "29", // Tarjeta de servicios
        "30", // Aplicación de anticipos
        "31", // Intermediario pagos
        "99"  // Por definir
    };

    public static bool IsValid(string? code) => code != null && Valid.Contains(code);
}
