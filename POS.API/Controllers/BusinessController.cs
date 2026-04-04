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

    public BusinessController(IBusinessService businessService, IAuthService authService)
    {
        _businessService = businessService;
        _authService = authService;
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

        if (!Enum.TryParse<POS.Domain.Enums.PlanType>(request.PlanType, true, out var planType))
            planType = POS.Domain.Enums.PlanType.Free;

        var business = new Business
        {
            Name = request.Name,
            PlanType = planType
        };

        var created = await _businessService.CreateAsync(business, UserId);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    /// <summary>
    /// Updates the business type (giro).
    /// </summary>
    /// <param name="request">The new business type.</param>
    /// <returns>Success acknowledgement.</returns>
    /// <response code="200">Business type updated.</response>
    /// <response code="400">If the type is invalid.</response>
    [HttpPut("type")]
    [Authorize(Roles = "Owner")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateType([FromBody] UpdateBusinessTypeRequest request)
    {
        if (!Enum.TryParse<POS.Domain.Enums.BusinessType>(request.BusinessType, true, out var businessType))
            return BadRequest(new { message = "Invalid business type" });

        var business = await _businessService.GetByIdAsync(BusinessId);
        business.BusinessType = businessType;
        await _businessService.UpdateAsync(business);
        return Ok(new { message = "Business type updated", businessType = businessType.ToString() });
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
        await _businessService.UpdateAsync(business);

        var response = await _authService.SwitchBranchAsync(UserId, BranchId);
        return Ok(response);
    }
}

/// <summary>
/// Request body for updating business type.
/// </summary>
public class UpdateBusinessTypeRequest
{
    public string BusinessType { get; set; } = null!;
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
