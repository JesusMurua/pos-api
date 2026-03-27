using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POS.Domain.Exceptions;

namespace POS.API.Controllers;

/// <summary>
/// Base controller for all authenticated API endpoints.
/// Extracts common JWT claims (BranchId, BusinessId, UserId, UserRole)
/// so derived controllers don't need to receive them as query parameters.
/// </summary>
[ApiController]
[Authorize]
public abstract class BaseApiController : ControllerBase
{
    /// <summary>
    /// Gets the branch identifier from the JWT "branchId" claim.
    /// </summary>
    /// <exception cref="UnauthorizedException">Thrown when the branchId claim is missing or invalid.</exception>
    protected int BranchId
    {
        get
        {
            var claim = User.FindFirst("branchId");
            if (claim == null || !int.TryParse(claim.Value, out var branchId))
                throw new UnauthorizedException("Missing or invalid branchId claim in token.");

            return branchId;
        }
    }

    /// <summary>
    /// Gets the business identifier from the JWT "businessId" claim.
    /// </summary>
    /// <exception cref="UnauthorizedException">Thrown when the businessId claim is missing or invalid.</exception>
    protected int BusinessId
    {
        get
        {
            var claim = User.FindFirst("businessId");
            if (claim == null || !int.TryParse(claim.Value, out var businessId))
                throw new UnauthorizedException("Missing or invalid businessId claim in token.");

            return businessId;
        }
    }

    /// <summary>
    /// Gets the user identifier from the JWT NameIdentifier claim.
    /// </summary>
    /// <exception cref="UnauthorizedException">Thrown when the NameIdentifier claim is missing or invalid.</exception>
    protected int UserId
    {
        get
        {
            var claim = User.FindFirst(ClaimTypes.NameIdentifier);
            if (claim == null || !int.TryParse(claim.Value, out var userId))
                throw new UnauthorizedException("Missing or invalid UserId claim in token.");

            return userId;
        }
    }

    /// <summary>
    /// Gets the user role from the JWT Role claim.
    /// </summary>
    /// <exception cref="UnauthorizedException">Thrown when the Role claim is missing.</exception>
    protected string UserRole
    {
        get
        {
            var claim = User.FindFirst(ClaimTypes.Role);
            if (claim == null || string.IsNullOrWhiteSpace(claim.Value))
                throw new UnauthorizedException("Missing or invalid Role claim in token.");

            return claim.Value;
        }
    }
}
