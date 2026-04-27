using AwesomeAssertions;
using Faktuboh.Domain.Primitives;

namespace Faktuboh.Domain.Tests.Primitives;

public class CurrencyRegistryTests
{
    [Fact]
    public void Initial_set_contains_exactly_ten_currencies()
    {
        CurrencyRegistry.All.Should().HaveCount(10);
    }

    [Theory]
    [InlineData("EUR", 2)]
    [InlineData("USD", 2)]
    [InlineData("GBP", 2)]
    [InlineData("AED", 2)]
    [InlineData("SAR", 2)]
    [InlineData("EGP", 2)]
    [InlineData("JOD", 3)]
    [InlineData("KWD", 3)]
    [InlineData("BHD", 3)]
    [InlineData("TND", 3)]
    public void Each_currency_has_correct_minor_units(string code, int expectedMinorUnits)
    {
        CurrencyRegistry.IsSupported(code).Should().BeTrue();
        CurrencyRegistry.MinorUnits(code).Should().Be(expectedMinorUnits);
    }

    [Theory]
    [InlineData("XYZ")]   // unknown code
    [InlineData("eur")]   // case-sensitive: lower-case rejected (D2/2b strict ISO 4217)
    [InlineData("")]      // empty string
    public void Unsupported_codes_are_rejected(string code)
    {
        CurrencyRegistry.IsSupported(code).Should().BeFalse();
    }

    [Theory]
    [InlineData("EUR", 2)]
    [InlineData("JOD", 3)]
    public void TryGetMinorUnits_returns_true_with_correct_value_for_supported_codes(string code, int expected)
    {
        // R2-P1: atomic single-lookup helper used by Money's constructor.
        var ok = CurrencyRegistry.TryGetMinorUnits(code, out var minorUnits);

        ok.Should().BeTrue();
        minorUnits.Should().Be(expected);
    }

    [Theory]
    [InlineData("XYZ")]
    [InlineData("eur")]
    public void TryGetMinorUnits_returns_false_for_unsupported_codes(string code)
    {
        var ok = CurrencyRegistry.TryGetMinorUnits(code, out var minorUnits);

        ok.Should().BeFalse();
        minorUnits.Should().Be(0); // out value defaults
    }
}
