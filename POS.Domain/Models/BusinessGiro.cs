using POS.Domain.Models.Catalogs;

namespace POS.Domain.Models;

/// <summary>
/// Junction entity linking a <see cref="Business"/> to one or more
/// <see cref="BusinessTypeCatalog"/> sub-giros (N:M identity list).
/// </summary>
public class BusinessGiro
{
    public int Id { get; set; }

    public int BusinessId { get; set; }

    /// <summary>FK to <see cref="BusinessTypeCatalog"/>.Id.</summary>
    public int BusinessTypeId { get; set; }

    public Business Business { get; set; } = null!;

    public BusinessTypeCatalog? BusinessTypeCatalog { get; set; }
}
