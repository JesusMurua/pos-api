using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POS.Domain.Models;
using POS.Services.IService;

namespace POS.API.Controllers;

/// <summary>
/// Controller for managing stock receipts (receiving merchandise).
/// </summary>
[Route("api/stock-receipt")]
[Authorize]
public class StockReceiptController : BaseApiController
{
    private readonly IStockReceiptService _stockReceiptService;

    public StockReceiptController(IStockReceiptService stockReceiptService)
    {
        _stockReceiptService = stockReceiptService;
    }

    /// <summary>
    /// Gets all stock receipts for the current branch with optional filters.
    /// </summary>
    /// <param name="supplierId">Optional supplier filter.</param>
    /// <param name="from">Optional start date filter.</param>
    /// <param name="to">Optional end date filter.</param>
    /// <returns>A list of stock receipts.</returns>
    /// <response code="200">Returns the list of stock receipts.</response>
    [HttpGet]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(typeof(IEnumerable<StockReceipt>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll(
        [FromQuery] int? supplierId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to)
    {
        var receipts = await _stockReceiptService.GetAllAsync(BranchId, supplierId, from, to);
        return Ok(receipts);
    }

    /// <summary>
    /// Gets a stock receipt by its identifier with all items.
    /// </summary>
    /// <param name="id">The stock receipt identifier.</param>
    /// <returns>The requested stock receipt with items.</returns>
    /// <response code="200">Returns the requested stock receipt.</response>
    /// <response code="404">If the receipt is not found.</response>
    [HttpGet("{id}")]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(typeof(StockReceipt), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(int id)
    {
        var receipt = await _stockReceiptService.GetByIdAsync(id, BranchId);
        return Ok(receipt);
    }

    /// <summary>
    /// Creates a stock receipt and processes all inventory movements in a single transaction.
    /// </summary>
    /// <param name="request">The stock receipt data with items.</param>
    /// <returns>The created stock receipt with items.</returns>
    /// <response code="200">Returns the created stock receipt.</response>
    /// <response code="400">If the request data is invalid.</response>
    [HttpPost]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(typeof(StockReceipt), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateStockReceiptRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var receipt = await _stockReceiptService.CreateAsync(request, BranchId, UserId);
        return Ok(receipt);
    }
}
