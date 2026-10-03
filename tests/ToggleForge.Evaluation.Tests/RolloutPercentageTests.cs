namespace ToggleForge.Evaluation.Tests;

public sealed class RolloutPercentageTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(1_000)]
    [InlineData(9_999)]
    [InlineData(10_000)]
    public void FromBasisPoints_accepts_values_from_0_to_10000(int basisPoints)
    {
        Assert.Equal(basisPoints, RolloutPercentage.FromBasisPoints(basisPoints).BasisPoints);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10_001)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void FromBasisPoints_rejects_values_outside_0_to_10000(int basisPoints)
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => RolloutPercentage.FromBasisPoints(basisPoints));

        Assert.Equal("basisPoints", error.ParamName);
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(10_000, true)]
    [InlineData(10_001, false)]
    public void TryFromBasisPoints_reports_whether_the_value_is_in_range(int basisPoints, bool expected)
    {
        Assert.Equal(expected, RolloutPercentage.TryFromBasisPoints(basisPoints, out _));
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("0.01", 1)]
    [InlineData("10", 1_000)]
    [InlineData("12.5", 1_250)]
    [InlineData("99.99", 9_999)]
    [InlineData("100", 10_000)]
    public void FromPercent_converts_to_basis_points(string percent, int expectedBasisPoints)
    {
        var rollout = RolloutPercentage.FromPercent(decimal.Parse(percent, System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(expectedBasisPoints, rollout.BasisPoints);
    }

    [Theory]
    [InlineData("-0.01")]
    [InlineData("100.01")]
    [InlineData("12.345")]
    public void FromPercent_rejects_out_of_range_or_finer_than_one_basis_point(string percent)
    {
        var value = decimal.Parse(percent, System.Globalization.CultureInfo.InvariantCulture);

        var error = Assert.Throws<ArgumentOutOfRangeException>(() => RolloutPercentage.FromPercent(value));

        Assert.Equal("percent", error.ParamName);
    }

    [Fact]
    public void None_and_All_are_the_extremes()
    {
        Assert.Equal(0, RolloutPercentage.None.BasisPoints);
        Assert.Equal(10_000, RolloutPercentage.All.BasisPoints);
    }

    [Fact]
    public void Percentages_with_same_basis_points_are_equal()
    {
        Assert.Equal(RolloutPercentage.FromPercent(10m), RolloutPercentage.FromBasisPoints(1_000));
    }
}
