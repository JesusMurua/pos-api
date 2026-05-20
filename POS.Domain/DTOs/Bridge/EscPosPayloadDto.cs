using System.Text.Json.Serialization;

namespace POS.Domain.DTOs.Bridge;

// Forces PascalCase on the wire to match the Windows Service expectation without breaking global camelCase for Angular.
public record EscPosPayloadDto(
    [property: JsonPropertyName("PrinterId")] string PrinterId,
    [property: JsonPropertyName("Base64Bytes")] string Base64Bytes
);
