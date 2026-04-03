namespace POS.Domain.Exceptions;

public class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException() : base() { }

    public ConcurrencyConflictException(string message) : base(message) { }

    public ConcurrencyConflictException(string message, Exception innerException)
        : base(message, innerException) { }
}
