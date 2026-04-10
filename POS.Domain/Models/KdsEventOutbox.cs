using System.ComponentModel.DataAnnotations;

namespace POS.Domain.Models;

/// <summary>
/// Transactional outbox row used to dispatch KDS real-time events via SignalR.
/// Written in the SAME transaction as the originating PrintJob so that a server
/// restart between the database commit and the SignalR broadcast cannot drop
/// the event. A background worker drains this table on a short interval.
/// </summary>
public partial class KdsEventOutbox
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Branch this event belongs to. Integer matches the rest of the domain
    /// (Branch.Id is int); do not promote to Guid without migrating every FK.
    /// </summary>
    public int BranchId { get; set; }

    /// <summary>
    /// Physical destination group: "Kitchen", "Bar", "Waiters".
    /// Stored as a string so the dispatcher can build SignalR group names
    /// without coupling the worker to the PrintingDestination enum layout.
    /// </summary>
    [Required]
    [MaxLength(32)]
    public string Destination { get; set; } = null!;

    /// <summary>Serialized JSON payload broadcast to connected KDS clients.</summary>
    [Required]
    public string Payload { get; set; } = null!;

    /// <summary>True once the dispatcher has broadcast the event.</summary>
    public bool IsProcessed { get; set; }

    /// <summary>UTC timestamp when the outbox row was inserted.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>UTC timestamp when the event was successfully broadcast.</summary>
    public DateTime? ProcessedAt { get; set; }
}
