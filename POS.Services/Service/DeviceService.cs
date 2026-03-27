using POS.Domain.Enums;
using POS.Domain.Exceptions;
using POS.Domain.Models;
using POS.Repository;
using POS.Services.IService;

namespace POS.Services.Service;

public class DeviceService : IDeviceService
{
    private readonly IUnitOfWork _unitOfWork;
    private static readonly Random _random = new();

    public DeviceService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    #region Public API Methods

    /// <summary>
    /// Generates a unique 6-digit activation code for device setup.
    /// Retries on collision (up to 10 attempts).
    /// </summary>
    public async Task<GenerateCodeResponse> GenerateActivationCodeAsync(
        int businessId, int branchId, string mode, int createdBy)
    {
        var validModes = new[] { "counter", "cashier", "tables", "waiter", "kitchen", "kiosk" };
        if (!validModes.Contains(mode.ToLowerInvariant()))
            throw new ValidationException("Mode must be 'counter', 'cashier', 'tables', 'waiter', 'kitchen', or 'kiosk'");

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

        if (user.Role != UserRole.Owner)
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
}
