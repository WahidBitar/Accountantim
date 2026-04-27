using AwesomeAssertions;
using Faktuboh.Domain.Primitives;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace Faktuboh.Domain.Tests.Primitives;

public class MoneyTests
{
    [Theory]
    [InlineData("EUR", 1.23)]
    [InlineData("USD", 0.00)]
    [InlineData("JOD", 1.234)]
    [InlineData("KWD", 99.999)]
    public void Construction_succeeds_for_valid_amount_and_currency(string currency, double amount)
    {
        var money = new Money((decimal)amount, currency);

        money.Currency.Should().Be(currency);
        money.Amount.Should().Be((decimal)amount);
    }

    [Fact]
    public void Construction_throws_for_unsupported_currency()
    {
        var act = () => new Money(1m, "XYZ");
        act.Should().Throw<ArgumentException>().WithMessage("*XYZ*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Construction_throws_for_null_or_whitespace_currency(string currency)
    {
        var act = () => new Money(1m, currency);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Construction_throws_for_negative_amount()
    {
        var act = () => new Money(-1m, "EUR");
        act.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*non-negative*");
    }

    [Fact]
    public void Construction_throws_when_amount_exceeds_max_amount()
    {
        // R2-P3: Money rejects amounts above the numeric(19,4) ceiling.
        var act = () => new Money(Money.MaxAmount + 1m, "EUR");
        act.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*must not exceed*");
    }

    [Fact]
    public void Construction_normalizes_negative_zero_to_positive_zero()
    {
        // R2-P4: -0m bit pattern must not survive into ledger arithmetic.
        var negativeZero = new decimal(0, 0, 0, true, 0);

        var money = new Money(negativeZero, "EUR");

        // Equality check (`-0m == 0m` is true) is not enough — round-trip via
        // GetBits to verify the sign bit was actually stripped.
        var bits = decimal.GetBits(money.Amount);
        var signBitSet = (bits[3] & 0x80000000) != 0;
        signBitSet.Should().BeFalse(because: "negative-zero must be normalized to +0m");
    }

    [Theory]
    [InlineData("EUR", 1.234)]   // 3 dp on a 2-dp currency
    [InlineData("USD", 0.001)]
    [InlineData("JOD", 1.2345)]  // 4 dp on a 3-dp currency
    public void Construction_throws_when_amount_precision_exceeds_currency_minor_units(string currency, double amount)
    {
        var act = () => new Money((decimal)amount, currency);
        act.Should().Throw<ArgumentException>().WithMessage("*precision*");
    }

    [Property(MaxTest = 200)]
    public Property Amount_round_to_currency_precision_is_always_constructible()
    {
        var currencyGen = Gen.Elements(CurrencyRegistry.All.Keys.ToArray());

        // Bound the generator so decimal.Round does not overflow at the extremes
        // (decimal.MaxValue rounded to 2dp throws OverflowException). Also keep
        // amounts non-negative per Money's domain constraint (D1/1a).
        var amountGen = ArbMap.Default.GeneratorFor<decimal>()
            .Where(d => d >= 0m && d <= 1_000_000_000_000_000m);

        return Prop.ForAll(
            currencyGen.ToArbitrary(),
            amountGen.ToArbitrary(),
            (currency, rawAmount) =>
            {
                var minorUnits = CurrencyRegistry.MinorUnits(currency);
                var rounded = decimal.Round(rawAmount, minorUnits, MidpointRounding.ToEven);

                var money = new Money(rounded, currency);
                return money.Amount == rounded && money.Currency == currency;
            });
    }
}
