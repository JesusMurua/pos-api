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
    /// Generates a unique 6-digit activation code for device setup.
    /// Retries on collision (up to 10 attempts).
    /// </summary>
    public async Task<GenerateCodeResponse> GenerateActivationCodeAsync(
        int businessId, int branchId, string mode, string name, int createdBy)
    {
        var normalizedMode = mode.ToLowerInvariant();
        if (!DeviceModeCodes.IsValid(normalizedMode))
            throw new ValidationException($"Mode must be one of: {DeviceModeCodes.FormatList()}");

        var trimmedName = name?.Trim();
        if (string.IsNullOrEmpty(trimmedName))
            throw new ValidationException("Name is required");

        await EnforceDeviceModeGateAsync(businessId, normalizedMode);

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
            IsUsed = false
        };

        await _unitOfWork.DeviceActivationCodes.AddAsync(activation);
        await _unitOfWork.SaveChangesAsync();

        return new GenerateCodeResponse
        {
            Code = code,
            ExpiresAt = activation.ExpiresAt
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
        await EnforceDeviceModeGateAsync(preview.BusinessId, preview.Mode);

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

        // ── STEP 8: Atomic token mint (still inside transaction) ──────────────
        // Reuse activation.Business already loaded by the repo to skip an extra
        // round-trip to Businesses while holding the lock.
        var features = await _featureGate.GetEnabledFeaturesAsync(activation.BusinessId);
        var deviceToken = _authService.GenerateDeviceToken(device, activation.Business, features);

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

        await EnforceDeviceModeGateAsync(branch.BusinessId, normalizedMode);

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
    /// can authenticate against HTTP and SignalR without a human session.
    /// </summary>
    private async Task<string?> IssueDeviceTokenAsync(Device device, int businessId)
    {
        var business = await _unitOfWork.Business.GetByIdAsync(businessId);
        if (business == null) return null;

        var features = await _featureGate.GetEnabledFeaturesAsync(businessId);
        return _authService.GenerateDeviceToken(device, business, features);
    }

    /// <summary>
    /// Validates that the business's current plan × giros supports the requested device mode.
    /// Kiosk requires <see cref="FeatureKey.KioskMode"/>; kitchen requires either
    /// <see cref="FeatureKey.KdsBasic"/> or <see cref="FeatureKey.RealtimeKds"/>.
    /// Cashier and tables are universally available.
    /// </summary>
    private async Task EnforceDeviceModeGateAsync(int businessId, string normalizedMode)
    {
        switch (normalizedMode)
        {
            case DeviceModeCodes.Reception:
                if (!await _featureGate.IsEnabledAsync(businessId, FeatureKey.GymReception))
                {
                    var business = await _unitOfWork.Business.GetByIdAsync(businessId);
                    throw new PlanLimitExceededException(
                        "modo Recepción",
                        0,
                        PlanTypeIds.ToCode(business?.PlanTypeId ?? PlanTypeIds.Free));
                }
                break;

            case "kiosk":
                if (!await _featureGate.IsEnabledAsync(businessId, FeatureKey.KioskMode))
                {
                    var business = await _unitOfWork.Business.GetByIdAsync(businessId);
                    throw new PlanLimitExceededException(
                        "modo Kiosco",
                        0,
                        PlanTypeIds.ToCode(business?.PlanTypeId ?? PlanTypeIds.Free));
                }
                break;

            case "kitchen":
                var hasKdsBasic = await _featureGate.IsEnabledAsync(businessId, FeatureKey.KdsBasic);
                var hasRealtimeKds = await _featureGate.IsEnabledAsync(businessId, FeatureKey.RealtimeKds);
                if (!hasKdsBasic && !hasRealtimeKds)
                {
                    var business = await _unitOfWork.Business.GetByIdAsync(businessId);
                    throw new PlanLimitExceededException(
                        "modo Cocina (KDS)",
                        0,
                        PlanTypeIds.ToCode(business?.PlanTypeId ?? PlanTypeIds.Free));
                }
                break;
        }
    }

    #endregion
}
