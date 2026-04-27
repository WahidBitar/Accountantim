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
/// <para><b>Amount is bounded.</b> Values above <see cref="MaxAmount"/> are rejected to
/// stay within Postgres <c>numeric(19, 4)</c> precision (architecture.md §5.2.5) and to
/// prevent silent overflow in downstream aggregate arithmetic.</para>
/// <para><b>Amount is normalized to the currency's minor-unit scale</b> via banker's
/// rounding (<see cref="MidpointRounding.ToEven"/>); inputs with greater precision are
/// rejected rather than truncated. Negative-zero (<c>-0m</c>) is normalized to <c>+0m</c>.</para>
/// </remarks>
public sealed record Money
{
    /// <summary>
    /// Maximum permitted <see cref="Amount"/>. Aligns with Postgres <c>numeric(19, 4)</c>
    /// precision for user-facing ledger storage (architecture.md §5.2.5) and matches the
    /// FsCheck range applied by <c>MoneyTests.Amount_round_to_currency_precision_is_always_constructible</c>.
    /// </summary>
    public const decimal MaxAmount = 1_000_000_000_000_000m;

    public decimal Amount { get; }
    public string Currency { get; }

    public Money(decimal amount, string currency)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);

        if (amount < 0m)
            throw new ArgumentOutOfRangeException(
                nameof(amount),
                amount,
                "Money amount must be non-negative; use Direction { Receivable, Payable } for sign.");

        if (amount > MaxAmount)
            throw new ArgumentOutOfRangeException(
                nameof(amount),
                amount,
                $"Money amount must not exceed {MaxAmount:N0} (Postgres numeric(19,4) ledger precision).");

        if (!CurrencyRegistry.TryGetMinorUnits(currency, out var minorUnits))
            throw new ArgumentException($"Unsupported currency '{currency}'", nameof(currency));

        decimal rounded;
        try
        {
            rounded = decimal.Round(amount, minorUnits, MidpointRounding.ToEven);
        }
        catch (OverflowException)
        {
            // OverflowException is unreachable under the MaxAmount guard above for any
            // currency with minorUnits in [0, 28]; the catch remains as defensive belt
            // against future MaxAmount changes. Propagate as ArgumentOutOfRangeException
            // with paramName + actualValue set so diagnostics surface correctly.
            throw new ArgumentOutOfRangeException(
                nameof(amount),
                amount,
                $"Amount cannot be rounded to {minorUnits} minor units without overflow.");
        }

        if (amount != rounded)
            throw new ArgumentException(
                $"Amount {amount} precision exceeds {minorUnits} minor units for currency '{currency}'",
                nameof(amount));

        // Normalize negative-zero (-0m) to +0m. `decimal -0m` exists as a bit pattern;
        // `amount < 0m` is false for it, and `decimal.Round` preserves the sign.
        // Assigning the literal 0m strips the negative-zero bit pattern.
        Amount = rounded == 0m ? 0m : rounded;
        Currency = currency;
    }
}
