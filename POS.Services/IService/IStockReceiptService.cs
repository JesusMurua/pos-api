using POS.Domain.Models;

namespace POS.Services.IService;

/// <summary>
/// Provides operations for managing stock receipts.
/// </summary>
public interface IStockReceiptService
{
    /// <summary>
    /// Gets all stock receipts for a branch with optional filters.
    /// </summary>
    Task<IEnumerable<StockReceipt>> GetAllAsync(int branchId, int? supplierId, DateTime? from, DateTime? to);

    /// <summary>
    /// Gets a stock receipt by its identifier with items, validated against branch.
    /// </summary>
    /// <exception cref="POS.Domain.Exceptions.NotFoundException">Thrown when receipt not found or belongs to different branch.</exception>
    Task<StockReceipt> GetByIdAsync(int id, int branchId);

    /// <summary>
    /// Creates a stock receipt, saves items, and processes inventory movements in a single transaction.
    /// </summary>
    /// <exception cref="POS.Domain.Exceptions.ValidationException">Thrown when item validation fails.</exception>
    Task<StockReceipt> CreateAsync(CreateStockReceiptRequest request, int branchId, int userId);
}
