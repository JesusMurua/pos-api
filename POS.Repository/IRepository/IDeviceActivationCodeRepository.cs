using POS.Domain.Models;

namespace POS.Repository.IRepository;

public interface IDeviceActivationCodeRepository : IGenericRepository<DeviceActivationCode>
{
    Task<DeviceActivationCode?> GetByCodeAsync(string code);
    Task<bool> CodeExistsAsync(string code);
}
