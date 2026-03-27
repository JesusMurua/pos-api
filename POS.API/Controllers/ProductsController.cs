using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POS.Domain.Models;
using POS.Services.IService;

namespace POS.API.Controllers;

/// <summary>
/// Controller for managing products.
/// </summary>
[Route("api/[controller]")]
[Authorize]
public class ProductsController : BaseApiController
{
    private readonly IProductService _productService;
    private readonly IProductImportService _productImportService;
    private readonly IInventoryService _inventoryService;

    public ProductsController(
        IProductService productService,
        IProductImportService productImportService,
        IInventoryService inventoryService)
    {
        _productService = productService;
        _productImportService = productImportService;
        _inventoryService = inventoryService;
    }

    /// <summary>
    /// Retrieves all active products for the current branch, including sizes and extras.
    /// </summary>
    /// <returns>A list of active products.</returns>
    /// <response code="200">Returns the list of active products.</response>
    [HttpGet]
    [Authorize(Roles = "Owner,Manager,Cashier,Kitchen,Waiter")]
    [ProducesResponseType(typeof(IEnumerable<Product>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll()
    {
        var products = await _productService.GetAllActiveAsync(BranchId);
        return Ok(products);
    }

    /// <summary>
    /// Retrieves all active and available products for a branch (public/kiosk access).
    /// </summary>
    /// <param name="branchId">The branch identifier.</param>
    /// <returns>A list of active and available products.</returns>
    /// <response code="200">Returns the list of products.</response>
    [HttpGet("public")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(IEnumerable<Product>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllPublic([FromQuery] int branchId)
    {
        var products = await _productService.GetAllActiveAsync(branchId);
        return Ok(products);
    }

    /// <summary>
    /// Retrieves a product by its identifier.
    /// </summary>
    /// <param name="id">The product identifier.</param>
    /// <returns>The requested product.</returns>
    /// <response code="200">Returns the requested product.</response>
    /// <response code="404">If the product is not found.</response>
    [HttpGet("{id}")]
    [Authorize(Roles = "Owner")]
    [ProducesResponseType(typeof(Product), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(int id)
    {
        var product = await _productService.GetByIdAsync(id);
        return Ok(product);
    }

    /// <summary>
    /// Creates a new product.
    /// </summary>
    /// <param name="product">The product data to create.</param>
    /// <returns>The created product.</returns>
    /// <response code="201">Returns the created product.</response>
    /// <response code="400">If the product data is invalid.</response>
    [HttpPost]
    [Authorize(Roles = "Owner")]
    [ProducesResponseType(typeof(Product), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] Product product)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var created = await _productService.CreateAsync(product);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    /// <summary>
    /// Updates an existing product.
    /// </summary>
    /// <param name="id">The product identifier.</param>
    /// <param name="product">The updated product data.</param>
    /// <returns>The updated product.</returns>
    /// <response code="200">Returns the updated product.</response>
    /// <response code="404">If the product is not found.</response>
    /// <response code="400">If the product data is invalid.</response>
    [HttpPut("{id}")]
    [Authorize(Roles = "Owner")]
    [ProducesResponseType(typeof(Product), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Update(int id, [FromBody] Product product)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var updated = await _productService.UpdateAsync(id, product);
        return Ok(updated);
    }

    /// <summary>
    /// Toggles the active/inactive status of a product.
    /// </summary>
    /// <param name="id">The product identifier.</param>
    /// <returns>The updated product.</returns>
    /// <response code="200">Returns the updated product.</response>
    /// <response code="404">If the product is not found.</response>
    [HttpPatch("{id}/toggle")]
    [Authorize(Roles = "Owner")]
    [ProducesResponseType(typeof(Product), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Toggle(int id)
    {
        var product = await _productService.ToggleActiveAsync(id);
        return Ok(product);
    }

    /// <summary>
    /// Updates stock for a product with TrackStock enabled.
    /// </summary>
    /// <param name="id">The product identifier.</param>
    /// <param name="request">Stock adjustment data.</param>
    /// <returns>The updated product.</returns>
    /// <response code="200">Returns the updated product.</response>
    /// <response code="400">If TrackStock is false or type is invalid.</response>
    /// <response code="404">If the product is not found.</response>
    [HttpPost("{id}/stock")]
    [Authorize(Roles = "Owner")]
    [ProducesResponseType(typeof(Product), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateStock(int id, [FromBody] UpdateStockRequest request)
    {
        var product = await _productService.GetByIdAsync(id);
        if (product == null)
            return NotFound(new { message = $"Product with id {id} not found" });

        if (!product.TrackStock)
            return BadRequest(new { message = "Product does not have stock tracking enabled" });

        var validTypes = new[] { "in", "out", "adjustment" };
        if (!validTypes.Contains(request.Type?.ToLowerInvariant()))
            return BadRequest(new { message = "Type must be 'in', 'out', or 'adjustment'" });

        switch (request.Type!.ToLowerInvariant())
        {
            case "in":
                product.CurrentStock += request.Quantity;
                break;
            case "out":
                product.CurrentStock -= request.Quantity;
                if (product.CurrentStock < 0) product.CurrentStock = 0;
                break;
            case "adjustment":
                product.CurrentStock = request.Quantity;
                break;
        }

        if (product.CurrentStock > product.LowStockThreshold)
            product.IsAvailable = true;
        if (product.CurrentStock <= 0)
            product.IsAvailable = false;

        await _productService.UpdateAsync(id, product);
        return Ok(product);
    }

    /// <summary>
    /// Gets inventory movements for a product with TrackStock enabled.
    /// </summary>
    /// <param name="id">The product identifier.</param>
    /// <returns>A list of inventory movements, or empty array if none.</returns>
    /// <response code="200">Returns the list of movements.</response>
    [HttpGet("{id}/movements")]
    [Authorize(Roles = "Owner")]
    [ProducesResponseType(typeof(IEnumerable<InventoryMovement>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMovements(int id)
    {
        var movements = await _inventoryService.GetProductMovementsAsync(id);
        return Ok(movements);
    }

    /// <summary>
    /// Downloads an Excel template for product import.
    /// </summary>
    /// <returns>An Excel file with headers and example data.</returns>
    /// <response code="200">Returns the Excel template file.</response>
    [HttpGet("import/template")]
    [Authorize(Roles = "Owner")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetImportTemplate()
    {
        var bytes = _productImportService.GenerateTemplate();
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "plantilla-productos.xlsx");
    }

    /// <summary>
    /// Previews products from an Excel file without saving to database.
    /// </summary>
    /// <param name="file">The Excel file to preview.</param>
    /// <returns>A preview with valid rows and validation errors.</returns>
    /// <response code="200">Returns the import preview.</response>
    /// <response code="400">If the file is missing or invalid.</response>
    [HttpPost("import/preview")]
    [Authorize(Roles = "Owner")]
    [ProducesResponseType(typeof(ProductImportPreview), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> PreviewImport(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "File is required" });

        if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "File must be .xlsx format" });

        var preview = await _productImportService.PreviewAsync(file.OpenReadStream(), BranchId);
        return Ok(preview);
    }

    /// <summary>
    /// Executes product import from previously validated rows.
    /// </summary>
    /// <param name="rows">The validated rows to import.</param>
    /// <returns>Import result with counts and warnings.</returns>
    /// <response code="200">Returns the import result.</response>
    /// <response code="400">If rows are missing or empty.</response>
    [HttpPost("import/execute")]
    [Authorize(Roles = "Owner")]
    [ProducesResponseType(typeof(ProductImportResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ExecuteImport([FromBody] List<ProductImportRow> rows)
    {
        if (rows == null || rows.Count == 0)
            return BadRequest(new { message = "No rows to import" });

        var result = await _productImportService.ImportAsync(rows, BranchId);
        return Ok(result);
    }
}

/// <summary>
/// Request body for updating product stock.
/// </summary>
public class UpdateStockRequest
{
    public string Type { get; set; } = null!;
    public decimal Quantity { get; set; }
    public string? Reason { get; set; }
}
