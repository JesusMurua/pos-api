using POS.Domain.Models;
using POS.Repository.IRepository;

namespace POS.Repository.Repository;

public class InventoryMovementRepository : GenericRepository<InventoryMovement>, IInventoryMovementRepository
{
    public InventoryMovementRepository(ApplicationDbContext context) : base(context)
    {
    }
}
