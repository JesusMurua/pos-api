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
    public static ProductResponse ToResponse(this ProductEntity entity)
    {
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
            Metadata = entity.Metadata,
            Sizes = entity.Sizes?.Select(s => new ProductSizeResponse
            {
                Id = s.Id,
                Label = s.Label,
                ExtraPriceCents = s.ExtraPriceCents
            }).ToList() ?? new(),
            ModifierGroups = (entity.ModifierGroups ?? Enumerable.Empty<ProductModifierGroup>())
                .OrderBy(g => g.SortOrder)
                .ThenBy(g => g.Id)
                .Select(g => g.ToResponse())
                .ToList(),
            Images = entity.Images?.Select(i => new ProductImageResponse
            {
                Id = i.Id,
                Url = i.Url,
                SortOrder = i.SortOrder,
                CreatedAt = i.CreatedAt
            }).ToList() ?? new()
        };
    }

    public static ProductModifierGroupResponse ToResponse(this ProductModifierGroup group)
    {
        return new ProductModifierGroupResponse
        {
            Id = group.Id,
            Name = group.Name,
            SortOrder = group.SortOrder,
            IsRequired = group.IsRequired,
            MinSelectable = group.MinSelectable,
            MaxSelectable = group.MaxSelectable,
            Extras = (group.Extras ?? Enumerable.Empty<ProductExtra>())
                .OrderBy(e => e.SortOrder)
                .ThenBy(e => e.Id)
                .Select(e => new ProductExtraResponse
                {
                    Id = e.Id,
                    Label = e.Label,
                    PriceCents = e.PriceCents,
                    SortOrder = e.SortOrder
                })
                .ToList()
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
            Metadata = request.Metadata,
            Sizes = request.Sizes.Select(s => new ProductSize
            {
                Label = s.Label,
                ExtraPriceCents = s.ExtraPriceCents
            }).ToList(),
            ModifierGroups = request.ModifierGroups.Select(g => g.ToEntity()).ToList()
        };
    }

    /// <summary>
    /// Converts a modifier group request into a fresh entity tree (group
    /// plus its extras). Used both by <see cref="ToEntity"/> and by the
    /// service's update path when rebuilding the product's groups.
    /// </summary>
    public static ProductModifierGroup ToEntity(this ProductModifierGroupRequest request)
    {
        return new ProductModifierGroup
        {
            Name = request.Name,
            SortOrder = request.SortOrder,
            IsRequired = request.IsRequired,
            MinSelectable = request.MinSelectable,
            MaxSelectable = request.MaxSelectable,
            Extras = request.Extras.Select(e => new ProductExtra
            {
                Label = e.Label,
                PriceCents = e.PriceCents,
                SortOrder = e.SortOrder
            }).ToList()
        };
    }

    /// <summary>
    /// Applies a request payload to an already-tracked entity. Only updates
    /// fields the user is allowed to change; identity fields (Id, BranchId)
    /// are left untouched. Sizes and ModifierGroups are replaced wholesale
    /// by the caller.
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
        entity.Metadata = request.Metadata;
    }
}
