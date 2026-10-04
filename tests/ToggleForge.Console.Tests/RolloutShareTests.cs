using ToggleForge.Console.Drafts;

namespace ToggleForge.Console.Tests;

public sealed class RolloutShareTests
{
    [Theory]
    [InlineData("0", 0)]
    [InlineData("0.01", 1)]
    [InlineData("12.5", 1_250)]
    [InlineData("99.99", 9_999)]
    [InlineData("100", 10_000)]
    public void A_percent_with_up_to_two_decimals_becomes_basis_points(string percent, int expected)
    {
        var converted = RolloutShare.TryToBasisPoints(decimal.Parse(percent, System.Globalization.CultureInfo.InvariantCulture), out var basisPoints);

        Assert.True(converted);
        Assert.Equal(expected, basisPoints);
    }

    [Theory]
    [InlineData("-0.01")]
    [InlineData("100.01")]
    [InlineData("0.001")]
    [InlineData("12.345")]
    public void A_percent_outside_0_to_100_or_with_three_decimals_is_refused(string percent)
    {
        var converted = RolloutShare.TryToBasisPoints(decimal.Parse(percent, System.Globalization.CultureInfo.InvariantCulture), out var basisPoints);

        Assert.False(converted);
        Assert.Equal(0, basisPoints);
    }

    [Theory]
    [InlineData(0, "0%")]
    [InlineData(1, "0.01%")]
    [InlineData(1_250, "12.5%")]
    [InlineData(10_000, "100%")]
    public void Basis_points_show_as_a_percent(int basisPoints, string expected) =>
        Assert.Equal(expected, RolloutShare.Format(basisPoints));
}
