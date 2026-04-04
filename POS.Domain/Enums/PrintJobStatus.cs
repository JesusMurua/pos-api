namespace POS.Domain.Enums;

/// <summary>
/// Represents the lifecycle state of a <see cref="Models.PrintJob"/>.
/// Transitions: Pending → InProgress → Printed (success) or Pending → Failed (max attempts exceeded).
/// </summary>
public enum PrintJobStatus
{
    /// <summary>
    /// The job has been created and is waiting to be picked up by a printer, KDS, or tablet.
    /// Valid transitions: <see cref="InProgress"/>, <see cref="Printed"/>, <see cref="Failed"/>.
    /// </summary>
    Pending = 0,

    /// <summary>
    /// The peripheral confirmed successful printing.
    /// <c>PrintedAt</c> is set when transitioning to this state.
    /// Valid from: <see cref="Pending"/> or <see cref="InProgress"/>.
    /// </summary>
    Printed = 1,

    /// <summary>
    /// Printing failed after reaching the maximum allowed attempt count (3).
    /// Manual intervention is required.
    /// </summary>
    Failed = 2,

    /// <summary>
    /// A KDS operator acknowledged the ticket and is actively preparing the items.
    /// Intermediate state between <see cref="Pending"/> and <see cref="Printed"/>.
    /// Valid transition: <see cref="Printed"/>.
    /// </summary>
    InProgress = 3
}
