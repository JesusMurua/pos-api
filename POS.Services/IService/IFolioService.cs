namespace POS.Services.IService;

/// <summary>
/// Provides atomic folio number generation per branch.
/// </summary>
public interface IFolioService
{
    /// <summary>
    /// Atomically generates the next folio number for a branch.
    /// </summary>
    Task<string> GenerateAsync(int branchId);
}
