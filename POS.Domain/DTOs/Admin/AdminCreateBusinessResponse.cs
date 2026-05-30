namespace POS.Domain.DTOs.Admin;

/// <summary>
/// Response from <c>POST /api/Admin/businesses</c>. Surfaces the
/// freshly-created tenant identifiers plus resolved codes the admin panel
/// can render without a second catalog round-trip.
/// <para>
/// <see cref="OwnerJwt"/> is intentionally nullable and only populated when
/// the request set <c>IncludeOwnerJwt = true</c>. Under the global
/// <c>WhenWritingNull</c> JSON policy the field is omitted from the wire
/// payload entirely when null, so a default-flag response does not even
/// hint at its existence to the admin panel logs.
/// </para>
/// </summary>
public sealed record AdminCreateBusinessResponse(
    int BusinessId,
    string OwnerEmail,
    string OwnerName,
    int PlanTypeId,
    string PlanTypeCode,
    int PrimaryMacroCategoryId,
    string PrimaryMacroCategoryCode,
    string? TrialEndsAt,
    string CreatedAt,
    string? OwnerJwt);
