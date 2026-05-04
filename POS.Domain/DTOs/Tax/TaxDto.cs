namespace POS.Domain.DTOs.Tax;

/// <summary>
/// Read-only projection of a <see cref="POS.Domain.Models.Tax"/> catalog row.
/// Mirrors the entity shape with nullability preserved on <see cref="Code"/>
/// (SAT codes are optional for non-MX taxes).
/// </summary>
public record TaxDto(
    int Id,
    string? Code,
    string CountryCode,
    bool IsDefault,
    string Name,
    decimal Rate);
