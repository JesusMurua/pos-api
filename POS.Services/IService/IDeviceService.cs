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
}
