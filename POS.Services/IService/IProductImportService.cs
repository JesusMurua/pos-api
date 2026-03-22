using POS.Domain.Models;

namespace POS.Services.IService;

/// <summary>
/// Provides operations for importing products from Excel files.
/// </summary>
public interface IProductImportService
{
    /// <summary>
    /// Generates an Excel template for product import.
    /// </summary>
    /// <returns>Excel file as byte array.</returns>
    byte[] GenerateTemplate();

    /// <summary>
    /// Parses and validates an Excel file without saving to database.
    /// </summary>
    /// <param name="fileStream">The uploaded Excel file stream.</param>
    /// <param name="branchId">The branch to import products to.</param>
    /// <returns>A preview with valid rows and validation errors.</returns>
    Task<ProductImportPreview> PreviewAsync(Stream fileStream, int branchId);

    /// <summary>
    /// Executes the import, saving validated products to database.
    /// </summary>
    /// <param name="rows">The validated rows to import.</param>
    /// <param name="branchId">The branch to import products to.</param>
    /// <returns>Import result with counts and warnings.</returns>
    Task<ProductImportResult> ImportAsync(List<ProductImportRow> rows, int branchId);
}
