using POS.Domain.DTOs.Device;
using POS.Domain.Models;

namespace POS.Services.IService;

public interface IDeviceService
{
    /// <summary>
    /// Generates a 6-digit device activation code. When <paramref name="cashRegisterId"/>
    /// is supplied, the activation flow will auto-link the freshly-paired device
    /// to that register inside the same transaction. The register is validated
    /// to belong to <paramref name="branchId"/> and to currently be unbound; both
    /// validations run at code generation time so an admin gets immediate
    /// feedback rather than seeing a deferred 400 at activation.
    /// </summary>
    Task<GenerateCodeResponse> GenerateActivationCodeAsync(
        int businessId, int branchId, string mode, string name, int createdBy, int? cashRegisterId = null);

    /// <summary>
    /// Lists all live (non-consumed, non-expired) activation codes that belong
    /// to <paramref name="businessId"/>, optionally narrowed by
    /// <paramref name="branchId"/>. Used by the back-office Dashboard to
    /// surface what is currently consuming the device-licensing quota and to
    /// inform the operator before generating new codes.
    /// </summary>
    Task<IReadOnlyList<PendingDeviceCodeDto>> GetPendingCodesAsync(int businessId, int? branchId = null);

    /// <summary>
    /// Returns a per-mode quota snapshot so the frontend can render proactive
    /// disabled-state UI (instead of relying on a reactive 403 from
    /// <c>POST /api/Device/generate-code</c>). One entry per metered device
    /// mode is included: <c>cashier</c>, <c>tables</c>, <c>kitchen</c>,
    /// <c>kiosk</c>, <c>reception</c>. <paramref name="branchId"/> is consulted
    /// only for branch-scoped modes (Reception); business-scoped modes are
    /// counted across the entire tenant regardless of <paramref name="branchId"/>.
    /// Reuses the same primitive used by <c>EnforceDeviceLimitsAsync</c> so the
    /// numbers are guaranteed to match the enforcer at the next call site.
    /// </summary>
    Task<DeviceLimitsDto> GetDeviceLimitsAsync(int businessId, int branchId);

    /// <summary>
    /// Atomic device pairing entry-point. Validates the 6-digit activation code,
    /// upserts the <c>Device</c> by <paramref name="deviceUuid"/>, marks the code
    /// as consumed, and issues a long-lived <c>DeviceToken</c> — all inside a
    /// single transaction with pessimistic row-level locking on the activation
    /// row. Replaces the previous two-step <c>activate</c> + <c>register</c>
    /// flow that left anonymous terminals unable to authenticate against
    /// <c>/api/devices/register</c>.
    /// </summary>
    Task<ActivateDeviceResponse> ActivateAndRegisterDeviceAsync(string code, string deviceUuid);
    Task<DeviceSetupResponse> SetupWithEmailAsync(string email, string password);
    Task<DeviceResponse> RegisterOrUpdateDeviceAsync(DeviceRegistrationRequest request);

    /// <summary>
    /// Lookup-or-insert flavor of <see cref="RegisterOrUpdateDeviceAsync"/>
    /// for callers already inside an outer transaction (notably the
    /// <c>CashierSessionService.InitializeAsync</c> orchestration). Enforces
    /// the per-plan device-limit gate the same way the public path does, but:
    /// <list type="bullet">
    ///   <item><description>Does NOT emit a <c>DeviceToken</c> — the Owner
    ///   keeps using their own JWT, the extra JWT signing on the critical
    ///   path of POS startup is pure waste.</description></item>
    ///   <item><description>Does NOT call <c>SaveChangesAsync</c> — the
    ///   orchestrator owns the transaction commit so that a downstream
    ///   register-link failure rolls the device row back together with
    ///   the rest of the work.</description></item>
    /// </list>
    /// Returns the tracked <see cref="Device"/> entity so the caller can
    /// reach <c>device.Id</c> without remapping a DTO.
    /// </summary>
    Task<Device> EnsureRegisteredAsync(string deviceUuid, int branchId, string mode, string name);
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

    /// <summary>Pre-configured device label set by the Admin at code generation.</summary>
    public string Name { get; set; } = null!;

    /// <summary>Normalized device mode (cashier, tables, kitchen, kiosk, reception).</summary>
    public string Mode { get; set; } = null!;

    /// <summary>Branch the device will be paired into. Echoed back so the UI can render context without a second query.</summary>
    public string BranchName { get; set; } = null!;

    /// <summary>UTC timestamp the code was issued. Frontend uses this together with <see cref="ExpiresAt"/> to render a countdown.</summary>
    public DateTime CreatedAt { get; set; }
}

public class ActivateDeviceResponse
{
    /// <summary>Database identity of the registered device. Populated by EF on insert.</summary>
    public int Id { get; set; }

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

    /// <summary>
    /// Long-lived JWT issued for this device on activation. Persisted client-side
    /// (IndexedDB) and used to authenticate every subsequent HTTP and SignalR
    /// call without requiring a human session.
    /// </summary>
    public string DeviceToken { get; set; } = null!;
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
