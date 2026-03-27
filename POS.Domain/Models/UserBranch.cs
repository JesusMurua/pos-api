namespace POS.Domain.Models;

/// <summary>
/// Join entity representing the many-to-many relationship between users and branches.
/// A user can belong to multiple branches; one branch is marked as default for JWT generation.
/// </summary>
public class UserBranch
{
    /// <summary>
    /// The user identifier (FK to User).
    /// </summary>
    public int UserId { get; set; }

    /// <summary>
    /// The branch identifier (FK to Branch).
    /// </summary>
    public int BranchId { get; set; }

    /// <summary>
    /// Whether this is the user's default branch, used for JWT claim generation.
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>
    /// Navigation property to the related user.
    /// </summary>
    public virtual User? User { get; set; }

    /// <summary>
    /// Navigation property to the related branch.
    /// </summary>
    public virtual Branch? Branch { get; set; }
}
