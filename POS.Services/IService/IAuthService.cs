using POS.Domain.DTOs.Auth;
using POS.Domain.Models;

namespace POS.Services.IService;

/// <summary>
/// Provides authentication operations.
/// </summary>
public interface IAuthService
{
    /// <summary>
    /// Authenticates an owner by email and password. Returns a JWT token response.
    /// </summary>
    Task<AuthResponse> EmailLoginAsync(string email, string password);

    /// <summary>
    /// Authenticates a staff member by branch PIN. Returns a JWT token response.
    /// </summary>
    Task<AuthResponse> PinLoginAsync(int branchId, string pin);

    /// <summary>
    /// Switches the authenticated user to a different branch and returns a new JWT.
    /// Owner/Manager can switch to any branch in their business.
    /// Other roles can only switch to branches assigned via UserBranch.
    /// Preserves the <paramref name="sessionType"/> from the incoming token so the
    /// new JWT carries the same login modality (email vs pin).
    /// </summary>
    /// <exception cref="POS.Domain.Exceptions.UnauthorizedException">
    /// Thrown when <paramref name="sessionType"/> is missing or not one of
    /// <c>email</c> / <c>pin</c>. No graceful-fallback path exists.
    /// </exception>
    Task<AuthResponse> SwitchBranchAsync(int userId, int branchId, string? sessionType);

    /// <summary>
    /// Registers a new business with owner account and matrix branch.
    /// Returns JWT token so user can immediately enter the app.
    /// </summary>
    Task<AuthResponse> RegisterAsync(RegisterRequest request);

    /// <summary>
    /// Rehydrates the current session from the database and returns a freshly minted
    /// JWT plus an up-to-date <see cref="AuthResponse"/>. Used by the SPA on boot and
    /// after onboarding completion to break out of stale-token redirect loops.
    /// The <paramref name="sessionType"/> value is preserved from the incoming token
    /// so the session modality (email vs pin) and its expiration bound do not change.
    /// </summary>
    /// <exception cref="POS.Domain.Exceptions.UnauthorizedException">
    /// Thrown when the user or business is no longer active, or when
    /// <paramref name="sessionType"/> is missing or not one of <c>email</c> /
    /// <c>pin</c>. No graceful-fallback path exists.
    /// </exception>
    Task<AuthResponse> GetSessionAsync(int userId, string? sessionType);

    /// <summary>
    /// Issues a long-lived JWT that represents a physical device (KDS screen, kiosk)
    /// rather than a human user. The token carries <c>businessId</c>, <c>branchId</c>,
    /// <c>deviceId</c>, a <c>type=device</c> discriminator and the resolved feature
    /// matrix. It has no <c>userId</c> / <c>roleId</c> and its lifetime is measured
    /// in years so that infrastructure hardware does not require human re-auth.
    ///
    /// <paramref name="macroCode"/> is the resolved <c>MacroCategory.InternalCode</c>
    /// passed in by the caller — this method intentionally does not touch the
    /// <c>Business.PrimaryMacroCategory</c> navigation property, since that nav
    /// is rarely eager-loaded and silently produces an empty <c>macroCategory</c>
    /// claim.
    /// </summary>
    string GenerateDeviceToken(Device device, Business business, string macroCode, IReadOnlyList<string> features);
}

