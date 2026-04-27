namespace Faktuboh.Domain.Errors;

public abstract class DomainException : Exception
{
    protected DomainException(string code, string message) : base(message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        Code = code;
    }

    public string Code { get; }
}
