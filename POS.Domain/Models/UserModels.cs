using System.ComponentModel.DataAnnotations;
using POS.Domain.Enums;

namespace POS.Domain.Models;

/// <summary>
/// Request to create a new user.
/// </summary>
public class CreateUserRequest
{
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = null!;

    public UserRole Role { get; set; }

    public int? BranchId { get; set; }

    /// <summary>
    /// PIN for Cashier, Waiter, Kitchen roles (4 digits).
    /// </summary>
    public string? Pin { get; set; }

    /// <summary>
    /// Email for Owner and Manager roles.
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// Password for Owner and Manager roles.
    /// </summary>
    public string? Password { get; set; }
}

/// <summary>
/// Request to update an existing user.
/// </summary>
public class UpdateUserRequest
{
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = null!;

    public UserRole Role { get; set; }

    public bool IsActive { get; set; }

    /// <summary>
    /// Optional — only update PIN if provided.
    /// </summary>
    public string? Pin { get; set; }

    /// <summary>
    /// Optional — only update password if provided.
    /// </summary>
    public string? Password { get; set; }
}

/// <summary>
/// User data for display (no sensitive fields).
/// </summary>
public class UserDto
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Email { get; set; }
    public UserRole Role { get; set; }
    public string RoleName { get; set; } = null!;
    public int? BranchId { get; set; }
    public bool IsActive { get; set; }
    public bool HasPin { get; set; }
    public bool HasEmail { get; set; }
    public DateTime CreatedAt { get; set; }
}
