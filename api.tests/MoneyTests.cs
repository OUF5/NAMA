using System.Globalization;
using Api.Domain;

namespace Api.Tests;

// R-MON-01 and R-MON-02. Decimals are passed as strings because attributes cannot hold decimal.
public class MoneyTests
{
    private static decimal Amount(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);

    [Theory]
    [InlineData("125.50", 12550)]
    [InlineData("0.01", 1)]
    [InlineData("10000000", 1_000_000_000)]
    [InlineData("1.500", 150)]
    public void Amount_converts_to_minor_units_and_back(string amount, long minor)
    {
        Assert.Equal(minor, Money.ToMinor(Amount(amount)));
        Assert.Equal(Amount(amount), Money.FromMinor(minor));
    }

    [Fact]
    public void More_than_two_decimals_cannot_convert()
    {
        Assert.Throws<ArgumentException>(() => Money.ToMinor(1.005m));
    }

    [Theory]
    [InlineData("0.01")]
    [InlineData("125.5")]
    [InlineData("1.500")]
    [InlineData("10000000")]
    public void Valid_amount(string amount)
    {
        Assert.True(Money.IsValidAmount(Amount(amount)));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1.005")]
    [InlineData("10000000.01")]
    [InlineData("79228162514264337593543950335")]
    public void Invalid_amount(string amount)
    {
        Assert.False(Money.IsValidAmount(Amount(amount)));
    }

    // SET-01 allows a salary of 0.
    [Fact]
    public void Zero_is_valid_only_when_allowed()
    {
        Assert.True(Money.IsValidAmount(0m, allowZero: true));
        Assert.False(Money.IsValidAmount(-0.01m, allowZero: true));
    }
}