/// <summary>
/// Request for public registration. Only the macro category is captured at this stage —
/// sub-giros and <c>CustomGiroDescription</c> are assigned later during onboarding.
/// </summary>
public class RegisterRequest
{
    public string BusinessName { get; set; } = null!;
    public string OwnerName { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string Password { get; set; } = null!;

    /// <summary>Macro category id (1-4) that drives POS experience and plan rules.</summary>
    public int PrimaryMacroCategoryId { get; set; }

    public int? PlanTypeId { get; set; }
    public string? FolioPrefix { get; set; }
    public string? CountryCode { get; set; }

    /// <summary>
    /// IANA timezone identifier for the matrix branch. Validated and persisted
    /// by <see cref="IAuthService.RegisterAsync"/>. Unknown values raise
    /// <see cref="POS.Domain.Exceptions.ValidationException"/>.
    /// </summary>
    public string? TimeZoneId { get; set; }

    /// <summary>
    /// When <c>true</c>, <see cref="IAuthService.RegisterAsync"/> skips the
    /// fire-and-forget welcome email dispatch. Default <c>false</c> preserves
    /// the public <c>POST /api/Auth/register</c> behavior; the admin tenant
    /// creation endpoint overrides this to <c>true</c> for demo flows where
    /// no real customer should receive the email.
    /// </summary>
    public bool SuppressWelcomeEmail { get; set; } = false;

    /// <summary>
    /// Admin-flow extension: full sub-giro id set persisted as
    /// <c>BusinessGiro</c> rows inside the same registration transaction.
    /// Validated against <c>BusinessTypeCatalog</c>; unknown ids roll the
    /// transaction back. Null preserves the public Register behavior
    /// (no sub-giros assigned at registration — captured later via
    /// <c>PUT /api/business/giro</c>).
    /// </summary>
    public IReadOnlyList<int>? SubGiroIds { get; set; }

    /// <summary>
    /// Admin-flow extension: free-text giro description used when the
    /// operator selects "Otra" instead of a catalog id. Persisted on
    /// <c>Business.CustomGiroDescription</c>. Null preserves default.
    /// </summary>
    public string? CustomGiroDescription { get; set; }

    /// <summary>
    /// Admin-flow extension: fiscal configuration persisted on the
    /// <c>Business</c> entity (Rfc, TaxRegime, LegalName, InvoicingEnabled)
    /// inside the same registration transaction. Null leaves these
    /// columns at their schema defaults (NULL / false). When supplied
    /// with <see cref="FiscalConfigInput.InvoicingEnabled"/> = true, the
    /// CfdiInvoicing feature gate is intentionally bypassed because the
    /// super admin is provisioning the tenant — the gate exists to stop
    /// end users from enabling a paid feature, not the super admin.
    /// </summary>
    public FiscalConfigInput? FiscalConfig { get; set; }

    /// <summary>
    /// Admin-flow extension: when <c>true</c>, marks the freshly created
    /// <c>Business</c> as fully onboarded (<c>OnboardingCompleted = true</c>,
    /// <c>OnboardingStatusId = 3</c>) so the Owner JWT carries
    /// <c>onboardingCompleted = true</c> at first login and the SPA route
    /// guard sends them straight to the dashboard. Default <c>false</c>
    /// preserves the public Register flow that walks the wizard.
    /// </summary>
    public bool MarkOnboardingComplete { get; set; } = false;
}

/// <summary>
/// Service-layer payload for <see cref="RegisterRequest.FiscalConfig"/>.
/// Mirrors the four columns that <c>POST /api/Business/fiscal</c> updates
/// post-registration; centralizes them here so the admin endpoint can
/// provision a tax-ready tenant in one shot. Address / contact still live
/// on the matrix <c>Branch</c> and are out of scope for this payload.
/// </summary>
public sealed class FiscalConfigInput
{
    /// <summary>Mexico SAT identifier. 12 chars for companies, 13 for individuals.</summary>
    public string? Rfc { get; set; }

    /// <summary>SAT regime code (e.g. "601" General de Ley, "612" Personas Físicas).</summary>
    public string? TaxRegime { get; set; }

    /// <summary>Razón social as registered with SAT.</summary>
    public string? LegalName { get; set; }

    /// <summary>
    /// Toggles CFDI invoicing for the tenant. When the admin endpoint sets
    /// this to <c>true</c>, the <c>CfdiInvoicing</c> feature gate is
    /// bypassed (admin provisioning, not end-user upgrade).
    /// </summary>
    public bool InvoicingEnabled { get; set; }
}

/// <summary>
/// Response returned after successful authentication.
/// </summary>
public class AuthResponse
{
    public string Token { get; set; } = null!;
    public int RoleId { get; set; }
    public string Name { get; set; } = null!;
    public int BusinessId { get; set; }

    /// <summary>
    /// The branch ID used for the current session (included in the JWT).
    /// </summary>
    public int CurrentBranchId { get; set; }

    /// <summary>
    /// All branches the user has access to.
    /// </summary>
    public List<BranchSummary> Branches { get; set; } = new();

    public int PlanTypeId { get; set; }

    /// <summary>Drives POS experience, plan rules and Stripe pricing group.</summary>
    public int PrimaryMacroCategoryId { get; set; }

    public string? TrialEndsAt { get; set; }
    public string? SubscriptionStatus { get; set; }
    public bool OnboardingCompleted { get; set; }

    /// <summary>
    /// 1-based onboarding step the user is currently on. Sourced from
    /// <c>Business.CurrentOnboardingStep</c>. Frontend uses this to resume the
    /// onboarding wizard at the right step instead of defaulting to step 1.
    /// </summary>
    public int CurrentOnboardingStep { get; set; }

    /// <summary>
    /// FK to <c>OnboardingStatusCatalog.Id</c> (1=Pending, 2=InProgress, 3=Completed,
    /// 4=Skipped). Surfaces catalog state so the frontend can distinguish
    /// <c>OnboardingCompleted=false</c> + InProgress from + Skipped.
    /// </summary>
    public int OnboardingStatusId { get; set; }

    /// <summary>
    /// UTC timestamp (ISO 8601 with offset) of the moment the user
    /// dismissed the First-Run Experience welcome screen, or <c>null</c>
    /// when they have not seen it yet. The SPA route guard redirects to
    /// <c>/welcome</c> when this is null; the welcome screen calls
    /// <c>POST /api/User/welcome-shown</c> to populate it.
    /// </summary>
    public string? WelcomeShownAt { get; set; }

    /// <summary>
    /// Per-business operational counts the welcome screen and dashboard
    /// widgets use to render derived state. Cross-branch — reflects the
    /// entire business, not just the caller's current branch.
    /// </summary>
    public BusinessSnapshot Snapshot { get; set; } = new(0, 0, 0, 0, 0, 0);
}

/// <summary>
/// Lightweight branch representation for login responses and JWT claims.
/// </summary>
public class BranchSummary
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
}
