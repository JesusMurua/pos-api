namespace POS.Domain.DTOs.AccessControl;

/// <summary>
/// Result of an access-control evaluation returned to the bridge so the local
/// reception UI can render granted/denied state and show the customer name.
/// All identifying fields are nullable to cover the "unknown QR" case where
/// no customer matched the hash.
/// </summary>
public class AccessResultDto
{
    public bool IsGranted { get; set; }

    public int AccessReasonId { get; set; }

    public int? CustomerId { get; set; }

    public string? CustomerName { get; set; }

    public int? CustomerMembershipId { get; set; }

    /// <summary>
    /// Id of the persisted <c>AccessLog</c> row backing this attempt. Null when
    /// no row was written (unknown QR — no Customer FK target). Lets dashboard
    /// clients deep-link from the real-time feed to the audit detail view.
    /// </summary>
    public int? AccessLogId { get; set; }
}
