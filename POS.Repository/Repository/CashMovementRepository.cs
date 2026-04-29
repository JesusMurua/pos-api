using Microsoft.EntityFrameworkCore;
using POS.Domain.Models;
using POS.Repository.IRepository;

namespace POS.Repository.Repository;

public class CashMovementRepository : GenericRepository<CashMovement>, ICashMovementRepository
{
    public CashMovementRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<CashMovement?> GetByIdWithUserAsync(int id)
    {
        return await _context.CashMovements
            .Include(m => m.CreatedByUser)
            .FirstOrDefaultAsync(m => m.Id == id);
    }
}
