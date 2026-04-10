namespace POS.Domain.Exceptions;

/// <summary>
/// Thrown when a CashRegister with the requested name already exists in the
/// branch and the caller did not opt into the takeover/recovery flow.
/// The middleware maps this to HTTP 409 Conflict and includes the existing
/// register id so the frontend can prompt the user to confirm takeover.
/// </summary>
public class RegisterNameTakenException : Exception
{
    public int ExistingRegisterId { get; }
    public bool HasOpenSession { get; }

    public RegisterNameTakenException(int existingRegisterId, bool hasOpenSession)
        : base("Ya existe una caja con ese nombre en esta sucursal. Confirma takeover para reclamarla.")
    {
        ExistingRegisterId = existingRegisterId;
        HasOpenSession = hasOpenSession;
    }
}
