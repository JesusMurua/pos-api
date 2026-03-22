namespace POS.Domain.Models;

/// <summary>
/// Single product row parsed from Excel.
/// </summary>
public class ProductImportRow
{
    public int RowNumber { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public bool IsAvailable { get; set; } = true;
    public bool IsPopular { get; set; }
}

/// <summary>
/// Result of Excel validation before import.
/// </summary>
public class ProductImportPreview
{
    public List<ProductImportRow> ValidRows { get; set; } = new();
    public List<ProductImportError> Errors { get; set; } = new();
    public int TotalRows { get; set; }
    public bool HasErrors => Errors.Any();
}

/// <summary>
/// Validation error for a specific row.
/// </summary>
public class ProductImportError
{
    public int RowNumber { get; set; }
    public string Field { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Result after executing the import.
/// </summary>
public class ProductImportResult
{
    public int ImportedCount { get; set; }
    public int SkippedCount { get; set; }
    public List<string> Warnings { get; set; } = new();
}
