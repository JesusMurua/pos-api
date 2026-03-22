using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POS.Domain.Models;
using POS.Services.IService;

namespace POS.API.Controllers;

/// <summary>
/// Controller for managing products.
/// </summary>
[Route("api/[controller]")]
[ApiController]
[Authorize(Roles = "Owner")]
public class ProductsController : ControllerBase
{
    private readonly IProductService _productService;
    private readonly IProductImportService _productImportService;

    public ProductsController(IProductService productService, IProductImportService productImportService)
    {
        _productService = productService;
        _productImportService = productImportService;
    }

    /// <summary>
    /// Retrieves all active products for a branch, including sizes and extras.
    /// </summary>
    /// <param name="branchId">The branch identifier.</param>
    /// <returns>A list of active products.</returns>
    /// <response code="200">Returns the list of active products.</response>
    [HttpGet]
    [Authorize(Roles = "Owner,Cashier")]
    [ProducesResponseType(typeof(IEnumerable<Product>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll([FromQuery] int branchId)
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
    [ProducesResponseType(typeof(Product), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Toggle(int id)
    {
        var product = await _productService.ToggleActiveAsync(id);
        return Ok(product);
    }

    /// <summary>
    /// Downloads an Excel template for product import.
    /// </summary>
    /// <returns>An Excel file with headers and example data.</returns>
    /// <response code="200">Returns the Excel template file.</response>
    [HttpGet("import/template")]
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
    /// <param name="branchId">The branch to import products to.</param>
    /// <param name="file">The Excel file to preview.</param>
    /// <returns>A preview with valid rows and validation errors.</returns>
    /// <response code="200">Returns the import preview.</response>
    /// <response code="400">If the file is missing or invalid.</response>
    [HttpPost("import/preview")]
    [ProducesResponseType(typeof(ProductImportPreview), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> PreviewImport([FromQuery] int branchId, IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "File is required" });

        if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "File must be .xlsx format" });

        var preview = await _productImportService.PreviewAsync(file.OpenReadStream(), branchId);
        return Ok(preview);
    }

    /// <summary>
    /// Executes product import from previously validated rows.
    /// </summary>
    /// <param name="branchId">The branch to import products to.</param>
    /// <param name="rows">The validated rows to import.</param>
    /// <returns>Import result with counts and warnings.</returns>
    /// <response code="200">Returns the import result.</response>
    /// <response code="400">If rows are missing or empty.</response>
    [HttpPost("import/execute")]
    [ProducesResponseType(typeof(ProductImportResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ExecuteImport(
        [FromQuery] int branchId,
        [FromBody] List<ProductImportRow> rows)
    {
        if (rows == null || rows.Count == 0)
            return BadRequest(new { message = "No rows to import" });

        var result = await _productImportService.ImportAsync(rows, branchId);
        return Ok(result);
    }
}
