using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using POS.API.Auth;
using POS.Domain.DTOs.Admin;
using POS.Domain.DTOs.Common;
using POS.Domain.Exceptions;
using POS.Domain.Helpers;
using POS.Domain.Models;
using POS.Repository;
using POS.Services.IService;

namespace POS.API.Controllers;

/// <summary>
/// Ops-only tenant directory. Authenticated exclusively via the
/// <c>X-Admin-Token</c> header (see <see cref="AdminTokenAuthenticationHandler"/>)
/// and rate limited to 30 req/min/IP via the
/// <c>AdminBusinessCreationPolicy</c> sliding window so a runaway script
/// cannot mass-create tenants or scrape the directory. Bypasses the BDD-019
/// tenant query filters via <see cref="IBusinessRepository.GetAllForAdminAsync"/>
/// — never expose these routes to the end-user JWT scheme.
/// </summary>
[Route("api/Admin/businesses")]
[ApiController]
[Authorize(AuthenticationSchemes = AdminTokenAuthenticationHandler.SchemeName)]
[EnableRateLimiting("AdminBusinessCreationPolicy")]
public class AdminBusinessesController : ControllerBase
{
    private const int MaxPageSize = 200;
    private const int DefaultPageSize = 50;

    private readonly IAuthService _authService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserService _userService;
    private readonly IBusinessSnapshotService _businessSnapshot;
    private readonly IFeatureGateService _featureGate;
    private readonly ILogger<AdminBusinessesController> _logger;

    public AdminBusinessesController(
        IAuthService authService,
        IUnitOfWork unitOfWork,
        IUserService userService,
        IBusinessSnapshotService businessSnapshot,
        IFeatureGateService featureGate,
        ILogger<AdminBusinessesController> logger)
    {
        _authService = authService;
        _unitOfWork = unitOfWork;
        _userService = userService;
        _businessSnapshot = businessSnapshot;
        _featureGate = featureGate;
        _logger = logger;
    }

