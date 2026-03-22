using POS.Domain.Models;
using POS.Repository.IRepository;

namespace POS.Repository.Repository;

public class CashMovementRepository : GenericRepository<CashMovement>, ICashMovementRepository
{
    public CashMovementRepository(ApplicationDbContext context) : base(context)
    {
    }
}
