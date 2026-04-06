using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POS.Domain.Models;
using POS.Services.IService;

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
