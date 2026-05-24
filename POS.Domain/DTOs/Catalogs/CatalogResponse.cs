namespace POS.Domain.DTOs.Catalogs;

/// <summary>
/// Read-only envelope returned by every <c>ICatalogService.Get*Async</c>
/// method. Pairs the projected DTO list with the strong ETag computed by
/// the cache-aside helper (see BDD-021 §6.1.B) so the controller can
/// negotiate <c>If-None-Match</c> without recomputing the fingerprint.
/// </summary>
/// <typeparam name="T">DTO record type contained in the payload.</typeparam>
/// <param name="Payload">Deterministically-ordered DTO list (see BDD-021 §6.1.E).</param>
/// <param name="ETag">
/// Strong ETag wrapped in double quotes per RFC 9110 §8.8
/// (e.g. <c>"abc123..."</c>). Emitted verbatim by the controller helper.
/// </param>
public sealed record CatalogResponse<T>(IReadOnlyList<T> Payload, string ETag);
