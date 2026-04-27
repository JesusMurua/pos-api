using POS.Domain.DTOs.Device;
using POS.Domain.Models;

namespace POS.Services.IService;

public interface IDeviceService
{
    Task<GenerateCodeResponse> GenerateActivationCodeAsync(int businessId, int branchId, string mode, string name, int createdBy);
    Task<ActivateDeviceResponse> ValidateActivationCodeAsync(string code);
    Task<DeviceSetupResponse> SetupWithEmailAsync(string email, string password);
    Task<DeviceResponse> RegisterOrUpdateDeviceAsync(DeviceRegistrationRequest request);
    Task UpdateHeartbeatAsync(string uuid);
    Task<DeviceResponse?> GetByUuidAsync(string uuid);

    /// <summary>
    /// Lists devices owned by <paramref name="businessId"/>, optionally narrowed
    /// by <paramref name="branchId"/>. Returns an array of
    /// <see cref="DeviceListItemResponse"/> projections with <c>BranchName</c>
    /// included via a single SQL join.
    /// </summary>
    Task<IReadOnlyList<DeviceListItemResponse>> ListForBusinessAsync(int businessId, int? branchId);

    /// <summary>
    /// Flips <c>Device.IsActive</c> and invalidates the auth cache entry for the
    /// device. Cross-tenant ids raise <see cref="POS.Domain.Exceptions.NotFoundException"/>
    /// (opaque — no cross-tenant enumeration).
    /// </summary>
    Task<ToggleActiveResult> ToggleActiveAsync(int deviceId, int callerBusinessId);

    /// <summary>
    /// Partial update of a device's <c>Name</c> and/or <c>BranchId</c>. Fields
    /// absent from the request are left untouched. Cross-tenant device ids raise
    /// <see cref="POS.Domain.Exceptions.NotFoundException"/>; invalid branch ids
    /// (cross-tenant, inactive, or missing) raise
    /// <see cref="POS.Domain.Exceptions.ValidationException"/>. Also invalidates
    /// the auth cache entry for the affected device.
    /// </summary>
    Task<DeviceListItemResponse> UpdateDeviceAsync(
        int deviceId, int callerBusinessId, UpdateDeviceRequest request);
}

/// <summary>
/// Minimal outcome of <see cref="IDeviceService.ToggleActiveAsync"/>.
/// </summary>
public class ToggleActiveResult
{
    public int Id { get; set; }
    public bool IsActive { get; set; }
}

public class GenerateCodeResponse
{
    public string Code { get; set; } = null!;
    public DateTime ExpiresAt { get; set; }
}

public class ActivateDeviceResponse
{
    public int BusinessId { get; set; }
    public int BranchId { get; set; }
    public string Mode { get; set; } = null!;
    public string BusinessName { get; set; } = null!;
    public string BranchName { get; set; } = null!;

    /// <summary>
    /// Pre-configured device label set by the Admin at code generation. The
    /// terminal should adopt this value verbatim and skip any "name this device"
    /// prompt.
    /// </summary>
    public string Name { get; set; } = null!;
}

public class DeviceSetupResponse
{
    public int BusinessId { get; set; }
    public string BusinessName { get; set; } = null!;
    public List<BranchSummary> Branches { get; set; } = new();

    /// <summary>
    /// Tenant macro category (FK to <c>MacroCategory.Id</c>). Drives the
    /// vertical-aware mode picker on the device-setup screen so a Gym tenant
    /// no longer sees "Mesas" / "Pantalla de Cocina".
    /// </summary>
    public int PrimaryMacroCategoryId { get; set; }

    /// <summary>
    /// Whether the matrix branch is configured to run a kitchen workflow.
    /// Surfaced here so the setup UI can hide the <c>kitchen</c> mode option
    /// without an additional round-trip after branch selection.
    /// </summary>
    public bool HasKitchen { get; set; }

    /// <summary>
    /// Whether the matrix branch is configured for table service. Used by the
    /// setup UI to hide the <c>tables</c> mode option for non-table-service
    /// tenants (gyms, retail, etc.).
    /// </summary>
    public bool HasTables { get; set; }
}
