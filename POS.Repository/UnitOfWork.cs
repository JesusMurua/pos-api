using POS.Repository.IRepository;
using POS.Repository.Repository;

namespace POS.Repository;

public class UnitOfWork : IUnitOfWork
{
    private readonly ApplicationDbContext _context;

    private IProductRepository? _products;
    private ICategoryRepository? _categories;
    private IOrderRepository? _orders;
    private IBranchRepository? _branches;
    private IBusinessRepository? _business;
    private IUserRepository? _users;
    private IDiscountPresetRepository? _discountPresets;
    private ICashRegisterSessionRepository? _cashRegisterSessions;
    private ICashMovementRepository? _cashMovements;

    public UnitOfWork(ApplicationDbContext context)
    {
        _context = context;
    }

    public IProductRepository Products =>
        _products ??= new ProductRepository(_context);

    public ICategoryRepository Categories =>
        _categories ??= new CategoryRepository(_context);

    public IOrderRepository Orders =>
        _orders ??= new OrderRepository(_context);

    public IBranchRepository Branches =>
        _branches ??= new BranchRepository(_context);

    public IBusinessRepository Business =>
        _business ??= new BusinessRepository(_context);

    public IUserRepository Users =>
        _users ??= new UserRepository(_context);

    public IDiscountPresetRepository DiscountPresets =>
        _discountPresets ??= new DiscountPresetRepository(_context);

    public ICashRegisterSessionRepository CashRegisterSessions =>
        _cashRegisterSessions ??= new CashRegisterSessionRepository(_context);

    public ICashMovementRepository CashMovements =>
        _cashMovements ??= new CashMovementRepository(_context);

    public async Task<int> SaveChangesAsync()
    {
        return await _context.SaveChangesAsync();
    }

    public void Dispose()
    {
        _context.Dispose();
    }
}
