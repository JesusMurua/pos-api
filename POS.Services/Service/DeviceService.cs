using POS.Domain.DTOs.Device;
using POS.Domain.Enums;
using POS.Domain.Exceptions;
using POS.Domain.Helpers;
using POS.Domain.Models;
using POS.Repository;
using POS.Services.IService;

namespace POS.Services.Service;

public class DeviceService : IDeviceService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IFeatureGateService _featureGate;
    private readonly IAuthService _authService;
    private readonly IDeviceAuthorizationService _deviceAuth;
    private static readonly Random _random = new();

    public DeviceService(
        IUnitOfWork unitOfWork,
        IFeatureGateService featureGate,
        IAuthService authService,
        IDeviceAuthorizationService deviceAuth)
    {
        _unitOfWork = unitOfWork;
        _featureGate = featureGate;
        _authService = authService;
        _deviceAuth = deviceAuth;
    }

    #region Public API Methods

    /// <summary>
    /// Generates a unique 6-digit activation code for device setup. The full
    /// flow runs inside a single transaction so hygiene + enforcement + insert
    /// are atomic: if any step fails, no half-cancelled prior codes survive.
    /// </summary>
    /// <remarks>
    /// Order is load-bearing: hygiene runs BEFORE the enforcement count so
    /// the codes we are about to invalidate do not inflate the usage figure
    /// and trigger a false-positive 403. See
    /// <c>docs/monetization-architecture.md</c> §3 for the policy rationale.
    /// </remarks>
    public async Task<GenerateCodeResponse> GenerateActivationCodeAsync(
        int businessId, int branchId, string mode, string name, int createdBy, int? cashRegisterId = null)
    {
        var normalizedMode = mode.ToLowerInvariant();
        if (!DeviceModeCodes.IsValid(normalizedMode))
            throw new ValidationException($"Mode must be one of: {DeviceModeCodes.FormatList()}");

        var trimmedName = name?.Trim();
        if (string.IsNullOrEmpty(trimmedName))
            throw new ValidationException("Name is required");

        // ── CROSS-TENANT GUARD (IDOR fix): an Owner of Tenant A must not be
        // able to generate codes against Tenant B by submitting a foreign
        // BranchId. We fetch the branch here to (a) prove existence + tenancy
        // and (b) reuse it to populate BranchName in the response without a
        // second round-trip.
        var branch = await _unitOfWork.Branches.GetByIdAsync(branchId)
            ?? throw new NotFoundException($"Branch with id {branchId} not found");

        if (branch.BusinessId != businessId)
            throw new UnauthorizedException("Branch does not belong to the caller's business");

        // ── AUTO-LINK PRE-FLIGHT: when a CashRegisterId is supplied, validate
        // it up-front so the admin gets immediate feedback instead of an opaque
        // FK violation at activation time. We enforce both cross-tenant
        // (BranchId match) and the unique-partial-index precondition
        // (DeviceId == null) here; the activation flow can then assume the
        // pre-assignment is still valid (race-protected by re-read on activate).
        if (cashRegisterId.HasValue)
        {
            var register = await _unitOfWork.CashRegisters.GetByIdAsync(cashRegisterId.Value)
                ?? throw new NotFoundException($"Cash register with id {cashRegisterId.Value} not found");

            if (register.BranchId != branchId)
                throw new ValidationException("Cash register does not belong to this branch");

            if (register.DeviceId.HasValue)
                throw new ValidationException("Cash register is already linked to a device. Unlink it first.");
        }

        await using var transaction = await _unitOfWork.BeginTransactionAsync();

        // ── HYGIENE: invalidate any prior pending code for the same target ─
        var stalePending = await _unitOfWork.DeviceActivationCodes
            .GetPendingByTargetAsync(branchId, normalizedMode, trimmedName);

        if (stalePending.Count > 0)
        {
            var now = DateTime.UtcNow;
            foreach (var stale in stalePending)
            {
                stale.IsUsed = true;
                stale.UsedAt = now;
                _unitOfWork.DeviceActivationCodes.Update(stale);
            }
            await _unitOfWork.SaveChangesAsync();
        }

        // ── ENFORCE: data-driven count using EnforcementScope ───────────────
        await EnforceDeviceLimitsAsync(businessId, branchId, normalizedMode);

        // ── GENERATE: collision-safe loop ──────────────────────────────────
        string code;
        var attempts = 0;

        do
        {
            code = _random.Next(100000, 999999).ToString();
            attempts++;

            if (attempts > 10)
                throw new ValidationException("Unable to generate unique code. Please try again.");

        } while (await _unitOfWork.DeviceActivationCodes.CodeExistsAsync(code));

        var activation = new DeviceActivationCode
        {
            Code = code,
            BusinessId = businessId,
            BranchId = branchId,
            Mode = normalizedMode,
            Name = trimmedName,
            CreatedBy = createdBy,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(24),
            IsUsed = false,
            CashRegisterId = cashRegisterId
        };

        await _unitOfWork.DeviceActivationCodes.AddAsync(activation);
        await _unitOfWork.SaveChangesAsync();

        await transaction.CommitAsync();

        return new GenerateCodeResponse
        {
            Code = code,
            ExpiresAt = activation.ExpiresAt,
            Name = activation.Name,
            Mode = activation.Mode,
            BranchName = branch.Name,
            CreatedAt = activation.CreatedAt
        };
    }

    /// <summary>
    /// Atomic device pairing: validates the activation code, upserts the
    /// <see cref="Device"/> row (idempotent by <paramref name="deviceUuid"/>),
    /// consumes the code, and issues the <c>DeviceToken</c> — all inside one
    /// transaction with pessimistic row-level locking on the activation row.
    /// </summary>
    /// <remarks>
    /// Replaces the previous two-step <c>activate</c> + <c>register</c> flow
    /// where the second hop required Owner/Manager auth that the anonymous
    /// terminal could not provide. The 6-digit code is now the sole credential
    /// that bootstraps a fresh terminal.
    /// </remarks>
    public async Task<ActivateDeviceResponse> ActivateAndRegisterDeviceAsync(string code, string deviceUuid)
    {
        // ── STEP 1: Fail-fast pre-validation (no lock) ────────────────────────
        // Reject obviously bad codes without opening a transaction so brute-force
        // attempts don't pile up FOR UPDATE locks against the table.
        var preview = await _unitOfWork.DeviceActivationCodes.GetByCodeAsync(code);

        if (preview == null)
            throw new ValidationException("Invalid activation code");

        if (preview.IsUsed)
            throw new ValidationException("Activation code has already been used");

        if (preview.ExpiresAt < DateTime.UtcNow)
            throw new ValidationException("Activation code has expired");

        // Re-validate plan × mode here. The plan / giro could have been downgraded
        // between generate-code and activate (window up to 24h via ExpiresAt).
        // The code being activated is itself in the pending-codes count, so we
        // signal isConsumingPendingCode=true to subtract it before comparing —
        // otherwise a tenant exactly at usage==limit (with one pending code that
        // is its own consumer) would receive a false-positive 403 even though
        // the post-activation total stays identical (–1 pending, +1 active).
        await EnforceDeviceLimitsAsync(
            preview.BusinessId,
            preview.BranchId,
            preview.Mode,
            isConsumingPendingCode: true);

        // ── STEP 2: Open transaction ──────────────────────────────────────────
        await using var transaction = await _unitOfWork.BeginTransactionAsync();

        // ── STEP 3: Lock & Hydrate ────────────────────────────────────────────
        // FOR UPDATE serializes concurrent callers on the same code row.
        var activation = await _unitOfWork.DeviceActivationCodes.GetByCodeForUpdateAsync(code);

        if (activation == null)
            throw new ValidationException("Invalid activation code");

        // ── STEP 4: Double-check post-lock ────────────────────────────────────
        // Another transaction may have consumed this code between fail-fast and
        // lock acquisition. The lock guarantees we now see the latest committed
        // state.
        if (activation.IsUsed)
            throw new ValidationException("Activation code has already been used");

        if (activation.ExpiresAt < DateTime.UtcNow)
            throw new ValidationException("Activation code has expired");

        // ── STEP 5: Idempotent upsert by DeviceUuid ───────────────────────────
        var existingDevice = await _unitOfWork.Devices.GetByDeviceUuidAsync(deviceUuid);

        Device device;
        if (existingDevice != null)
        {
            existingDevice.BranchId = activation.BranchId;
            existingDevice.Mode = activation.Mode;
            existingDevice.Name = activation.Name;
            existingDevice.IsActive = true;
            _unitOfWork.Devices.Update(existingDevice);
            device = existingDevice;
        }
        else
        {
            device = new Device
            {
                BranchId = activation.BranchId,
                DeviceUuid = deviceUuid,
                Mode = activation.Mode,
                Name = activation.Name,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            await _unitOfWork.Devices.AddAsync(device);
        }

        // ── STEP 6: Consume the code ──────────────────────────────────────────
        activation.IsUsed = true;
        activation.UsedAt = DateTime.UtcNow;
        _unitOfWork.DeviceActivationCodes.Update(activation);

        // ── STEP 7: Flush — populates device.Id via INSERT ... RETURNING ──────
        // Changes are persisted inside the transaction but NOT committed yet.
        // If anything below throws, the transaction.DisposeAsync rolls back.
        await _unitOfWork.SaveChangesAsync();

        // ── STEP 7b: Auto-link the device to the pre-assigned CashRegister ────
        // Runs INSIDE the same transaction — if the link fails (e.g. the register
        // got linked to another device between code generation and activation),
        // the entire activation is rolled back. The admin sees a clean 400, the
        // tenant is not left with an orphaned device row.
        if (activation.CashRegisterId.HasValue)
        {
            var register = await _unitOfWork.CashRegisters.GetByIdAsync(activation.CashRegisterId.Value);

            if (register == null || register.BranchId != activation.BranchId)
                throw new ValidationException(
                    "Cash register pre-assigned at code generation is no longer valid for this branch.");

            if (register.DeviceId.HasValue && register.DeviceId.Value != device.Id)
                throw new ValidationException(
                    "Cash register was linked to another device after the code was generated. " +
                    "Generate a new code or pick a different register.");

            register.DeviceId = device.Id;
            _unitOfWork.CashRegisters.Update(register);
            await _unitOfWork.SaveChangesAsync();
        }

        // ── STEP 8: Atomic token mint (still inside transaction) ──────────────
        // Reuse activation.Business already loaded by the repo to skip an extra
        // round-trip to Businesses while holding the lock. macroCode is resolved
        // explicitly via the catalog repo because activation.Business is loaded
        // without its PrimaryMacroCategory navigation property — relying on the
        // nav would silently emit an empty claim (PR 3 bug fix).
        var features = await _featureGate.GetEnabledFeaturesAsync(activation.BusinessId);
        var macroCode = await ResolveMacroCodeAsync(activation.Business.PrimaryMacroCategoryId);
        var deviceToken = _authService.GenerateDeviceToken(device, activation.Business, macroCode, features);

        // ── STEP 9: Commit ────────────────────────────────────────────────────
        await transaction.CommitAsync();

        // Re-pair flips IsActive to true; the cached value may still be false.
        if (existingDevice != null)
            _deviceAuth.Invalidate(device.Id);

        return new ActivateDeviceResponse
        {
            Id = device.Id,
            BusinessId = activation.BusinessId,
            BranchId = activation.BranchId,
            Mode = activation.Mode,
            BusinessName = activation.Business.Name,
            BranchName = activation.Branch.Name,
            Name = activation.Name,
            DeviceToken = deviceToken
        };
    }

    /// <summary>
    /// Validates Owner credentials for device setup flow.
    /// Only Owner role is allowed.
    /// </summary>
    public async Task<DeviceSetupResponse> SetupWithEmailAsync(string email, string password)
    {
        var user = await _unitOfWork.Users.GetByEmailAsync(email);

        if (user == null || string.IsNullOrEmpty(user.PasswordHash))
            throw new ValidationException("Invalid email or password");

        if (user.RoleId != UserRoleIds.Owner)
            throw new ValidationException("Only Owner accounts can set up devices");

        if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
            throw new ValidationException("Invalid email or password");

        var business = await _unitOfWork.Business.GetByIdAsync(user.BusinessId);
        if (business == null)
            throw new NotFoundException($"Business with id {user.BusinessId} not found");

        var branches = (await _unitOfWork.Branches.GetAsync(
            b => b.BusinessId == user.BusinessId && b.IsActive)).ToList();

        // Pick the matrix branch as the canonical source for the kitchen/tables
        // flags exposed on the response. Falls back to the first active branch
        // by id when no matrix is flagged (legacy tenants).
        var primaryBranch = branches.FirstOrDefault(b => b.IsMatrix)
            ?? branches.OrderBy(b => b.Id).FirstOrDefault();

        return new DeviceSetupResponse
        {
            BusinessId = user.BusinessId,
            BusinessName = business.Name,
            Branches = branches
                .OrderBy(b => b.Id)
                .Select(b => new BranchSummary { Id = b.Id, Name = b.Name })
                .ToList(),
            PrimaryMacroCategoryId = business.PrimaryMacroCategoryId,
            HasKitchen = primaryBranch?.HasKitchen ?? false,
            HasTables = primaryBranch?.HasTables ?? false
        };
    }

    #endregion

    #region Device Registration Methods

    /// <summary>
    /// Registers a new device or updates an existing one by DeviceUuid.
    /// If the device already exists, updates BranchId, Mode, Name, and reactivates it.
    /// </summary>
    public async Task<DeviceResponse> RegisterOrUpdateDeviceAsync(DeviceRegistrationRequest request)
    {
        var normalizedMode = request.Mode.ToLowerInvariant();

        if (!DeviceModeCodes.IsValid(normalizedMode))
            throw new ValidationException($"Mode must be one of: {DeviceModeCodes.FormatList()}");

        var branch = await _unitOfWork.Branches.GetByIdAsync(request.BranchId)
            ?? throw new NotFoundException($"Branch with id {request.BranchId} not found");

        await EnforceDeviceLimitsAsync(branch.BusinessId, request.BranchId, normalizedMode);

        var existing = await _unitOfWork.Devices.GetByDeviceUuidAsync(request.DeviceUuid);

        Device device;
        if (existing != null)
        {
            existing.BranchId = request.BranchId;
            existing.Mode = normalizedMode;
            existing.Name = request.Name;
            existing.IsActive = true;
            _unitOfWork.Devices.Update(existing);
            await _unitOfWork.SaveChangesAsync();
            device = existing;
        }
        else
        {
            device = new Device
            {
                BranchId = request.BranchId,
                DeviceUuid = request.DeviceUuid,
                Mode = normalizedMode,
                Name = request.Name,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            await _unitOfWork.Devices.AddAsync(device);
            await _unitOfWork.SaveChangesAsync();
        }

        var deviceToken = await IssueDeviceTokenAsync(device, branch.BusinessId);
        return MapToResponse(device, deviceToken);
    }

    /// <summary>
    /// Updates the LastSeenAt timestamp for a device heartbeat.
    /// </summary>
    public async Task UpdateHeartbeatAsync(string uuid)
    {
        var device = await _unitOfWork.Devices.GetByDeviceUuidAsync(uuid);
        if (device == null)
            throw new NotFoundException($"Device with UUID '{uuid}' not found");

        device.LastSeenAt = DateTime.UtcNow;
        _unitOfWork.Devices.Update(device);
        await _unitOfWork.SaveChangesAsync();
    }

    /// <summary>
    /// Returns the current configuration for a device by UUID.
    /// </summary>
    public async Task<DeviceResponse?> GetByUuidAsync(string uuid)
    {
        var device = await _unitOfWork.Devices.GetByDeviceUuidAsync(uuid);
        return device == null ? null : MapToResponse(device);
    }

    #endregion

    #region Back Office Management

    /// <inheritdoc />
    public async Task<IReadOnlyList<DeviceListItemResponse>> ListForBusinessAsync(int businessId, int? branchId)
    {
        return await _unitOfWork.Devices.ListProjectedAsync(businessId, branchId);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PendingDeviceCodeDto>> GetPendingCodesAsync(int businessId, int? branchId = null)
    {
        var rows = await _unitOfWork.DeviceActivationCodes
            .ListPendingByBusinessAsync(businessId, branchId);

        return rows
            .Select(c => new PendingDeviceCodeDto
            {
                Code = c.Code,
                Name = c.Name,
                Mode = c.Mode,
                BranchId = c.BranchId,
                BranchName = c.Branch.Name,
                CreatedAt = c.CreatedAt,
                ExpiresAt = c.ExpiresAt
            })
            .ToList();
    }

    /// <inheritdoc />
    public async Task<ToggleActiveResult> ToggleActiveAsync(int deviceId, int callerBusinessId)
    {
        // Opaque 404 on cross-tenant or missing — never 403, to avoid
        // cross-tenant id enumeration via status-code differentiation.
        var device = await _unitOfWork.Devices.GetForTenantAsync(deviceId, callerBusinessId)
            ?? throw new NotFoundException("Device not found");

        device.IsActive = !device.IsActive;
        _unitOfWork.Devices.Update(device);
        await _unitOfWork.SaveChangesAsync();

        _deviceAuth.Invalidate(deviceId);

        return new ToggleActiveResult { Id = device.Id, IsActive = device.IsActive };
    }

    /// <inheritdoc />
    public async Task<DeviceListItemResponse> UpdateDeviceAsync(
        int deviceId, int callerBusinessId, UpdateDeviceRequest request)
    {
        if (request.Name is null && request.BranchId is null)
            throw new ValidationException("At least one of name or branchId must be provided");

        var device = await _unitOfWork.Devices.GetForTenantAsync(deviceId, callerBusinessId)
            ?? throw new NotFoundException("Device not found");

        if (request.Name is not null)
        {
            var trimmed = request.Name.Trim();
            if (string.IsNullOrEmpty(trimmed))
                throw new ValidationException("Name cannot be blank");
            device.Name = trimmed;
        }

        if (request.BranchId is not null && request.BranchId.Value != device.BranchId)
        {
            var targetBranch = await _unitOfWork.Branches.GetByIdAsync(request.BranchId.Value);
            if (targetBranch is null
                || targetBranch.BusinessId != callerBusinessId
                || !targetBranch.IsActive)
            {
                throw new ValidationException("BranchId is not a valid active branch in this business");
            }
            device.BranchId = request.BranchId.Value;
        }

        _unitOfWork.Devices.Update(device);
        await _unitOfWork.SaveChangesAsync();

        // Invalidate cache even though only IsActive is cached today — keeps the
        // invariant "every admin mutation flushes the device's cache entry" so
        // future cache schema extensions stay safe.
        _deviceAuth.Invalidate(deviceId);

        var projected = await _unitOfWork.Devices.GetProjectedByIdAsync(deviceId);
        return projected!; // Just saved — guaranteed to exist.
    }

    #endregion

    #region Private Helpers

    private static DeviceResponse MapToResponse(Device device, string? deviceToken = null)
    {
        return new DeviceResponse
        {
            Id = device.Id,
            DeviceUuid = device.DeviceUuid,
            Mode = device.Mode,
            Name = device.Name,
            IsActive = device.IsActive,
            BranchId = device.BranchId,
            LastSeenAt = device.LastSeenAt,
            CreatedAt = device.CreatedAt,
            DeviceToken = deviceToken
        };
    }

    /// <summary>
    /// Resolves the business and the enabled feature set and returns a long-lived
    /// JWT representing the device. Called from registration flows so the device
    /// can authenticate against HTTP and SignalR without a human session. The
    /// <c>macroCode</c> is resolved explicitly via the catalog repo so the JWT's
    /// <c>macroCategory</c> claim is never empty (PR 3 bug fix).
    /// </summary>
    private async Task<string?> IssueDeviceTokenAsync(Device device, int businessId)
    {
        var business = await _unitOfWork.Business.GetByIdAsync(businessId);
        if (business == null) return null;

        var features = await _featureGate.GetEnabledFeaturesAsync(businessId);
        var macroCode = await ResolveMacroCodeAsync(business.PrimaryMacroCategoryId);
        return _authService.GenerateDeviceToken(device, business, macroCode, features);
    }

    /// <summary>
    /// Resolves a <c>MacroCategory.InternalCode</c> by id. Returns
    /// <see cref="string.Empty"/> only when the catalog row is missing, which
    /// signals a corrupted seed — never a routine null-nav-property situation.
    /// </summary>
    private async Task<string> ResolveMacroCodeAsync(int primaryMacroCategoryId)
    {
        var macros = await _unitOfWork.Catalog.GetMacroCategoriesAsync();
        return macros.FirstOrDefault(m => m.Id == primaryMacroCategoryId)?.InternalCode ?? string.Empty;
    }

    /// <summary>
    /// Maps a device mode string to the quantitative <see cref="FeatureKey"/>
    /// that gates and meters it. <c>cashier</c> and <c>tables</c> share the
    /// <see cref="FeatureKey.MaxCashRegisters"/> quota — both are fixed
    /// floor-terminals counted as a single hardware class. Returns <c>null</c>
    /// for modes that are not metered (none today).
    /// </summary>
    private static FeatureKey? GetFeatureKeyForMode(string normalizedMode) => normalizedMode switch
    {
        DeviceModeCodes.Cashier   => FeatureKey.MaxCashRegisters,
        DeviceModeCodes.Tables    => FeatureKey.MaxCashRegisters,
        DeviceModeCodes.Kitchen   => FeatureKey.MaxKdsScreens,
        DeviceModeCodes.Kiosk     => FeatureKey.MaxKiosks,
        DeviceModeCodes.Reception => FeatureKey.MaxReceptionsPerBranch,
        _                         => null
    };

    /// <summary>
    /// Data-driven device-licensing enforcement. Resolves the quantitative
    /// feature for <paramref name="normalizedMode"/>, reads its limit and
    /// scope from the <see cref="IFeatureGateService"/> snapshot, and
    /// rejects the call when the active hardware count plus pending
    /// activation codes already meets or exceeds the limit. <c>null</c>
    /// limit means infinite (Enterprise) and bypasses the check.
    /// </summary>
    /// <param name="isConsumingPendingCode">
    /// When <c>true</c>, the caller is in the middle of consuming a pending
    /// activation code (i.e. the activate flow). The pending code is by
    /// definition included in the <c>pendingCodes</c> count, so this flag
    /// instructs the method to subtract one from the figure before comparing
    /// against the limit. Without this adjustment a tenant at exactly
    /// <c>usage == limit</c> (with one pending code being its own consumer)
    /// would receive a false-positive 403 even though the post-activation
    /// total is unchanged. Generate-code and back-office register flows must
    /// keep the default <c>false</c>.
    /// </param>
    /// <remarks>
    /// Usage formula (see <c>docs/monetization-architecture.md</c> §2):
    /// <c>Usage = COUNT(active devices in mode) + COUNT(pending codes in mode)</c>,
    /// scope-filtered by <c>BranchId</c> when the feature is
    /// <see cref="EnforcementScope.Branch"/>.
    /// </remarks>
    private async Task EnforceDeviceLimitsAsync(int businessId, int branchId, string normalizedMode, bool isConsumingPendingCode = false)
    {
        var feature = GetFeatureKeyForMode(normalizedMode);
        if (feature is null) return;

        var (limit, scope) = await _featureGate.GetEnforcementInfoAsync(businessId, feature.Value);

        // null limit = infinite (e.g. Enterprise plan).
        if (limit is null) return;

        // Branch-scoped features count only within the requesting branch;
        // global features count the entire tenant.
        int? scopeBranchId = scope == EnforcementScope.Branch ? branchId : null;

        var activeDevices = await _unitOfWork.Devices
            .CountActiveByModeAsync(businessId, scopeBranchId, normalizedMode);
        var pendingCodes = await _unitOfWork.DeviceActivationCodes
            .CountPendingByModeAsync(businessId, scopeBranchId, normalizedMode);

        // Activate flow: subtract the code we are about to consume so the
        // pre-state count does not over-report by one (the code is in the
        // pending table until the transaction commits its IsUsed flip).
        if (isConsumingPendingCode && pendingCodes > 0)
            pendingCodes--;

        var usage = activeDevices + pendingCodes;

        // Sum purchased Stripe Add-on licenses for this feature. Fail-strict:
        // only active/trialing subscriptions contribute — a past_due tenant
        // loses the add-on capacity until payment recovers (see XML doc on
        // ISubscriptionItemRepository.SumAddonQuantityByFeatureAsync).
        var addonLimit = await _unitOfWork.SubscriptionItems
            .SumAddonQuantityByFeatureAsync(businessId, feature.Value);

        var effectiveLimit = limit.Value + addonLimit;

        if (usage >= effectiveLimit)
        {
            var business = await _unitOfWork.Business.GetByIdAsync(businessId);
            throw new PlanLimitExceededException(
                resource: $"modo {normalizedMode}",
                limit: effectiveLimit,
                currentPlan: PlanTypeIds.ToCode(business?.PlanTypeId ?? PlanTypeIds.Free));
        }
    }

    #endregion
}
