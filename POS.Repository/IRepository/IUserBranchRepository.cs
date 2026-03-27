using POS.Domain.Models;

namespace POS.Repository.IRepository;

/// <summary>
/// Repository interface for user-branch assignments.
/// </summary>
public interface IUserBranchRepository : IGenericRepository<UserBranch>
{
    /// <summary>
    /// Gets all branch assignments for a user, including branch navigation.
    /// </summary>
    Task<IEnumerable<UserBranch>> GetByUserIdAsync(int userId);

    /// <summary>
    /// Gets all user assignments for a branch, including user navigation.
    /// </summary>
    Task<IEnumerable<UserBranch>> GetByBranchIdAsync(int branchId);
}