    /// <summary>
    /// Provisions a new tenant (Business + matrix Branch + Owner User +
    /// macro-shaped seed data) in a single atomic transaction by delegating
    /// to <see cref="IAuthService.RegisterAsync"/>. Returns the freshly
    /// created identifiers and, when <see cref="AdminCreateBusinessRequest.IncludeOwnerJwt"/>
    /// is true, the Owner JWT so the super admin can drop into the tenant's
    /// POS without a manual login.
    /// </summary>
    /// <response code="200">Tenant created successfully.</response>
    /// <response code="400">Validation error (invalid macro, missing default tax, etc.).</response>
    /// <response code="401">Missing or invalid <c>X-Admin-Token</c> header.</response>
    /// <response code="409">Email already registered to another tenant.</response>
    /// <response code="429">Rate limit exceeded.</response>
    [HttpPost]
    [ProducesResponseType(typeof(AdminCreateBusinessResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Create([FromBody] AdminCreateBusinessRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var stopwatch = Stopwatch.StartNew();
        var resolvedPlanTypeId = request.PlanTypeId ?? PlanTypeIds.Free;

        try
        {
            var registerRequest = new RegisterRequest
            {
                BusinessName = request.BusinessName,
                OwnerName = request.OwnerName,
                Email = request.Email,
                Password = request.Password,
                PrimaryMacroCategoryId = request.PrimaryMacroCategoryId,
                PlanTypeId = resolvedPlanTypeId,
                FolioPrefix = request.FolioPrefix,
                CountryCode = request.CountryCode,
                TimeZoneId = request.TimeZoneId,
                SuppressWelcomeEmail = request.SuppressWelcomeEmail,
                SubGiroIds = request.SubGiroIds,
                CustomGiroDescription = request.CustomGiroDescription,
                FiscalConfig = request.FiscalConfig is null
                    ? null
                    : new FiscalConfigInput
                    {
                        Rfc = request.FiscalConfig.Rfc,
                        TaxRegime = request.FiscalConfig.TaxRegime,
                        LegalName = request.FiscalConfig.LegalName,
                        InvoicingEnabled = request.FiscalConfig.InvoicingEnabled
                    },
                MarkOnboardingComplete = request.MarkOnboardingComplete
            };

            var auth = await _authService.RegisterAsync(registerRequest);

            var business = await _unitOfWork.Business.GetByIdAsync(auth.BusinessId)
                ?? throw new InvalidOperationException(
                    $"Business {auth.BusinessId} was created but could not be reloaded for the response.");

            var response = new AdminCreateBusinessResponse(
                BusinessId: auth.BusinessId,
                OwnerEmail: request.Email,
                OwnerName: request.OwnerName,
                PlanTypeId: auth.PlanTypeId,
                PlanTypeCode: PlanTypeIds.ToCode(auth.PlanTypeId),
                PrimaryMacroCategoryId: auth.PrimaryMacroCategoryId,
                PrimaryMacroCategoryCode: MacroCategoryIds.ToCode(auth.PrimaryMacroCategoryId),
                TrialEndsAt: auth.TrialEndsAt,
                CreatedAt: business.CreatedAt.ToString("o"),
                OnboardingCompleted: auth.OnboardingCompleted,
                OnboardingStatusId: auth.OnboardingStatusId,
                OwnerJwt: request.IncludeOwnerJwt ? auth.Token : null);

            LogCreateAudit(
                result: "Success",
                businessId: auth.BusinessId,
                email: request.Email,
                durationMs: stopwatch.ElapsedMilliseconds);

            return Ok(response);
        }
        catch (ValidationException ex) when (ex.Message == "EMAIL_ALREADY_EXISTS")
        {
            LogCreateAudit(
                result: "EmailDuplicate",
                businessId: null,
                email: request.Email,
                durationMs: stopwatch.ElapsedMilliseconds);
            return Conflict(new { message = "Email already registered" });
        }
        catch (ValidationException ex)
        {
            LogCreateAudit(
                result: "ValidationError",
                businessId: null,
                email: request.Email,
                durationMs: stopwatch.ElapsedMilliseconds);
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Admin createBusiness failed unexpectedly for email {Email}", request.Email);
            LogCreateAudit(
                result: "Error",
                businessId: null,
                email: request.Email,
                durationMs: stopwatch.ElapsedMilliseconds);
            return Problem(
                title: "Tenant creation failed",
                detail: "An unexpected error occurred. Check server logs for the correlation id.",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Cross-tenant paginated directory of every business in the system.
    /// Bypasses the BDD-019 tenant query filters so the super admin sees
    /// every row regardless of the request's <c>ITenantContext</c>.
    /// </summary>
    /// <response code="200">Page of businesses matching the optional search.</response>
    /// <response code="401">Missing or invalid <c>X-Admin-Token</c> header.</response>
    /// <response code="429">Rate limit exceeded.</response>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<AdminBusinessListItem>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> List(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = DefaultPageSize,
        [FromQuery] string? search = null,
        [FromQuery] int? planTypeId = null,
        [FromQuery] int? primaryMacroCategoryId = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] string? trialStatus = null,
        [FromQuery] string sortBy = "createdAt",
        [FromQuery] string sortDir = "desc",
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        var clampedPageNumber = Math.Max(1, pageNumber);
        var clampedPageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var (rows, total) = await _unitOfWork.Business.GetAllForAdminAsync(
            pageNumber: clampedPageNumber,
            pageSize: clampedPageSize,
            search: search,
            planTypeId: planTypeId,
            primaryMacroCategoryId: primaryMacroCategoryId,
            isActive: isActive,
            trialStatus: trialStatus,
            sortBy: sortBy,
            sortDir: sortDir,
            cancellationToken: cancellationToken);

        var items = rows.Select(b =>
        {
            var owner = b.Users?
                .Where(u => u.RoleId == UserRoleIds.Owner)
                .OrderBy(u => u.CreatedAt)
                .FirstOrDefault();

            return new AdminBusinessListItem(
                Id: b.Id,
                Name: b.Name,
                OwnerEmail: owner?.Email,
                OwnerName: owner?.Name,
                PlanTypeId: b.PlanTypeId,
                PlanTypeCode: PlanTypeIds.ToCode(b.PlanTypeId),
                PrimaryMacroCategoryId: b.PrimaryMacroCategoryId,
                PrimaryMacroCategoryCode: MacroCategoryIds.ToCode(b.PrimaryMacroCategoryId),
                TrialEndsAt: b.TrialEndsAt?.ToString("o"),
                IsActive: b.IsActive,
                CreatedAt: b.CreatedAt.ToString("o"));
        }).ToList();

        var envelope = new PagedResponse<AdminBusinessListItem>(
            Items: items,
            TotalCount: total,
            PageSize: clampedPageSize,
            PageNumber: clampedPageNumber);

        LogListAudit(
            pageNumber: clampedPageNumber,
            pageSize: clampedPageSize,
            search: search,
            resultCount: items.Count,
            durationMs: stopwatch.ElapsedMilliseconds);

        return Ok(envelope);
    }

    /// <summary>
    /// Cross-tenant aggregate stats for the super-admin dashboard.
    /// Repo emits raw counts and groupings (numeric keys) — this action
    /// resolves the public PlanTypeCode / MacroCategoryCode via the
    /// static helpers and backfills <c>CreatedByMonth</c> with the six
    /// calendar months ending at "now" so the FE chart has a stable
    /// shape regardless of which months actually had creates.
    /// </summary>
    /// <response code="200">Aggregate snapshot.</response>
    /// <response code="401">Missing or invalid <c>X-Admin-Token</c> header.</response>
    /// <response code="429">Rate limit exceeded.</response>
    [HttpGet("stats")]
    [ProducesResponseType(typeof(AdminBusinessStatsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Stats(CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        // Single nowUtc read so every count in the response shares the
        // same instant — trials and growth window calculations stay
        // self-consistent even if the underlying clock ticks during the
        // ~100ms the repo spends issuing eleven sequential queries.
        var nowUtc = DateTime.UtcNow;
        var raw = await _unitOfWork.Business.GetAdminStatsAsync(nowUtc, cancellationToken);

        var byPlan = raw.CountsByPlanTypeId
            .Select(kvp => new PlanDistribution(
                PlanTypeId: kvp.Key,
                PlanTypeCode: PlanTypeIds.ToCode(kvp.Key),
                Count: kvp.Value))
            .OrderBy(p => p.PlanTypeId)
            .ToList();

        var byMacro = raw.CountsByMacroId
            .Select(kvp => new MacroDistribution(
                PrimaryMacroCategoryId: kvp.Key,
                PrimaryMacroCategoryCode: MacroCategoryIds.ToCode(kvp.Key),
                Count: kvp.Value))
            .OrderBy(m => m.PrimaryMacroCategoryId)
            .ToList();

        // Backfill the six calendar months ending at the current month.
        // The loop runs oldest → newest so the FE consumes the array in
        // chronological order without sorting.
        var currentMonthStart = new DateTime(nowUtc.Year, nowUtc.Month, 1);
        var createdByMonth = new List<MonthlyCount>(6);
        for (var offset = 5; offset >= 0; offset--)
        {
            var bucket = currentMonthStart.AddMonths(-offset);
            raw.CountsByYearMonth.TryGetValue((bucket.Year, bucket.Month), out var count);
            createdByMonth.Add(new MonthlyCount(bucket.Year, bucket.Month, count));
        }

        var response = new AdminBusinessStatsResponse(
            TotalBusinesses: raw.TotalBusinesses,
            ActiveBusinesses: raw.ActiveBusinesses,
            InactiveBusinesses: raw.InactiveBusinesses,
            ByPlan: byPlan,
            ByMacro: byMacro,
            TrialsExpiring7Days: raw.TrialsExpiring7Days,
            TrialsExpiring14Days: raw.TrialsExpiring14Days,
            OnboardingCompleted: raw.OnboardingCompleted,
            OnboardingPending: raw.OnboardingPending,
            TotalUsers: raw.TotalUsers,
            TotalProducts: raw.TotalProducts,
            CreatedByMonth: createdByMonth);

        LogStatsAudit(stopwatch.ElapsedMilliseconds);

        return Ok(response);
    }

    /// <summary>
    /// Full tenant detail view used by the fino-admin SPA when the
    /// operator opens a business row from the directory list.
    /// </summary>
    /// <response code="200">Tenant detail.</response>
    /// <response code="404">Business not found.</response>
    /// <response code="401">Missing or invalid <c>X-Admin-Token</c>.</response>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(AdminBusinessDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var business = await _unitOfWork.Business.GetByIdForAdminAsync(id, cancellationToken);
        if (business is null)
        {
            LogActionAudit("AdminBusinessGetByIdNotFound", id, stopwatch.ElapsedMilliseconds);
            return NotFound();
        }

        var detail = await BuildDetailAsync(business);
        LogActionAudit("AdminBusinessGetById", id, stopwatch.ElapsedMilliseconds);
        return Ok(detail);
    }

    /// <summary>
    /// Suspends or reactivates a tenant. When <c>IsActive=false</c> the
    /// owner / staff are immediately blocked from authenticating via
    /// email or PIN login (gate enforced in <c>AuthService</c>).
    /// </summary>
    [HttpPatch("{id:int}/status")]
    [ProducesResponseType(typeof(AdminBusinessDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ToggleStatus(
        int id,
        [FromBody] AdminToggleBusinessStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var business = await _unitOfWork.Business.GetByIdForAdminAsync(id, cancellationToken);
        if (business is null) return NotFound();

        business.IsActive = request.IsActive;
        await _unitOfWork.SaveChangesAsync();
        _featureGate.Invalidate(id);

        _logger.LogInformation(
            "Admin tenant status change for {BusinessId}: IsActive={IsActive}, reason={Reason}",
            id, request.IsActive, request.Reason ?? "(none)");

        // Refetch via the admin path to get the post-mutation Branches +
        // Owner inclusion populated; the snapshot also rebuilds with the
        // up-to-date IsActive flag if the FE renders derived state.
        var fresh = await _unitOfWork.Business.GetByIdForAdminAsync(id, cancellationToken);
        var detail = await BuildDetailAsync(fresh!);
        LogActionAudit("AdminBusinessToggleStatus", id, stopwatch.ElapsedMilliseconds);
        return Ok(detail);
    }

    /// <summary>
    /// Changes the tenant's plan directly (admin override). Stripe-managed
    /// subscriptions will resync on the next webhook event — the worker
    /// treats Stripe as single source of truth for billed tenants.
    /// </summary>
    [HttpPatch("{id:int}/plan")]
    [ProducesResponseType(typeof(AdminBusinessDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ChangePlan(
        int id,
        [FromBody] AdminChangePlanRequest request,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var business = await _unitOfWork.Business.GetByIdForAdminAsync(id, cancellationToken);
        if (business is null) return NotFound();

        business.PlanTypeId = request.PlanTypeId;
        await _unitOfWork.SaveChangesAsync();
        _featureGate.Invalidate(id);

        var fresh = await _unitOfWork.Business.GetByIdForAdminAsync(id, cancellationToken);
        var detail = await BuildDetailAsync(fresh!);
        LogActionAudit("AdminBusinessChangePlan", id, stopwatch.ElapsedMilliseconds);
        return Ok(detail);
    }

    /// <summary>
    /// Extends the in-app trial window to <see cref="AdminExtendTrialRequest.NewTrialEndsAt"/>.
    /// Rejects past dates and instants more than 180 days in the future.
    /// </summary>
    [HttpPatch("{id:int}/trial")]
    [ProducesResponseType(typeof(AdminBusinessDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ExtendTrial(
        int id,
        [FromBody] AdminExtendTrialRequest request,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var nowUtc = DateTime.UtcNow;
        if (request.NewTrialEndsAt <= nowUtc)
            return BadRequest(new { message = "NewTrialEndsAt must be in the future." });
        if (request.NewTrialEndsAt > nowUtc.AddDays(180))
            return BadRequest(new { message = "NewTrialEndsAt cannot be more than 180 days from now." });

        var business = await _unitOfWork.Business.GetByIdForAdminAsync(id, cancellationToken);
        if (business is null) return NotFound();

        business.TrialEndsAt = request.NewTrialEndsAt;
        await _unitOfWork.SaveChangesAsync();

        var fresh = await _unitOfWork.Business.GetByIdForAdminAsync(id, cancellationToken);
        var detail = await BuildDetailAsync(fresh!);
        LogActionAudit("AdminBusinessExtendTrial", id, stopwatch.ElapsedMilliseconds);
        return Ok(detail);
    }

    /// <summary>
    /// Regenerates (or sets) the tenant Owner's password. The plaintext
    /// password is returned so the operator can relay it to the customer.
    /// </summary>
    [HttpPost("{id:int}/reset-owner-password")]
    [ProducesResponseType(typeof(AdminResetOwnerPasswordResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResetOwnerPassword(
        int id,
        [FromBody] AdminResetOwnerPasswordRequest request)
    {
        var stopwatch = Stopwatch.StartNew();
        if (!ModelState.IsValid) return BadRequest(ModelState);

        try
        {
            var newPassword = await _userService.ResetOwnerPasswordAsync(id, request.NewPassword);
            LogActionAudit("AdminBusinessResetOwnerPassword", id, stopwatch.ElapsedMilliseconds);
            return Ok(new AdminResetOwnerPasswordResponse(newPassword));
        }
        catch (NotFoundException)
        {
            return NotFound();
        }
        catch (ValidationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Issues a short-lived (2 hour) JWT as the tenant's Owner so the
    /// super admin can drop into the POS for support / debugging. Every
    /// impersonation is structured-logged with the caller token id,
    /// target user id, and TTL so retroactive forensics is possible.
    /// </summary>
    [HttpPost("{id:int}/impersonate")]
    [ProducesResponseType(typeof(AdminImpersonateResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Impersonate(
        int id,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var business = await _unitOfWork.Business.GetByIdForAdminAsync(id, cancellationToken);
        if (business is null) return NotFound();

        var owner = business.Users?
            .Where(u => u.RoleId == UserRoleIds.Owner)
            .OrderBy(u => u.CreatedAt)
            .FirstOrDefault();
        if (owner is null) return NotFound(new { message = "Business has no Owner user." });

        var ttl = TimeSpan.FromHours(2);
        var auth = await _authService.GetSessionAsync(owner.Id, "email", ttl);
        var expiresAt = DateTime.UtcNow.Add(ttl);

        LogImpersonateAudit(id, owner.Id, ttl, stopwatch.ElapsedMilliseconds);

        return Ok(new AdminImpersonateResponse(
            OwnerJwt: auth.Token,
            OwnerEmail: owner.Email ?? string.Empty,
            OwnerName: owner.Name,
            ExpiresAt: expiresAt.ToString("o")));
    }

    private async Task<AdminBusinessDetailResponse> BuildDetailAsync(Business business)
    {
        var owner = business.Users?
            .Where(u => u.RoleId == UserRoleIds.Owner)
            .OrderBy(u => u.CreatedAt)
            .FirstOrDefault();

        var snapshot = await _businessSnapshot.BuildAsync(business.Id);

        var branches = business.Branches?
            .OrderBy(b => b.Id)
            .Select(b => new BranchInfo(
                Id: b.Id,
                Name: b.Name,
                IsActive: b.IsActive,
                CreatedAt: b.CreatedAt.ToString("o")))
            .ToList()
            ?? new List<BranchInfo>();

        var subGiroIds = business.BusinessGiros?
            .Select(g => g.BusinessTypeId)
            .OrderBy(x => x)
            .ToList()
            ?? new List<int>();

        return new AdminBusinessDetailResponse(
            Id: business.Id,
            Name: business.Name,
            IsActive: business.IsActive,
            CreatedAt: business.CreatedAt.ToString("o"),
            CountryCode: business.CountryCode,
            PlanTypeId: business.PlanTypeId,
            PlanTypeCode: PlanTypeIds.ToCode(business.PlanTypeId),
            PrimaryMacroCategoryId: business.PrimaryMacroCategoryId,
            PrimaryMacroCategoryCode: MacroCategoryIds.ToCode(business.PrimaryMacroCategoryId),
            CustomGiroDescription: business.CustomGiroDescription,
            SubGiroIds: subGiroIds,
            OnboardingCompleted: business.OnboardingCompleted,
            OnboardingStatusId: business.OnboardingStatusId,
            CurrentOnboardingStep: business.CurrentOnboardingStep,
            TrialEndsAt: business.TrialEndsAt?.ToString("o"),
            TrialUsed: business.TrialUsed,
            Rfc: business.Rfc,
            TaxRegime: business.TaxRegime,
            LegalName: business.LegalName,
            InvoicingEnabled: business.InvoicingEnabled,
            Snapshot: snapshot,
            OwnerEmail: owner?.Email,
            OwnerName: owner?.Name,
            OwnerLastLoginAt: owner?.LastLoginAt?.ToString("o"),
            Branches: branches);
    }

    private void LogActionAudit(string eventType, int businessId, long durationMs)
    {
        var tokenId = User.FindFirst(AdminTokenAuthenticationHandler.TokenIdClaimType)?.Value
                      ?? "anonymous";
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        _logger.LogInformation(
            "Admin tenant action {@AdminBusinessActionAudit}",
            new
            {
                Timestamp = DateTime.UtcNow,
                EventType = eventType,
                CallerTokenId = tokenId,
                CallerIp = ip,
                BusinessId = businessId,
                DurationMs = durationMs
            });
    }

    private void LogImpersonateAudit(int businessId, int targetUserId, TimeSpan ttl, long durationMs)
    {
        var tokenId = User.FindFirst(AdminTokenAuthenticationHandler.TokenIdClaimType)?.Value
                      ?? "anonymous";
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        // Impersonate is the most security-sensitive admin action — log a
        // separate structured event so an audit query can isolate the
        // "who impersonated which Owner, for how long" forensics trail.
        _logger.LogWarning(
            "Admin impersonate {@AdminImpersonateAudit}",
            new
            {
                Timestamp = DateTime.UtcNow,
                CallerTokenId = tokenId,
                CallerIp = ip,
                BusinessId = businessId,
                TargetUserId = targetUserId,
                TtlHours = ttl.TotalHours,
                DurationMs = durationMs
            });
    }

    private void LogCreateAudit(string result, int? businessId, string email, long durationMs)
    {
        var tokenId = User.FindFirst(AdminTokenAuthenticationHandler.TokenIdClaimType)?.Value
                      ?? "anonymous";
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        _logger.LogInformation(
            "Admin createBusiness {@AdminBusinessCreationAudit}",
            new
            {
                Timestamp = DateTime.UtcNow,
                CallerTokenId = tokenId,
                CallerIp = ip,
                BusinessId = businessId,
                Email = email,
                Result = result,
                DurationMs = durationMs
            });
    }

    private void LogListAudit(int pageNumber, int pageSize, string? search, int resultCount, long durationMs)
    {
        var tokenId = User.FindFirst(AdminTokenAuthenticationHandler.TokenIdClaimType)?.Value
                      ?? "anonymous";
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        _logger.LogInformation(
            "Admin listBusinesses {@AdminBusinessListAudit}",
            new
            {
                Timestamp = DateTime.UtcNow,
                CallerTokenId = tokenId,
                CallerIp = ip,
                PageNumber = pageNumber,
                PageSize = pageSize,
                Search = search,
                ResultCount = resultCount,
                DurationMs = durationMs
            });
    }

    private void LogStatsAudit(long durationMs)
    {
        var tokenId = User.FindFirst(AdminTokenAuthenticationHandler.TokenIdClaimType)?.Value
                      ?? "anonymous";
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        // Counts themselves are intentionally NOT logged — they are not
        // sensitive but the log line stays tighter and any future log
        // sink (S3, Datadog) does not retain a moving snapshot of system
        // size in plain text.
        _logger.LogInformation(
            "Admin listBusinessStats {@AdminBusinessStatsAudit}",
            new
            {
                Timestamp = DateTime.UtcNow,
                CallerTokenId = tokenId,
                CallerIp = ip,
                DurationMs = durationMs
            });
    }
}
