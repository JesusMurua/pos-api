using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POS.API.Models;
using POS.Domain.Models;
using POS.Services.IService;

namespace POS.API.Controllers;

/// <summary>
/// Controller for managing businesses.
/// </summary>
[Route("api/[controller]")]
[Authorize]
public class BusinessController : BaseApiController
{
    private readonly IBusinessService _businessService;
    private readonly IAuthService _authService;
    private readonly IFeatureGateService _featureGate;

    public BusinessController(
        IBusinessService businessService,
        IAuthService authService,
        IFeatureGateService featureGate)
    {
        _businessService = businessService;
        _authService = authService;
        _featureGate = featureGate;
    }

    /// <summary>
    /// Returns the list of feature codes enabled for the caller's business.
    /// The frontend uses this to hide or disable UI elements up-front instead of
    /// probing each feature individually.
    /// </summary>
    /// <returns>A flat array of feature codes (e.g. "CoreHardware", "RealtimeKds").</returns>
    /// <response code="200">Returns the enabled feature codes.</response>
    [HttpGet("features")]
    [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetFeatures()
    {
        var features = await _featureGate.GetEnabledFeaturesAsync(BusinessId);
        return Ok(features);
    }

    /// <summary>
    /// Retrieves a business by its identifier with branches.
    /// </summary>
    /// <param name="id">The business identifier.</param>
    /// <returns>The requested business.</returns>
    /// <response code="200">Returns the requested business.</response>
    /// <response code="404">If the business is not found.</response>
    [HttpGet("{id}")]
    [Authorize(Roles = "Owner")]
    [ProducesResponseType(typeof(Business), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(int id)
    {
        var business = await _businessService.GetByIdAsync(id);
        return Ok(business);
    }

    /// <summary>
    /// Creates a new business.
    /// </summary>
    /// <param name="request">The business data to create.</param>
    /// <returns>The created business.</returns>
    /// <response code="201">Returns the created business.</response>
    /// <response code="400">If the request data is invalid.</response>
    [HttpPost]
    [Authorize(Roles = "Owner")]
    [ProducesResponseType(typeof(Business), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateBusinessRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var business = new Business
        {
            Name = request.Name,
            PlanTypeId = request.PlanTypeId
        };

        var created = await _businessService.CreateAsync(business, UserId);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    /// <summary>
    /// Returns the current giro configuration (macro, sub-giro set, custom description)
    /// so the frontend can rehydrate onboarding state after a reload or back navigation.
    /// </summary>
    /// <returns>The current <see cref="BusinessGiroResponse"/> for the authenticated business.</returns>
    /// <response code="200">Returns the current giro configuration.</response>
    /// <response code="404">If the business is not found.</response>
    [HttpGet("giro")]
    [Authorize(Roles = "Owner")]
    [ProducesResponseType(typeof(BusinessGiroResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetGiro()
    {
        var giro = await _businessService.GetGiroAsync(BusinessId);
        return Ok(giro);
    }

    /// <summary>
    /// Replaces the business's macro category, sub-giro set and optional custom description.
    /// </summary>
    /// <param name="request">Macro category, list of sub-giro ids and optional description.</param>
    /// <returns>Resolved macro category and sub-giro identifiers.</returns>
    /// <response code="200">Giro configuration updated.</response>
    /// <response code="400">If validation fails.</response>
    [HttpPut("giro")]
    [Authorize(Roles = "Owner")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateGiro([FromBody] UpdateBusinessGiroRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        if (request.PrimaryMacroCategoryId <= 0)
            return BadRequest(new { message = "PrimaryMacroCategoryId inválido" });

        if (request.BusinessTypeIds == null || request.BusinessTypeIds.Count == 0)
            return BadRequest(new { message = "Debe seleccionar al menos un giro" });

        var updated = await _businessService.UpdateGiroAsync(
            BusinessId,
            request.PrimaryMacroCategoryId,
            request.BusinessTypeIds,
            request.CustomGiroDescription);

        return Ok(new
        {
            message = "Giro configuration updated",
            primaryMacroCategoryId = updated.PrimaryMacroCategoryId,
            businessTypeIds = request.BusinessTypeIds,
            customGiroDescription = updated.CustomGiroDescription
        });
    }

    /// <summary>
    /// Updates fiscal configuration for electronic invoicing (CFDI).
    /// </summary>
    /// <param name="request">The fiscal configuration data.</param>
    /// <returns>Success acknowledgement.</returns>
    /// <response code="200">Fiscal configuration updated.</response>
    /// <response code="400">If the data is invalid.</response>
    [HttpPut("fiscal")]
    [Authorize(Roles = "Owner")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateFiscalConfig([FromBody] UpdateFiscalConfigRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var business = await _businessService.GetByIdAsync(BusinessId);

        business.Rfc = request.Rfc?.Trim().ToUpperInvariant();
        business.TaxRegime = request.TaxRegime;
        business.LegalName = request.LegalName;
        business.InvoicingEnabled = request.InvoicingEnabled;

        await _businessService.UpdateAsync(business);

        return Ok(new
        {
            message = "Fiscal configuration updated",
            rfc = business.Rfc,
            taxRegime = business.TaxRegime,
            legalName = business.LegalName,
            invoicingEnabled = business.InvoicingEnabled
        });
    }

    /// <summary>
    /// Gets the fiscal configuration for the current business.
    /// </summary>
    /// <returns>Fiscal configuration data.</returns>
    /// <response code="200">Returns the fiscal configuration.</response>
    [HttpGet("fiscal")]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetFiscalConfig()
    {
        var business = await _businessService.GetByIdAsync(BusinessId);

        return Ok(new
        {
            rfc = business.Rfc,
            taxRegime = business.TaxRegime,
            legalName = business.LegalName,
            invoicingEnabled = business.InvoicingEnabled,
            facturapiOrganizationId = business.FacturapiOrganizationId
        });
    }

    /// <summary>
    /// Marks the business onboarding as completed and returns a fresh JWT.
    /// </summary>
    /// <returns>A new JWT token with updated onboardingCompleted claim.</returns>
    /// <response code="200">Returns a fresh JWT token.</response>
    [HttpPost("complete-onboarding")]
    [Authorize(Roles = "Owner")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> CompleteOnboarding()
    {
        var business = await _businessService.GetByIdAsync(BusinessId);
        business.OnboardingCompleted = true;
        business.OnboardingStatusId = 3;
        await _businessService.UpdateAsync(business);

        var sessionType = User.FindFirst("sessionType")?.Value;
        var response = await _authService.SwitchBranchAsync(UserId, BranchId, sessionType);
        return Ok(response);
    }

    /// <summary>
    /// Updates the onboarding status and current step for the business.
    /// </summary>
    /// <param name="request">The onboarding status and step data.</param>
    /// <returns>Updated onboarding state.</returns>
    /// <response code="200">Onboarding status updated.</response>
    /// <response code="400">If the statusId is invalid.</response>
    [HttpPut("onboarding-step")]
    [Authorize(Roles = "Owner")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateOnboardingStep([FromBody] UpdateOnboardingStepRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        if (request.StatusId < 1 || request.StatusId > 4)
            return BadRequest(new { message = "Invalid statusId. Must be 1-4." });

        var business = await _businessService.GetByIdAsync(BusinessId);
        business.OnboardingStatusId = request.StatusId;
        business.CurrentOnboardingStep = request.Step;

        if (request.StatusId == 3)
            business.OnboardingCompleted = true;

        await _businessService.UpdateAsync(business);

        return Ok(new
        {
            onboardingStatusId = business.OnboardingStatusId,
            currentOnboardingStep = business.CurrentOnboardingStep,
            onboardingCompleted = business.OnboardingCompleted
        });
    }
}

/// <summary>
/// Request body for replacing the business's macro category and sub-giro set.
/// </summary>
public class UpdateBusinessGiroRequest
{
    /// <summary>Target <see cref="POS.Domain.Models.Catalogs.MacroCategory"/>.Id that will drive POS/plan rules.</summary>
    [System.ComponentModel.DataAnnotations.Required]
    public int PrimaryMacroCategoryId { get; set; }

    /// <summary>Full replacement set of sub-giro ids (BusinessTypeCatalog.Id). Must contain at least one.</summary>
    [System.ComponentModel.DataAnnotations.Required]
    [System.ComponentModel.DataAnnotations.MinLength(1)]
    public List<int> BusinessTypeIds { get; set; } = new();

    /// <summary>Free-text clarification when the user picks "Otro" or a non-catalog giro.</summary>
    [System.ComponentModel.DataAnnotations.MaxLength(100)]
    public string? CustomGiroDescription { get; set; }
}

/// <summary>
/// Request body for updating fiscal configuration.
/// </summary>
public class UpdateFiscalConfigRequest
{
    /// <summary>RFC of the business (tax ID). 12 or 13 characters.</summary>
    [System.ComponentModel.DataAnnotations.MaxLength(13)]
    public string? Rfc { get; set; }

    /// <summary>SAT tax regime code (e.g., "601", "612").</summary>
    [System.ComponentModel.DataAnnotations.MaxLength(3)]
    public string? TaxRegime { get; set; }

    /// <summary>Legal name exactly as registered with SAT.</summary>
    [System.ComponentModel.DataAnnotations.MaxLength(300)]
    public string? LegalName { get; set; }

    /// <summary>Whether electronic invoicing is enabled.</summary>
    public bool InvoicingEnabled { get; set; }
}

/// <summary>
/// Request body for updating onboarding status and step.
/// </summary>
public class UpdateOnboardingStepRequest
{
    /// <summary>Onboarding status catalog Id (1=Pending, 2=InProgress, 3=Completed, 4=Skipped).</summary>
    [System.ComponentModel.DataAnnotations.Required]
    public int StatusId { get; set; }

    /// <summary>Current onboarding step (1-based).</summary>
    [System.ComponentModel.DataAnnotations.Required]
    [System.ComponentModel.DataAnnotations.Range(1, 100)]
    public int Step { get; set; }
}
