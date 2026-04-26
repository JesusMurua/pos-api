using POS.Domain.Models;

namespace POS.Services.IService;

/// <summary>
/// Provides operations for managing businesses.
/// </summary>
public interface IBusinessService
{
    /// <summary>
    /// Retrieves a business by its identifier.
    /// </summary>
    Task<Business> GetByIdAsync(int id);

    /// <summary>
    /// Creates a new business with its matrix branch and assigns the owner to it.
    /// </summary>
    /// <param name="business">The business data.</param>
    /// <param name="ownerUserId">The owner user ID to assign to the matrix branch.</param>
    Task<Business> CreateAsync(Business business, int ownerUserId);

    /// <summary>
    /// Updates an existing business.
    /// </summary>
    Task<Business> UpdateAsync(Business business);

    /// <summary>
    /// Replaces the macro category, custom description and full sub-giro set for a business.
    /// Deletes all existing <see cref="BusinessGiro"/> rows and inserts the new ones.
    /// </summary>
    Task<Business> UpdateGiroAsync(int businessId, int primaryMacroCategoryId, IReadOnlyList<int> businessTypeIds, string? customGiroDescription);

    /// <summary>
    /// Returns the business's current macro category, sub-giro set and optional custom description.
    /// Used by the frontend to rehydrate onboarding state after a reload or "back" navigation.
    /// </summary>
    Task<BusinessGiroResponse> GetGiroAsync(int businessId);

    /// <summary>
    /// Updates fiscal configuration (RFC, tax regime, legal name, invoicing flag).
    /// Per BDD-015, the <c>false → true</c> transition of <paramref name="invoicingEnabled"/>
    /// requires the <see cref="POS.Domain.Enums.FeatureKey.CfdiInvoicing"/> feature; other
    /// field edits and the <c>true → false</c> transition bypass the gate so operators
    /// can always clean state.
    /// </summary>
    /// <exception cref="POS.Domain.Exceptions.PlanLimitExceededException">
    /// Thrown when enabling invoicing on a plan that does not include <c>CfdiInvoicing</c>.
    /// </exception>
    Task<Business> UpdateFiscalConfigAsync(
        int businessId, string? rfc, string? taxRegime, string? legalName, bool invoicingEnabled);

    /// <summary>
    /// Returns the flat settings view used by the frontend Settings screen:
    /// business display name plus the matrix branch's contact information.
    /// </summary>
    /// <exception cref="POS.Domain.Exceptions.NotFoundException">
    /// Thrown when the business or its matrix branch cannot be located.
    /// </exception>
    Task<BusinessSettingsResult> GetSettingsAsync(int businessId);

    /// <summary>
    /// Updates the business display name together with the matrix branch's
    /// address and phone in a single SaveChanges call.
    /// </summary>
    /// <exception cref="POS.Domain.Exceptions.NotFoundException">
    /// Thrown when the business or its matrix branch cannot be located.
    /// </exception>
    Task<BusinessSettingsResult> UpdateSettingsAsync(
        int businessId, string businessName, string? address, string? phone);
}

/// <summary>
/// Service-layer projection that mirrors the API <c>BusinessSettingsDto</c>
/// so the controller can map it without leaking entities.
/// </summary>
public class BusinessSettingsResult
{
    public string BusinessName { get; set; } = null!;
    public string? Address { get; set; }
    public string? Phone { get; set; }
}

/// <summary>
/// Current giro configuration for a business — shape returned by GET /api/business/giro.
/// </summary>
public class BusinessGiroResponse
{
    public int PrimaryMacroCategoryId { get; set; }

    /// <summary>
    /// Set of sub-giro ids (<c>BusinessTypeCatalog.Id</c>). Renamed from
    /// <c>BusinessTypeIds</c> by BDD-015 for symmetry with
    /// <c>UpdateBusinessGiroRequest.SubGiroIds</c>.
    /// </summary>
    public List<int> SubGiroIds { get; set; } = new();

    public string? CustomGiroDescription { get; set; }
}
