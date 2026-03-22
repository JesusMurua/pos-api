using ClosedXML.Excel;
using POS.Domain.Models;
using POS.Repository;
using POS.Services.IService;

namespace POS.Services.Service;

/// <summary>
/// Implements product import from Excel files.
/// </summary>
public class ProductImportService : IProductImportService
{
    private readonly IUnitOfWork _unitOfWork;

    public ProductImportService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    #region Public API Methods

    /// <summary>
    /// Generates an Excel template for product import.
    /// </summary>
    public byte[] GenerateTemplate()
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Productos");

        // Headers
        var headers = new[] { "Nombre", "Precio", "Categoría", "Disponible (Sí/No)", "Popular (Sí/No)" };
        for (int i = 0; i < headers.Length; i++)
        {
            var cell = ws.Cell(1, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#16A34A");
        }

        // Example rows
        var examples = new[]
        {
            new { Name = "Enchiladas Verdes", Price = 75.00, Category = "Comida", Available = "Sí", Popular = "No" },
            new { Name = "Agua de Jamaica", Price = 25.00, Category = "Bebidas", Available = "Sí", Popular = "No" },
            new { Name = "Taco de Canasta", Price = 20.00, Category = "Antojitos", Available = "Sí", Popular = "Sí" }
        };

        for (int i = 0; i < examples.Length; i++)
        {
            var row = i + 2;
            ws.Cell(row, 1).Value = examples[i].Name;
            ws.Cell(row, 2).Value = examples[i].Price;
            ws.Cell(row, 3).Value = examples[i].Category;
            ws.Cell(row, 4).Value = examples[i].Available;
            ws.Cell(row, 5).Value = examples[i].Popular;

            for (int col = 1; col <= 5; col++)
                ws.Cell(row, col).Style.Fill.BackgroundColor = XLColor.FromHtml("#F0FDF4");
        }

        // Note
        var noteCell = ws.Cell(6, 1);
        noteCell.Value = "Nota: El precio debe ser en pesos (ej: 75.00). Disponible y Popular deben ser Sí o No.";
        noteCell.Style.Font.Italic = true;
        noteCell.Style.Font.FontColor = XLColor.Gray;

        ws.Columns().AdjustToContents();
        ws.SheetView.FreezeRows(1);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    /// <summary>
    /// Parses and validates an Excel file without saving to database.
    /// </summary>
    public async Task<ProductImportPreview> PreviewAsync(Stream fileStream, int branchId)
    {
        var preview = new ProductImportPreview();

        using var workbook = new XLWorkbook(fileStream);
        var ws = workbook.Worksheets.First();
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;

        for (int rowNum = 2; rowNum <= lastRow; rowNum++)
        {
            var row = ws.Row(rowNum);

            var name = row.Cell(1).GetString().Trim();
            var priceStr = row.Cell(2).GetString().Trim();
            var categoryName = row.Cell(3).GetString().Trim();
            var availableStr = row.Cell(4).GetString().Trim();
            var popularStr = row.Cell(5).GetString().Trim();

            // Skip completely empty rows
            if (string.IsNullOrEmpty(name) && string.IsNullOrEmpty(priceStr) &&
                string.IsNullOrEmpty(categoryName) && string.IsNullOrEmpty(availableStr) &&
                string.IsNullOrEmpty(popularStr))
                continue;

            // Skip note/instruction rows
            if (name.StartsWith("Nota", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("Note", StringComparison.OrdinalIgnoreCase))
                continue;

            preview.TotalRows++;
            var hasError = false;

            // Validate Name
            if (string.IsNullOrEmpty(name))
            {
                preview.Errors.Add(new ProductImportError
                    { RowNumber = rowNum, Field = "Nombre", Message = "El nombre es requerido" });
                hasError = true;
            }
            else if (name.Length > 150)
            {
                preview.Errors.Add(new ProductImportError
                    { RowNumber = rowNum, Field = "Nombre", Message = "El nombre no puede exceder 150 caracteres" });
                hasError = true;
            }

            // Validate Price
            if (!decimal.TryParse(priceStr, out var price) || price <= 0)
            {
                preview.Errors.Add(new ProductImportError
                    { RowNumber = rowNum, Field = "Precio", Message = "El precio debe ser un número mayor a 0" });
                hasError = true;
            }

            // Validate CategoryName
            if (string.IsNullOrEmpty(categoryName))
            {
                preview.Errors.Add(new ProductImportError
                    { RowNumber = rowNum, Field = "Categoría", Message = "La categoría es requerida" });
                hasError = true;
            }

            // Validate IsAvailable
            var isAvailable = true;
            if (!string.IsNullOrEmpty(availableStr) && !TryParseSiNo(availableStr, out isAvailable))
            {
                preview.Errors.Add(new ProductImportError
                    { RowNumber = rowNum, Field = "Disponible", Message = "Debe ser Sí o No" });
                hasError = true;
            }

            // Validate IsPopular
            var isPopular = false;
            if (!string.IsNullOrEmpty(popularStr) && !TryParseSiNo(popularStr, out isPopular))
            {
                preview.Errors.Add(new ProductImportError
                    { RowNumber = rowNum, Field = "Popular", Message = "Debe ser Sí o No" });
                hasError = true;
            }

            if (!hasError)
            {
                preview.ValidRows.Add(new ProductImportRow
                {
                    RowNumber = rowNum,
                    Name = name,
                    Price = price,
                    CategoryName = categoryName,
                    IsAvailable = isAvailable,
                    IsPopular = isPopular
                });
            }
        }

        return preview;
    }

    /// <summary>
    /// Executes the import, saving validated products to database.
    /// </summary>
    public async Task<ProductImportResult> ImportAsync(List<ProductImportRow> rows, int branchId)
    {
        var result = new ProductImportResult();
        var categoryCache = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // Pre-load existing categories for this branch
        var existingCategories = await _unitOfWork.Categories.GetAsync(
            c => c.BranchId == branchId && c.IsActive);

        foreach (var cat in existingCategories)
            categoryCache[cat.Name] = cat.Id;

        foreach (var row in rows)
        {
            // Resolve or create category
            if (!categoryCache.TryGetValue(row.CategoryName, out var categoryId))
            {
                var maxSort = categoryCache.Count > 0 ? categoryCache.Count + 1 : 1;
                var newCategory = new Category
                {
                    BranchId = branchId,
                    Name = row.CategoryName,
                    Icon = "pi-tag",
                    SortOrder = maxSort,
                    IsActive = true
                };

                await _unitOfWork.Categories.AddAsync(newCategory);
                await _unitOfWork.SaveChangesAsync();

                categoryCache[row.CategoryName] = newCategory.Id;
                categoryId = newCategory.Id;
                result.Warnings.Add($"Categoría '{row.CategoryName}' creada automáticamente");
            }

            var product = new Product
            {
                CategoryId = categoryId,
                Name = row.Name,
                PriceCents = (int)(row.Price * 100),
                IsAvailable = row.IsAvailable,
                IsPopular = row.IsPopular,
                ImageUrl = null
            };

            await _unitOfWork.Products.AddAsync(product);
            result.ImportedCount++;
        }

        await _unitOfWork.SaveChangesAsync();
        return result;
    }

    #endregion

    #region Private Helper Methods

    private static bool TryParseSiNo(string value, out bool result)
    {
        var normalized = value.ToLowerInvariant()
            .Replace("í", "i");

        switch (normalized)
        {
            case "si":
            case "sí":
            case "yes":
                result = true;
                return true;
            case "no":
                result = false;
                return true;
            default:
                result = false;
                return false;
        }
    }

    #endregion
}
