using Microsoft.EntityFrameworkCore;
using POS.Domain.DTOs.Admin;
using POS.Domain.DTOs.Catalogs;
using POS.Domain.Exceptions;
using POS.Repository;
using POS.Services.IService;

namespace POS.Services.Service;

/// <inheritdoc />
public class PlanTypeAdminService : IPlanTypeAdminService
{
    private readonly ApplicationDbContext _context;
    private readonly ICatalogService _catalogService;

    public PlanTypeAdminService(ApplicationDbContext context, ICatalogService catalogService)
    {
        _context = context;
        _catalogService = catalogService;
    }

    public async Task<IReadOnlyList<PlanTypeDto>> GetAllAsync() =>
        await _context.PlanTypeCatalogs.AsNoTracking()
            .OrderBy(p => p.SortOrder).ThenBy(p => p.Id)
            .Select(p => new PlanTypeDto(p.Id, p.Code, p.Name, p.SortOrder, p.MonthlyPrice, p.Currency))
            .ToListAsync();

    public async Task UpdateAsync(int id, AdminUpdatePlanTypeRequest request)
    {
        if (request.MonthlyPrice is < 0m)
            throw new ValidationException("MonthlyPrice cannot be negative.");

        var row = await _context.PlanTypeCatalogs.FirstOrDefaultAsync(p => p.Id == id)
            ?? throw new NotFoundException($"Plan type {id} not found.");

        // Code is the immutable freeze key. Full-replace of the editable fields.
        row.Name = request.Name;
        row.SortOrder = request.SortOrder;
        row.Currency = request.Currency;
        row.MonthlyPrice = request.MonthlyPrice; // nullable allowed (Enterprise = contact-sales)

        await _context.SaveChangesAsync();

        // Both public envelopes project MonthlyPrice — invalidate both (OQ-3 / D6).
        _catalogService.Invalidate("PlanTypes");
        _catalogService.Invalidate("Plans");
    }
}
