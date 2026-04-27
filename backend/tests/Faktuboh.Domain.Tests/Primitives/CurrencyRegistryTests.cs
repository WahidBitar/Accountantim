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
}
