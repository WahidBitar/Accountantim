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
