using System.ComponentModel.DataAnnotations;
using POS.Domain.Models.Catalogs;

namespace POS.Domain.Models;

/// <summary>
/// Junction entity linking a Business to one or more BusinessTypeCatalog entries (multi-giro).
/// </summary>
public class BusinessGiro
{
    public int Id { get; set; }

    public int BusinessId { get; set; }

    /// <summary>
    /// Code matching BusinessTypeCatalog.Code (e.g., "Restaurant", "Abarrotes").
    /// </summary>
    [Required, MaxLength(20)]
    public string CatalogCode { get; set; } = null!;

    /// <summary>
    /// Optional user-defined description when the giro is "Otra tienda" or needs clarification.
    /// </summary>
    [MaxLength(100)]
    public string? CustomDescription { get; set; }

    public Business Business { get; set; } = null!;

    public BusinessTypeCatalog? BusinessTypeCatalog { get; set; }
}
