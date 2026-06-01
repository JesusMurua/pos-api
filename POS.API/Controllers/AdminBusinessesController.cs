using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using POS.API.Auth;
using POS.Domain.DTOs.Admin;
using POS.Domain.DTOs.Common;
using POS.Domain.Exceptions;
using POS.Domain.Helpers;
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
    private readonly ILogger<AdminBusinessesController> _logger;

    public AdminBusinessesController(
        IAuthService authService,
        IUnitOfWork unitOfWork,
        ILogger<AdminBusinessesController> logger)
    {
        _authService = authService;
        _unitOfWork = unitOfWork;
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
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        var clampedPageNumber = Math.Max(1, pageNumber);
        var clampedPageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var (rows, total) = await _unitOfWork.Business.GetAllForAdminAsync(
            clampedPageNumber, clampedPageSize, search, cancellationToken);

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
