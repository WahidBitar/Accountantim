namespace Faktuboh.Domain.Errors;

public abstract class DomainException : Exception
{
    protected DomainException(string code, string message) : base(message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        Code = code;
    }

    /// <summary>
    /// Wrapping overload that preserves the causal chain when a lower-layer exception
    /// (e.g. an EF Core constraint violation) is rethrown as a domain error.
    /// </summary>
    protected DomainException(string code, string message, Exception? innerException)
        : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        Code = code;
    }

    public string Code { get; }
}
