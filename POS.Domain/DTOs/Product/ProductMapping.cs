using POS.Domain.Models;
using ProductEntity = POS.Domain.Models.Product;

namespace POS.Domain.DTOs.Product;

/// <summary>
/// Manual mapping between <see cref="ProductEntity"/> entities and their DTOs.
/// Kept as static extensions (no AutoMapper/Mapster in this project) so the
/// boundary between persistence and API contract is explicit and greppable.
/// The ProductEntity alias dodges the name clash between the
/// <c>POS.Domain.DTOs.Product</c> namespace and the <c>Product</c> model type.
/// </summary>
public static class ProductMapping
{
    /// <summary>
    /// Default name used when wrapping a flat list of extras into a single
    /// modifier group. Kept in sync with the migration backfill so the
    /// default group name is identical whether it was created by the
    /// migration or by a runtime Create/Update call.
    /// </summary>
    public const string DefaultGroupName = "Modificadores";

    public static ProductResponse ToResponse(this ProductEntity entity)
    {
        // Flatten every group's extras into the legacy flat shape so the
        // Angular frontend keeps receiving the same contract it does today.
        // Ordering follows group SortOrder first, then extra SortOrder, so
        // the flattened list remains deterministic across requests.
        var flatExtras = (entity.ModifierGroups ?? Enumerable.Empty<ProductModifierGroup>())
            .OrderBy(g => g.SortOrder)
            .ThenBy(g => g.Id)
            .SelectMany(g => (g.Extras ?? Enumerable.Empty<ProductExtra>())
                .OrderBy(e => e.SortOrder)
                .ThenBy(e => e.Id))
            .Select(e => new ProductExtraResponse
            {
                Id = e.Id,
                Label = e.Label,
                PriceCents = e.PriceCents
            })
            .ToList();

        return new ProductResponse
        {
            Id = entity.Id,
            CategoryId = entity.CategoryId,
            BranchId = entity.BranchId,
            Name = entity.Name,
            PriceCents = entity.PriceCents,
            ImageUrl = entity.ImageUrl,
            Description = entity.Description,
            Barcode = entity.Barcode,
            IsAvailable = entity.IsAvailable,
            IsPopular = entity.IsPopular,
            TrackStock = entity.TrackStock,
            CurrentStock = entity.CurrentStock,
            LowStockThreshold = entity.LowStockThreshold,
            SatProductCode = entity.SatProductCode,
            SatUnitCode = entity.SatUnitCode,
            TaxRate = entity.TaxRate,
            IsTaxIncluded = entity.IsTaxIncluded,
            PrintingDestination = entity.PrintingDestination,
            Sizes = entity.Sizes?.Select(s => new ProductSizeResponse
            {
                Id = s.Id,
                Label = s.Label,
                ExtraPriceCents = s.ExtraPriceCents
            }).ToList() ?? new(),
            Extras = flatExtras,
            Images = entity.Images?.Select(i => new ProductImageResponse
            {
                Id = i.Id,
                Url = i.Url,
                SortOrder = i.SortOrder,
                CreatedAt = i.CreatedAt
            }).ToList() ?? new()
        };
    }

    /// <summary>
    /// Builds a new entity from a create request. The caller is responsible
    /// for passing the entity to the repository — EF will generate the Id.
    /// </summary>
    public static ProductEntity ToEntity(this ProductRequest request)
    {
        return new ProductEntity
        {
            CategoryId = request.CategoryId,
            BranchId = request.BranchId,
            Name = request.Name,
            PriceCents = request.PriceCents,
            ImageUrl = request.ImageUrl,
            Description = request.Description,
            Barcode = request.Barcode,
            IsAvailable = request.IsAvailable,
            IsPopular = request.IsPopular,
            TrackStock = request.TrackStock,
            CurrentStock = request.CurrentStock,
            LowStockThreshold = request.LowStockThreshold,
            SatProductCode = request.SatProductCode,
            SatUnitCode = request.SatUnitCode,
            TaxRate = request.TaxRate,
            IsTaxIncluded = request.IsTaxIncluded,
            PrintingDestination = request.PrintingDestination,
            Sizes = request.Sizes.Select(s => new ProductSize
            {
                Label = s.Label,
                ExtraPriceCents = s.ExtraPriceCents
            }).ToList(),
            ModifierGroups = BuildDefaultGroups(request.Extras)
        };
    }

    /// <summary>
    /// Wraps a flat list of extras into a single default modifier group.
    /// The request DTO is still flat to preserve the frontend contract;
    /// once the UI sends grouped payloads, this method can be retired.
    /// Returns an empty collection when the request has no extras, so the
    /// product is simply saved without any modifier group.
    /// </summary>
    public static List<ProductModifierGroup> BuildDefaultGroups(IEnumerable<ProductExtraRequest> extras)
    {
        var list = extras?.ToList() ?? new List<ProductExtraRequest>();
        if (list.Count == 0) return new List<ProductModifierGroup>();

        var group = new ProductModifierGroup
        {
            Name = DefaultGroupName,
            SortOrder = 0,
            IsRequired = false,
            MinSelectable = 0,
            MaxSelectable = 99,
            Extras = list.Select((e, index) => new ProductExtra
            {
                Label = e.Label,
                PriceCents = e.PriceCents,
                SortOrder = index
            }).ToList()
        };

        return new List<ProductModifierGroup> { group };
    }

    /// <summary>
    /// Applies a request payload to an already-tracked entity. Only updates
    /// fields the user is allowed to change; identity fields (Id, BranchId)
    /// are left untouched. Sizes/Extras are replaced wholesale by the caller.
    /// </summary>
    public static void ApplyTo(this ProductRequest request, ProductEntity entity)
    {
        entity.Name = request.Name;
        entity.PriceCents = request.PriceCents;
        entity.ImageUrl = request.ImageUrl;
        entity.Description = request.Description;
        entity.Barcode = request.Barcode;
        entity.IsAvailable = request.IsAvailable;
        entity.IsPopular = request.IsPopular;
        entity.CategoryId = request.CategoryId;
        entity.TrackStock = request.TrackStock;
        entity.CurrentStock = request.CurrentStock;
        entity.LowStockThreshold = request.LowStockThreshold;
        entity.SatProductCode = request.SatProductCode;
        entity.SatUnitCode = request.SatUnitCode;
        entity.TaxRate = request.TaxRate;
        entity.IsTaxIncluded = request.IsTaxIncluded;
        entity.PrintingDestination = request.PrintingDestination;
    }
}
