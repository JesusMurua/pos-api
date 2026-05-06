using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POS.Domain.DTOs.Customer;
using POS.Domain.Models;
using POS.Repository.Utils;
using POS.Services.IService;
using NotFoundException = POS.Domain.Exceptions.NotFoundException;
using ValidationException = POS.Domain.Exceptions.ValidationException;

namespace POS.API.Controllers;

/// <summary>
/// Controller for managing CRM customers, store credit (fiado), and loyalty points.
/// </summary>
[Route("api/[controller]")]
[Authorize]
public class CustomersController : BaseApiController
{
    private readonly ICustomerService _customerService;

    public CustomersController(ICustomerService customerService)
    {
        _customerService = customerService;
    }

    /// <summary>
    /// Gets all active customers for the current business.
    /// </summary>
    /// <returns>A list of active customers.</returns>
    /// <response code="200">Returns the list of customers.</response>
    [HttpGet]
    [Authorize(Roles = "Owner,Manager,Cashier")]
    [ProducesResponseType(typeof(IEnumerable<Customer>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll()
    {
        var customers = await _customerService.GetByBusinessAsync(BusinessId);
        return Ok(customers);
    }

    /// <summary>
    /// Searches customers by name, phone, or email.
    /// </summary>
    /// <param name="q">The search query.</param>
    /// <returns>Matching customers (max 20).</returns>
    /// <response code="200">Returns matching customers.</response>
    [HttpGet("search")]
    [Authorize(Roles = "Owner,Manager,Cashier")]
    [ProducesResponseType(typeof(IEnumerable<Customer>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Search([FromQuery] string q)
    {
        var customers = await _customerService.SearchAsync(BusinessId, q);
        return Ok(customers);
    }

    /// <summary>
    /// Gets a customer by ID with balance details.
    /// </summary>
    /// <param name="id">The customer identifier.</param>
    /// <returns>The customer.</returns>
    /// <response code="200">Returns the customer.</response>
    /// <response code="404">If the customer is not found.</response>
    [HttpGet("{id}")]
    [Authorize(Roles = "Owner,Manager,Cashier")]
    [ProducesResponseType(typeof(Customer), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(int id)
    {
        var customer = await _customerService.GetByIdAsync(id);
        return Ok(customer);
    }

    /// <summary>
    /// Creates a new customer.
    /// </summary>
    /// <param name="request">The customer data.</param>
    /// <returns>The created customer.</returns>
    /// <response code="201">Returns the created customer.</response>
    /// <response code="400">If validation fails (e.g., duplicate phone).</response>
    [HttpPost]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(typeof(Customer), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateCustomerRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var customer = new Customer
        {
            FirstName = request.FirstName,
            LastName = request.LastName,
            Phone = request.Phone,
            Email = request.Email,
            CreditLimitCents = request.CreditLimitCents,
            Notes = request.Notes
        };

        var created = await _customerService.CreateAsync(BusinessId, customer);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    /// <summary>
    /// Updates an existing customer's profile data.
    /// </summary>
    /// <param name="id">The customer identifier.</param>
    /// <param name="request">The updated data.</param>
    /// <returns>The updated customer.</returns>
    /// <response code="200">Returns the updated customer.</response>
    /// <response code="404">If the customer is not found.</response>
    [HttpPut("{id}")]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(typeof(Customer), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateCustomerRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var customer = new Customer
        {
            FirstName = request.FirstName,
            LastName = request.LastName,
            Phone = request.Phone,
            Email = request.Email,
            CreditLimitCents = request.CreditLimitCents,
            Notes = request.Notes
        };

        var updated = await _customerService.UpdateAsync(id, customer);
        return Ok(updated);
    }

    /// <summary>
    /// Deactivates a customer (soft delete).
    /// </summary>
    /// <param name="id">The customer identifier.</param>
    /// <returns>Success acknowledgement.</returns>
    /// <response code="200">Customer deactivated.</response>
    /// <response code="404">If the customer is not found.</response>
    [HttpDelete("{id}")]
    [Authorize(Roles = "Owner")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Deactivate(int id)
    {
        await _customerService.DeactivateAsync(id);
        return Ok(new { success = true });
    }

    /// <summary>
    /// Adds credit to a customer's balance (customer pays down their tab / fiado).
    /// </summary>
    /// <param name="id">The customer identifier.</param>
    /// <param name="request">The credit amount and description.</param>
    /// <returns>The ledger transaction entry.</returns>
    /// <response code="200">Returns the ledger entry.</response>
    /// <response code="400">If the amount is invalid.</response>
    [HttpPost("{id}/credit/add")]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(typeof(CustomerTransaction), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AddCredit(int id, [FromBody] CreditRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var userName = User.Identity?.Name ?? "Unknown";
        var transaction = await _customerService.AddCreditAsync(
            id, request.AmountCents, request.Description, BranchId, userName);
        return Ok(transaction);
    }

    /// <summary>
    /// Manual adjustment of credit balance by owner/manager.
    /// </summary>
    /// <param name="id">The customer identifier.</param>
    /// <param name="request">The adjustment amount (positive or negative) and description.</param>
    /// <returns>The ledger transaction entry.</returns>
    /// <response code="200">Returns the ledger entry.</response>
    [HttpPost("{id}/credit/adjust")]
    [Authorize(Roles = "Owner")]
    [ProducesResponseType(typeof(CustomerTransaction), StatusCodes.Status200OK)]
    public async Task<IActionResult> AdjustCredit(int id, [FromBody] CreditRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var userName = User.Identity?.Name ?? "Unknown";
        var transaction = await _customerService.AdjustCreditAsync(
            id, request.AmountCents, request.Description, BranchId, userName);
        return Ok(transaction);
    }

    /// <summary>
    /// Manual adjustment of loyalty points by owner.
    /// </summary>
    /// <param name="id">The customer identifier.</param>
    /// <param name="request">The points amount (positive or negative) and description.</param>
    /// <returns>The ledger transaction entry.</returns>
    /// <response code="200">Returns the ledger entry.</response>
    [HttpPost("{id}/points/adjust")]
    [Authorize(Roles = "Owner")]
    [ProducesResponseType(typeof(CustomerTransaction), StatusCodes.Status200OK)]
    public async Task<IActionResult> AdjustPoints(int id, [FromBody] PointsRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var userName = User.Identity?.Name ?? "Unknown";
        var transaction = await _customerService.AdjustPointsAsync(
            id, request.PointsAmount, request.Description, BranchId, userName);
        return Ok(transaction);
    }

    /// <summary>
    /// Gets transaction history (credit + points ledger) for a customer.
    /// </summary>
    /// <param name="id">The customer identifier.</param>
    /// <param name="from">Optional start date filter.</param>
    /// <param name="to">Optional end date filter.</param>
    /// <returns>List of ledger transactions.</returns>
    /// <response code="200">Returns the transaction history.</response>
    [HttpGet("{id}/transactions")]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(typeof(IEnumerable<CustomerTransaction>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTransactions(int id, [FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var transactions = await _customerService.GetTransactionsAsync(id, from, to);
        return Ok(transactions);
    }

    /// <summary>
    /// Links a CRM customer to an existing fiscal customer for invoicing.
    /// Both must belong to the same business.
    /// </summary>
    /// <param name="id">The customer identifier.</param>
    /// <param name="request">The fiscal customer to link.</param>
    /// <returns>Success acknowledgement.</returns>
    /// <response code="200">Link created successfully.</response>
    /// <response code="400">If entities belong to different businesses.</response>
    /// <response code="404">If customer or fiscal customer is not found.</response>
    [HttpPost("{id}/link-fiscal")]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> LinkFiscalCustomer(int id, [FromBody] LinkFiscalCustomerRequest request)
    {
        await _customerService.LinkFiscalCustomerAsync(id, request.FiscalCustomerId);
        return Ok(new { success = true });
    }

    /// <summary>
    /// Recalculates customer balances from the transaction ledger (reconciliation).
    /// </summary>
    /// <param name="id">The customer identifier.</param>
    /// <returns>Success acknowledgement.</returns>
    /// <response code="200">Balances recalculated.</response>
    /// <response code="404">If the customer is not found.</response>
    [HttpPost("{id}/recalculate")]
    [Authorize(Roles = "Owner")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RecalculateBalances(int id)
    {
        await _customerService.RecalculateBalancesAsync(id);
        return Ok(new { success = true });
    }

    #region BDD-019 P4 — Customer-scoped read endpoints

    /// <summary>
    /// Returns paginated orders for the given customer scoped to the caller's
    /// tenant. Pure SQL projection — no entity hydration, no JSON columns
    /// loaded. Sorted by <c>CreatedAt</c> descending (most recent first).
    /// </summary>
    /// <param name="id">Customer identifier.</param>
    /// <param name="page">1-based page index. Default 1. Values &lt; 1 produce 400.</param>
    /// <param name="pageSize">Page size. Must be in [1, 100]; values &gt; 100 are silently capped at 100.</param>
    /// <param name="from">Optional inclusive lower bound on <c>CreatedAt</c> (UTC).</param>
    /// <param name="to">Optional inclusive upper bound on <c>CreatedAt</c> (UTC). Must satisfy <c>from &lt;= to</c>.</param>
    /// <response code="200">Returns the paginated order rows.</response>
    /// <response code="400">Invalid <c>page</c>/<c>pageSize</c> (<c>INVALID_PAGE_SIZE</c>) or invalid date range (<c>INVALID_DATE_RANGE</c>).</response>
    /// <response code="404">Returned if the customer does not exist OR if the customer belongs to a different tenant (Information Hiding — prevents enumeration).</response>
    [HttpGet("{id}/orders")]
    [Authorize(Roles = "Owner,Manager,Cashier")]
    [ProducesResponseType(typeof(PageData<CustomerOrderRowDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetOrders(
        int id,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        try
        {
            // Validate query params first (cheap, fail-fast).
            if (page < 1)
                throw new ValidationException("INVALID_PAGE_SIZE: page must be >= 1.");
            if (pageSize < 1)
                throw new ValidationException("INVALID_PAGE_SIZE: pageSize must be >= 1.");
            if (pageSize > 100) pageSize = 100;
            if (from.HasValue && to.HasValue && from.Value > to.Value)
                throw new ValidationException("INVALID_DATE_RANGE: 'from' must be <= 'to'.");

            var result = await _customerService.GetOrdersAsync(id, BusinessId, page, pageSize, from, to);
            return Ok(result);
        }
        catch (NotFoundException ex) { return NotFound(ex.Message); }
        catch (ValidationException ex) { return BadRequest(ex.Message); }
        catch (Exception ex) { return StatusCode(500, ex.Message); }
    }

    /// <summary>
    /// Returns the customer's memberships sorted by <c>ValidUntil</c> desc.
    /// Lazy-Expired rows (DB <c>Status = Active</c> but <c>ValidUntil &lt; UtcNow</c>)
    /// surface with <c>Status = "Expired"</c> in the projection per BDD-019 §6.1.2,
    /// and the optional <paramref name="status"/> filter respects that semantic.
    /// </summary>
    /// <remarks>
    /// Compound filter logic when <paramref name="status"/> is supplied:
    /// <list type="bullet">
    ///   <item><c>Active</c> → only rows with stored <c>Status = Active</c> AND <c>ValidUntil &gt;= UtcNow</c> (excludes lazy-Expired).</item>
    ///   <item><c>Expired</c> → rows with stored <c>Status = Expired</c> OR (<c>Status = Active</c> AND <c>ValidUntil &lt; UtcNow</c>) (includes lazy-Expired).</item>
    ///   <item><c>Frozen</c> → rows with stored <c>Status = Frozen</c>.</item>
    ///   <item><c>Cancelled</c> → rows with stored <c>Status = Cancelled</c>.</item>
    /// </list>
    /// Null / empty / whitespace returns all memberships unfiltered.
    /// </remarks>
    /// <param name="id">Customer identifier.</param>
    /// <param name="status">Optional filter: <c>Active</c>, <c>Expired</c>, <c>Frozen</c>, <c>Cancelled</c> (case-insensitive).</param>
    /// <response code="200">Returns the membership list (may be empty).</response>
    /// <response code="400">Unknown <paramref name="status"/> value (<c>INVALID_STATUS</c>).</response>
    /// <response code="404">Returned if the customer does not exist OR if the customer belongs to a different tenant (Information Hiding — prevents enumeration).</response>
    [HttpGet("{id}/memberships")]
    [Authorize(Roles = "Owner,Manager,Cashier")]
    [ProducesResponseType(typeof(IEnumerable<CustomerMembershipDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMemberships(int id, [FromQuery] string? status = null)
    {
        try
        {
            var result = await _customerService.GetMembershipsAsync(id, BusinessId, status);
            return Ok(result);
        }
        catch (NotFoundException ex) { return NotFound(ex.Message); }
        catch (ValidationException ex) { return BadRequest(ex.Message); }
        catch (Exception ex) { return StatusCode(500, ex.Message); }
    }

    /// <summary>
    /// Returns aggregate stats (<c>TotalSpentCents</c>, <c>OrderCount</c>,
    /// <c>LastOrderAt</c>) for the given customer. Computed via a single
    /// DB-level aggregation (SUM + COUNT + MAX) over paid, non-cancelled orders.
    /// Returns zeros / null for customers without qualifying orders.
    /// </summary>
    /// <param name="id">Customer identifier.</param>
    /// <response code="200">Returns the aggregated stats.</response>
    /// <response code="404">Returned if the customer does not exist OR if the customer belongs to a different tenant (Information Hiding — prevents enumeration).</response>
    [HttpGet("{id}/stats")]
    [Authorize(Roles = "Owner,Manager,Cashier")]
    [ProducesResponseType(typeof(CustomerStatsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStats(int id)
    {
        try
        {
            var result = await _customerService.GetStatsAsync(id, BusinessId);
            return Ok(result);
        }
        catch (NotFoundException ex) { return NotFound(ex.Message); }
        catch (Exception ex) { return StatusCode(500, ex.Message); }
    }

    #endregion
}

/// <summary>
/// Request body for creating a customer.
/// </summary>
public class CreateCustomerRequest
{
    [Required]
    [MaxLength(100)]
    public string FirstName { get; set; } = null!;

    [MaxLength(100)]
    public string? LastName { get; set; }

    [MaxLength(20)]
    public string? Phone { get; set; }

    [MaxLength(255)]
    public string? Email { get; set; }

    /// <summary>Maximum credit limit in cents. 0 = no limit.</summary>
    public int CreditLimitCents { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }
}

/// <summary>
/// Request body for updating a customer.
/// </summary>
public class UpdateCustomerRequest
{
    [Required]
    [MaxLength(100)]
    public string FirstName { get; set; } = null!;

    [MaxLength(100)]
    public string? LastName { get; set; }

    [MaxLength(20)]
    public string? Phone { get; set; }

    [MaxLength(255)]
    public string? Email { get; set; }

    public int CreditLimitCents { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }
}

/// <summary>
/// Request body for credit operations.
/// </summary>
public class CreditRequest
{
    /// <summary>Amount in cents. Positive for add, positive or negative for adjust.</summary>
    public int AmountCents { get; set; }

    /// <summary>Description of the transaction.</summary>
    [Required]
    [MaxLength(200)]
    public string Description { get; set; } = null!;
}

/// <summary>
/// Request body for points operations.
/// </summary>
public class PointsRequest
{
    /// <summary>Points amount. Positive or negative for adjust.</summary>
    public int PointsAmount { get; set; }

    /// <summary>Description of the transaction.</summary>
    [Required]
    [MaxLength(200)]
    public string Description { get; set; } = null!;
}

/// <summary>
/// Request body for linking a CRM customer to a fiscal customer.
/// </summary>
public class LinkFiscalCustomerRequest
{
    /// <summary>The fiscal customer ID to link.</summary>
    [Required]
    public int FiscalCustomerId { get; set; }
}
