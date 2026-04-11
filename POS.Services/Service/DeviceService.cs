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
    private static readonly Random _random = new();

    public DeviceService(
        IUnitOfWork unitOfWork,
        IFeatureGateService featureGate,
        IAuthService authService)
    {
        _unitOfWork = unitOfWork;
        _featureGate = featureGate;
        _authService = authService;
    }

    #region Public API Methods

    /// <summary>
    /// Generates a unique 6-digit activation code for device setup.
    /// Retries on collision (up to 10 attempts).
    /// </summary>
    public async Task<GenerateCodeResponse> GenerateActivationCodeAsync(
        int businessId, int branchId, string mode, int createdBy)
    {
        var validModes = new[] { "cashier", "tables", "kitchen", "kiosk" };
        var normalizedMode = mode.ToLowerInvariant();
        if (!validModes.Contains(normalizedMode))
            throw new ValidationException("Mode must be 'cashier', 'tables', 'kitchen', or 'kiosk'");

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
            Mode = mode.ToLowerInvariant(),
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
    /// Validates an activation code: must exist, not be used, and not be expired.
    /// Marks as used in the same operation.
    /// </summary>
    public async Task<ActivateDeviceResponse> ValidateActivationCodeAsync(string code)
    {
        var activation = await _unitOfWork.DeviceActivationCodes.GetByCodeAsync(code);

        if (activation == null)
            throw new ValidationException("Invalid activation code");

        if (activation.IsUsed)
            throw new ValidationException("Activation code has already been used");

        if (activation.ExpiresAt < DateTime.UtcNow)
            throw new ValidationException("Activation code has expired");

        activation.IsUsed = true;
        activation.UsedAt = DateTime.UtcNow;
        _unitOfWork.DeviceActivationCodes.Update(activation);
        await _unitOfWork.SaveChangesAsync();

        return new ActivateDeviceResponse
        {
            BusinessId = activation.BusinessId,
            BranchId = activation.BranchId,
            Mode = activation.Mode,
            BusinessName = activation.Business.Name,
            BranchName = activation.Branch.Name
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

        var branches = await _unitOfWork.Branches.GetAsync(
            b => b.BusinessId == user.BusinessId && b.IsActive);

        return new DeviceSetupResponse
        {
            BusinessId = user.BusinessId,
            BusinessName = business.Name,
            Branches = branches
                .OrderBy(b => b.Id)
                .Select(b => new BranchSummary { Id = b.Id, Name = b.Name })
                .ToList()
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
        var validModes = new[] { "cashier", "tables", "kitchen", "kiosk" };
        var normalizedMode = request.Mode.ToLowerInvariant();

        if (!validModes.Contains(normalizedMode))
            throw new ValidationException("Mode must be 'cashier', 'tables', 'kitchen', or 'kiosk'");

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
