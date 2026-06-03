namespace POS.Domain.DTOs.Pos;

/// <summary>
/// Response from <c>POST /api/Pos/initialize-cashier-session</c>. Carries
/// the freshly registered (or already-existing) <see cref="DeviceInfo"/>
/// plus the linked <see cref="CashRegisterInfo"/>, the discrete
/// <see cref="InitializeOutcome"/> the orchestration took, and — only for
/// <see cref="InitializeOutcome.ForceTakeover"/> — the id of the
/// previously-open session the server force-closed so the SPA can deep
/// link to a "shift was force-closed" detail view.
/// </summary>
public sealed record InitializeCashierSessionResponse(
    DeviceInfo Device,
    CashRegisterInfo Register,
    InitializeOutcome Outcome,
    int? ClosedSessionId);

/// <summary>
/// Compact device projection embedded in
/// <see cref="InitializeCashierSessionResponse"/>. Mirrors the wire shape
/// the SPA stores in IndexedDB for subsequent calls.
/// </summary>
public sealed record DeviceInfo(
    int Id,
    string Uuid,
    string Mode,
    string Name,
    int BranchId);

/// <summary>
/// Compact register projection embedded in
/// <see cref="InitializeCashierSessionResponse"/>.
/// </summary>
public sealed record CashRegisterInfo(
    int Id,
    string Name,
    int? DeviceId,
    bool IsActive);
