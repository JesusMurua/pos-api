using POS.Domain.Models;

namespace POS.Repository.IRepository;

public interface ICashMovementRepository : IGenericRepository<CashMovement>
{
    /// <summary>
    /// Tracked fetch by id with the <c>CreatedByUser</c> navigation eager-loaded,
    /// so the service can map straight to <c>CashMovementDto</c> without a
    /// follow-up query.
    /// </summary>
    Task<CashMovement?> GetByIdWithUserAsync(int id);
}
