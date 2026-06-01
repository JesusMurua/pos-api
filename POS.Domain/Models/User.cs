using System.ComponentModel.DataAnnotations;
using POS.Domain.Enums;
using POS.Domain.Helpers;
using POS.Domain.Interfaces;
using POS.Domain.Models.Catalogs;

namespace POS.Domain.Models;

public partial class User : IBusinessScoped
{
    public int Id { get; set; }

    public int BusinessId { get; set; }

    public int? BranchId { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = null!;

    [MaxLength(150)]
    public string? Email { get; set; }

    [MaxLength(255)]
    public string? PasswordHash { get; set; }

    [MaxLength(255)]
    public string? PinHash { get; set; }

    /// <summary>FK to UserRoleCatalog.Id (1=Owner, 2=Manager, 3=Cashier, 4=Kitchen, 5=Waiter, 6=Kiosk, 7=Host).</summary>
    public int RoleId { get; set; } = UserRoleIds.Cashier;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// UTC timestamp of the last successful credential authentication
    /// (<see cref="POS.Services.IService.IAuthService.EmailLoginAsync"/> or
    /// <see cref="POS.Services.IService.IAuthService.PinLoginAsync"/>). Not
    /// updated by session-rehydrate calls (<c>/api/Auth/me</c>) so the
    /// semantics stay strict: this is "when did the user last sign in",
    /// not "when was the user last active". If activity tracking is needed,
    /// add a separate <c>LastSeenAt</c> column in a future migration.
    /// </summary>
    public DateTime? LastLoginAt { get; set; }

    /// <summary>
    /// UTC timestamp of the moment the user dismissed the First-Run
    /// Experience welcome screen. Default null; set once on the first
    /// successful <c>POST /api/User/welcome-shown</c> call and preserved
    /// thereafter so subsequent dismissals do not move the timestamp.
    /// Surfaced as the <c>welcomeShownAt</c> JWT claim so the SPA route
    /// guard can redirect to <c>/welcome</c> without a database round-trip.
    /// </summary>
    public DateTime? WelcomeShownAt { get; set; }

    public UserRoleCatalog? RoleCatalog { get; set; }

    public virtual Business? Business { get; set; }

    public virtual Branch? Branch { get; set; }

    public virtual ICollection<Order>? Orders { get; set; }

    /// <summary>
    /// Branches this user belongs to (many-to-many via UserBranch).
    /// </summary>
    public virtual ICollection<UserBranch>? UserBranches { get; set; }

    public virtual ICollection<Reservation>? CreatedReservations { get; set; }
}
