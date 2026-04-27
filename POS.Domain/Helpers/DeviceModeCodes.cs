namespace POS.Domain.Helpers;

/// <summary>
/// Single source of truth for the string codes accepted by the device pairing
/// and registration endpoints. The values mirror <c>DeviceModeCatalog.Code</c>
/// rows so any client-facing validation message stays in sync with the
/// catalog table.
/// </summary>
public static class DeviceModeCodes
{
    public const string Cashier = "cashier";
    public const string Tables = "tables";
    public const string Kitchen = "kitchen";
    public const string Kiosk = "kiosk";
    public const string Reception = "reception";

    /// <summary>
    /// All accepted device mode codes. Iteration order is the canonical order
    /// used by <see cref="FormatList"/> when building error messages.
    /// </summary>
    public static readonly string[] All =
    {
        Cashier,
        Tables,
        Kitchen,
        Kiosk,
        Reception
    };

    /// <summary>
    /// Returns <c>true</c> when <paramref name="mode"/> matches one of the
    /// known codes (case-insensitive). Callers that already lowercase their
    /// input keep working unchanged.
    /// </summary>
    public static bool IsValid(string mode) =>
        All.Contains(mode, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Renders the list of valid codes as quoted, comma-separated tokens for
    /// inclusion in <see cref="POS.Domain.Exceptions.ValidationException"/>
    /// messages so the error text never drifts from the actual catalog.
    /// </summary>
    public static string FormatList() =>
        string.Join(", ", All.Select(m => $"'{m}'"));
}
