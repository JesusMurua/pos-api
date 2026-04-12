using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POS.Domain.DTOs.Product;
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
    private readonly IStorageService _storageService;

    public ProductsController(
        IProductService productService,
        IProductImportService productImportService,
        IInventoryService inventoryService,
        IStorageService storageService)
    {
        _productService = productService;
        _productImportService = productImportService;
        _inventoryService = inventoryService;
        _storageService = storageService;
    }

    /// <summary>
    /// Retrieves all active products for the current branch, including sizes and extras.
    /// </summary>
    /// <returns>A list of active products.</returns>
    /// <response code="200">Returns the list of active products.</response>
    [HttpGet]
    [Authorize(Roles = "Owner,Manager,Cashier,Kitchen,Waiter")]
    [ProducesResponseType(typeof(IEnumerable<ProductResponse>), StatusCodes.Status200OK)]
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
    [ProducesResponseType(typeof(IEnumerable<ProductResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllPublic([FromQuery] int branchId)
    {
        var products = await _productService.GetAllActiveAsync(branchId);
        return Ok(products);
    }

    /// <summary>
    /// Gets a product by barcode. BranchId from JWT.
    /// </summary>
    /// <param name="code">The barcode string to search.</param>
    /// <returns>The matching product or 404.</returns>
    /// <response code="200">Returns the matching product.</response>
    /// <response code="404">If no product matches the barcode.</response>
    [HttpGet("by-barcode/{code}")]
    [Authorize(Roles = "Owner,Manager,Cashier,Kitchen,Waiter")]
    [ProducesResponseType(typeof(ProductResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetByBarcode(string code)
    {
        var product = await _productService.GetByBarcodeAsync(BranchId, code);
        if (product == null)
            return NotFound(new { message = "Producto no encontrado" });
        return Ok(product);
    }

    /// <summary>
    /// Gets a product by barcode. Public endpoint for kiosk mode.
    /// </summary>
    /// <param name="code">The barcode string to search.</param>
    /// <param name="branchId">The branch ID as query parameter.</param>
    /// <returns>The matching product or 404.</returns>
    /// <response code="200">Returns the matching product.</response>
    /// <response code="404">If no product matches the barcode.</response>
    [HttpGet("public/by-barcode/{code}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ProductResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetByBarcodePublic(string code, [FromQuery] int branchId)
    {
        var product = await _productService.GetByBarcodeAsync(branchId, code);
        if (product == null)
            return NotFound(new { message = "Producto no encontrado" });
        return Ok(product);
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
    [ProducesResponseType(typeof(ProductResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(int id)
    {
        var product = await _productService.GetByIdAsync(id);
        return Ok(product);
    }

    /// <summary>
    /// Creates a new product.
    /// </summary>
    /// <param name="request">The product data to create.</param>
    /// <returns>The created product.</returns>
    /// <response code="201">Returns the created product.</response>
    /// <response code="400">If the product data is invalid.</response>
    [HttpPost]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(typeof(ProductResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] ProductRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var created = await _productService.CreateAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    /// <summary>
    /// Updates an existing product.
    /// </summary>
    /// <param name="id">The product identifier.</param>
    /// <param name="request">The updated product data.</param>
    /// <returns>The updated product.</returns>
    /// <response code="200">Returns the updated product.</response>
    /// <response code="404">If the product is not found.</response>
    /// <response code="400">If the product data is invalid.</response>
    [HttpPut("{id}")]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(typeof(ProductResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Update(int id, [FromBody] ProductRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var updated = await _productService.UpdateAsync(id, request);
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
    [ProducesResponseType(typeof(ProductResponse), StatusCodes.Status200OK)]
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
    [ProducesResponseType(typeof(ProductResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateStock(int id, [FromBody] UpdateStockRequest request)
    {
        var updated = await _productService.UpdateStockAsync(id, request.Type, request.Quantity);
        return Ok(updated);
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

    /// <summary>
    /// Uploads an image for a product and saves to Supabase Storage.
    /// </summary>
    /// <param name="id">The product identifier.</param>
    /// <param name="file">The image file.</param>
    /// <returns>The created product image.</returns>
    /// <response code="200">Returns the created image record.</response>
    /// <response code="400">If the file is missing or not an image.</response>
    /// <response code="404">If the product is not found.</response>
    [HttpPost("{id}/images")]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(typeof(ProductImage), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UploadImage(int id, IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "File is required" });

        if (!file.ContentType.StartsWith("image/"))
            return BadRequest(new { message = "File must be an image" });

        var product = await _productService.GetByIdAsync(id);
        if (product == null)
            return NotFound(new { message = $"Product with id {id} not found" });

        var url = await _storageService.UploadAsync(
            file.OpenReadStream(), file.FileName, file.ContentType);

        var image = new ProductImage
        {
            ProductId = id,
            Url = url,
            SortOrder = (product.Images?.Count ?? 0) + 1,
            CreatedAt = DateTime.UtcNow
        };

        await _productService.AddImageAsync(id, image);

        if (string.IsNullOrEmpty(product.ImageUrl))
            await _productService.UpdateImageUrlAsync(id, url);

        return Ok(image);
    }

    /// <summary>
    /// Deletes a product image from Supabase Storage and database.
    /// </summary>
    /// <param name="id">The product identifier.</param>
    /// <param name="imageId">The image identifier.</param>
    /// <returns>Success acknowledgement.</returns>
    /// <response code="200">Image deleted successfully.</response>
    /// <response code="404">If the image is not found.</response>
    [HttpDelete("{id}/images/{imageId}")]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteImage(int id, int imageId)
    {
        var image = await _productService.GetImageAsync(imageId);
        if (image == null || image.ProductId != id)
            return NotFound(new { message = "Image not found" });

        await _storageService.DeleteAsync(image.Url);
        await _productService.DeleteImageAsync(imageId);

        return Ok(new { success = true });
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
