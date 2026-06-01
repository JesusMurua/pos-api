using POS.Domain.DTOs.Auth;
using POS.Repository;
using POS.Services.IService;

namespace POS.Services.Service;

/// <inheritdoc cref="IBusinessSnapshotService"/>
public class BusinessSnapshotService : IBusinessSnapshotService
{
    private readonly IUnitOfWork _unitOfWork;

    public BusinessSnapshotService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<BusinessSnapshot> BuildAsync(int businessId)
    {
        // Sequential rather than Task.WhenAll because every query shares
        // the scoped ApplicationDbContext — concurrent operations throw
        // "second operation started before previous completed". For typical
        // tenant sizes each count stays sub-10ms; total wall-clock holds
        // well under 100ms.
        var userCount = await _unitOfWork.Users.CountAsync(u => u.BusinessId == businessId);
        var branchCount = await _unitOfWork.Branches.CountAsync(b => b.BusinessId == businessId);
        var productCount = await _unitOfWork.Products.CountForBusinessAsync(businessId);
        var tableCount = await _unitOfWork.RestaurantTables.CountForBusinessAsync(businessId);
        var cashRegisterCount = await _unitOfWork.CashRegisters.CountForBusinessAsync(businessId);
        var deviceCount = await _unitOfWork.Devices.CountForBusinessAsync(businessId);

        return new BusinessSnapshot(
            UserCount: userCount,
            ProductCount: productCount,
            BranchCount: branchCount,
            TableCount: tableCount,
            CashRegisterCount: cashRegisterCount,
            DeviceCount: deviceCount);
    }
}
