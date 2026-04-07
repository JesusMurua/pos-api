using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using POS.Domain.Exceptions;
using POS.Domain.Models;
using POS.Repository;

namespace POS.API.Controllers;

/// <summary>
/// Controller for managing payment provider configurations per branch.
/// </summary>
[Route("api/payment-configs")]
public class BranchPaymentConfigController : BaseApiController
{
    private readonly IUnitOfWork _unitOfWork;

    public BranchPaymentConfigController(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    /// <summary>
    /// Gets all payment provider configs for the current branch.
    /// </summary>
    [HttpGet]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(typeof(IEnumerable<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetByBranch()
    {
        var configs = await _unitOfWork.BranchPaymentConfigs.GetByBranchAsync(BranchId);

        var dtos = configs.Select(c => new
        {
            c.Id,
            c.Provider,
            c.TerminalId,
            c.IsActive,
            HasAccessToken = !string.IsNullOrEmpty(c.AccessToken),
            HasWebhookSecret = !string.IsNullOrEmpty(c.WebhookSecret),
            c.CreatedAt,
            c.UpdatedAt
        });

        return Ok(dtos);
    }

    /// <summary>
    /// Creates a new payment provider config for the current branch.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] BranchPaymentConfigRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var config = new BranchPaymentConfig
        {
            BranchId = BranchId,
            Provider = request.Provider.ToLowerInvariant(),
            AccessToken = request.AccessToken,
            WebhookSecret = request.WebhookSecret,
            TerminalId = request.TerminalId,
            IsActive = request.IsActive
        };

        await _unitOfWork.BranchPaymentConfigs.AddAsync(config);

        try
        {
            await _unitOfWork.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            throw new ValidationException(
                $"A payment config for provider '{request.Provider}' already exists for this branch.");
        }

        return Ok(new
        {
            config.Id,
            config.Provider,
            config.TerminalId,
            config.IsActive,
            HasAccessToken = true,
            HasWebhookSecret = !string.IsNullOrEmpty(config.WebhookSecret),
            config.CreatedAt
        });
    }

    /// <summary>
    /// Updates an existing payment provider config.
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Update(int id, [FromBody] BranchPaymentConfigRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var config = await _unitOfWork.BranchPaymentConfigs.GetByIdAsync(id)
            ?? throw new NotFoundException($"Payment config with id {id} not found");

        if (config.BranchId != BranchId)
            throw new UnauthorizedException("Payment config does not belong to this branch");

        config.Provider = request.Provider.ToLowerInvariant();
        config.AccessToken = request.AccessToken;
        config.WebhookSecret = request.WebhookSecret;
        config.TerminalId = request.TerminalId;
        config.IsActive = request.IsActive;
        config.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.BranchPaymentConfigs.Update(config);

        try
        {
            await _unitOfWork.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            throw new ValidationException(
                $"A payment config for provider '{request.Provider}' already exists for this branch.");
        }

        return Ok(new
        {
            config.Id,
            config.Provider,
            config.TerminalId,
            config.IsActive,
            HasAccessToken = true,
            HasWebhookSecret = !string.IsNullOrEmpty(config.WebhookSecret),
            config.CreatedAt,
            config.UpdatedAt
        });
    }
}
