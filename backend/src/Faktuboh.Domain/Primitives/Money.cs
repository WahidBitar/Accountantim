namespace Faktuboh.Domain.Primitives;

/// <summary>
/// A monetary amount in a supported ISO 4217 currency.
/// </summary>
/// <remarks>
/// <para><b>Currency code is case-sensitive (ISO 4217 upper-case).</b> Lower-case input
/// (e.g. <c>"eur"</c>) is intentionally rejected to surface caller bugs at boundaries.
/// Callers must canonicalize at system boundaries.</para>
/// <para><b>Sign is held by Direction, not Money.</b> Negative <see cref="Amount"/> values
/// are rejected; receivable/payable polarity belongs to <see cref="Direction"/> at the
/// aggregate level.</para>
/// <para><b>Amount is normalized to the currency's minor-unit scale</b> via banker's
/// rounding (<see cref="MidpointRounding.ToEven"/>); inputs with greater precision are
/// rejected rather than truncated.</para>
/// </remarks>
public sealed record Money
{
    public decimal Amount { get; }
    public string Currency { get; }

    public Money(decimal amount, string currency)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);

        if (amount < 0m)
            throw new ArgumentOutOfRangeException(
                nameof(amount),
                "Money amount must be non-negative; use Direction { Receivable, Payable } for sign.");

        if (!CurrencyRegistry.IsSupported(currency))
            throw new ArgumentException($"Unsupported currency '{currency}'", nameof(currency));

        var minorUnits = CurrencyRegistry.MinorUnits(currency);

        decimal rounded;
        try
        {
            rounded = decimal.Round(amount, minorUnits, MidpointRounding.ToEven);
        }
        catch (OverflowException ex)
        {
            throw new ArgumentOutOfRangeException(
                $"Amount {amount} cannot be rounded to {minorUnits} minor units without overflow.",
                ex);
        }

        if (amount != rounded)
            throw new ArgumentException(
                $"Amount {amount} precision exceeds {minorUnits} minor units for currency '{currency}'",
                nameof(amount));

        Amount = rounded;
        Currency = currency;
    }
}
