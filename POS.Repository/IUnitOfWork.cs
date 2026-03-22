using POS.Repository.IRepository;

namespace POS.Repository;

public interface IUnitOfWork : IDisposable
{
    IProductRepository Products { get; }
    ICategoryRepository Categories { get; }
    IOrderRepository Orders { get; }
    IBranchRepository Branches { get; }
    IBusinessRepository Business { get; }
    IUserRepository Users { get; }
    IDiscountPresetRepository DiscountPresets { get; }
    ICashRegisterSessionRepository CashRegisterSessions { get; }
    ICashMovementRepository CashMovements { get; }

    Task<int> SaveChangesAsync();
}
