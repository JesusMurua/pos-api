namespace POS.Domain.DTOs.Admin;

/// <summary>Manual super-admin notification send (POST /Admin/businesses/{id}/notify).</summary>
public sealed record AdminNotifyRequest
{
    public string TemplateCode { get; init; } = null!;
    public Dictionary<string, string>? Payload { get; init; }
}
