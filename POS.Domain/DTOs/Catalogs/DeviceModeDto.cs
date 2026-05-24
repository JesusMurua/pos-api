namespace POS.Domain.DTOs.Catalogs;

/// <summary>
/// Read-only projection of a <c>DeviceModeCatalog</c> row.
/// Returned by <c>GET /api/Catalog/device-modes</c>.
/// </summary>
/// <param name="Id">Stable identifier.</param>
/// <param name="Code">
/// Symbolic code (e.g. <c>cashier</c>, <c>kiosk</c>, <c>tables</c>, <c>kitchen</c>,
/// <c>reception</c>, <c>mobile</c>). Used as the <c>mode</c> claim in device JWTs.
/// </param>
/// <param name="Name">Spanish public label.</param>
/// <param name="Description">Short Spanish description shown in device-mode pickers.</param>
public sealed record DeviceModeDto(
    int Id,
    string Code,
    string Name,
    string? Description);
