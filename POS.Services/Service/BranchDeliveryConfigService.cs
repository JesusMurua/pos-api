using POS.Domain.Enums;
using POS.Domain.Exceptions;
using POS.Domain.Models;
using POS.Repository;
using POS.Services.Adapter;
using POS.Services.IService;

namespace POS.Services.Service;

public class BranchDeliveryConfigService : IBranchDeliveryConfigService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly DataProtectionHelper _dataProtection;

    public BranchDeliveryConfigService(IUnitOfWork unitOfWork, DataProtectionHelper dataProtection)
    {
        _unitOfWork = unitOfWork;
        _dataProtection = dataProtection;
    }

    #region Public API Methods

    /// <summary>
    /// Gets all delivery platform configs for a branch as DTOs.
    /// </summary>
    public async Task<IEnumerable<BranchDeliveryConfigDto>> GetByBranchAsync(int branchId, string baseUrl)
    {
        var configs = await _unitOfWork.BranchDeliveryConfigs.GetByBranchAsync(branchId);
        return configs.Select(c => MapToDto(c, branchId, baseUrl));
    }

    /// <summary>
    /// Creates or updates a platform config. Syncs Branch.HasDelivery.
    /// </summary>
    public async Task<BranchDeliveryConfigDto> UpsertAsync(
        int branchId, UpsertDeliveryConfigRequest request, string baseUrl)
    {
        if (request.Platform == OrderSource.Direct)
            throw new ValidationException("Platform cannot be Direct.");

        var existing = await _unitOfWork.BranchDeliveryConfigs
            .GetByBranchAndPlatformAsync(branchId, request.Platform);

        BranchDeliveryConfig config;

        if (existing == null)
        {
            config = new BranchDeliveryConfig
            {
                BranchId = branchId,
                Platform = request.Platform,
                IsActive = request.IsActive,
                IsPrepaidByPlatform = request.IsPrepaidByPlatform,
                StoreId = request.StoreId,
                CreatedAt = DateTime.UtcNow
            };

            if (!string.IsNullOrEmpty(request.ApiKey))
                config.ApiKeyEncrypted = _dataProtection.Encrypt(request.ApiKey);

            if (!string.IsNullOrEmpty(request.WebhookSecret))
                config.WebhookSecret = request.WebhookSecret;

            await _unitOfWork.BranchDeliveryConfigs.AddAsync(config);
        }
        else
        {
            config = existing;
            config.IsActive = request.IsActive;
            config.IsPrepaidByPlatform = request.IsPrepaidByPlatform;
            config.StoreId = request.StoreId;

            if (!string.IsNullOrEmpty(request.ApiKey))
                config.ApiKeyEncrypted = _dataProtection.Encrypt(request.ApiKey);

            if (!string.IsNullOrEmpty(request.WebhookSecret))
                config.WebhookSecret = request.WebhookSecret;

            _unitOfWork.BranchDeliveryConfigs.Update(config);
        }

        await _unitOfWork.SaveChangesAsync();
        await SyncBranchHasDeliveryAsync(branchId);

        return MapToDto(config, branchId, baseUrl);
    }

    /// <summary>
    /// Deletes a platform config. Syncs Branch.HasDelivery.
    /// </summary>
    public async Task DeleteAsync(int branchId, OrderSource platform)
    {
        var config = await _unitOfWork.BranchDeliveryConfigs
            .GetByBranchAndPlatformAsync(branchId, platform);

        if (config == null)
            throw new NotFoundException($"Delivery config for {platform} not found.");

        _unitOfWork.BranchDeliveryConfigs.Delete(config);
        await _unitOfWork.SaveChangesAsync();
        await SyncBranchHasDeliveryAsync(branchId);
    }

    #endregion

    #region Private Helper Methods

    private async Task SyncBranchHasDeliveryAsync(int branchId)
    {
        var hasAny = await _unitOfWork.BranchDeliveryConfigs.HasActiveConfigAsync(branchId);
        var branch = await _unitOfWork.Branches.GetByIdAsync(branchId);

        if (branch != null && branch.HasDelivery != hasAny)
        {
            branch.HasDelivery = hasAny;
            _unitOfWork.Branches.Update(branch);
            await _unitOfWork.SaveChangesAsync();
        }
    }

    private static BranchDeliveryConfigDto MapToDto(
        BranchDeliveryConfig config, int branchId, string baseUrl)
    {
        return new BranchDeliveryConfigDto
        {
            Id = config.Id,
            Platform = config.Platform,
            IsActive = config.IsActive,
            IsPrepaidByPlatform = config.IsPrepaidByPlatform,
            StoreId = config.StoreId,
            HasApiKey = !string.IsNullOrEmpty(config.ApiKeyEncrypted),
            HasWebhookSecret = !string.IsNullOrEmpty(config.WebhookSecret),
            WebhookUrl = $"{baseUrl}/api/delivery/webhook/{config.Platform.ToString().ToLower()}/{branchId}",
            CreatedAt = config.CreatedAt,
            UpdatedAt = config.UpdatedAt
        };
    }

    #endregion
}
