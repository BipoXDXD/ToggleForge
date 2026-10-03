namespace ToggleForge.Evaluation.Tests;

public sealed class KeyParsingTests
{
    [Theory]
    [InlineData("a")]
    [InlineData("0")]
    [InlineData("new-shop")]
    [InlineData("shop.v2_beta")]
    [InlineData("0123456789012345678901234567890123456789012345678901234567890123")]
    public void FlagKey_accepts_lowercase_keys_up_to_64_characters(string value)
    {
        Assert.True(FlagKey.TryParse(value, out var key));
        Assert.Equal(value, key.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("New-Shop")]
    [InlineData("-shop")]
    [InlineData(".shop")]
    [InlineData("_shop")]
    [InlineData("new shop")]
    [InlineData("shop/v2")]
    [InlineData("loja-é")]
    [InlineData("shop\0")]
    [InlineData("01234567890123456789012345678901234567890123456789012345678901234")]
    public void FlagKey_rejects_empty_uppercase_symbols_and_keys_longer_than_64(string? value)
    {
        var parsed = FlagKey.TryParse(value, out var key);

        Assert.False(parsed);
        Assert.Null(key);
    }

    [Fact]
    public void FlagKey_Parse_throws_ArgumentException_for_invalid_key()
    {
        var error = Assert.Throws<ArgumentException>(() => FlagKey.Parse("Bad Key"));

        Assert.Equal("value", error.ParamName);
    }

    [Fact]
    public void FlagKeys_with_same_value_are_equal()
    {
        Assert.Equal(FlagKey.Parse("new-shop"), FlagKey.Parse("new-shop"));
    }

    [Fact]
    public void FlagKey_ToString_returns_the_raw_value()
    {
        Assert.Equal("new-shop", FlagKey.Parse("new-shop").ToString());
    }

    [Theory]
    [InlineData("studio-game", true)]
    [InlineData("Studio-Game", false)]
    [InlineData("", false)]
    public void ProjectKey_follows_the_key_format(string value, bool expected)
    {
        Assert.Equal(expected, ProjectKey.TryParse(value, out _));
    }

    [Theory]
    [InlineData("prod", true)]
    [InlineData("PROD", false)]
    [InlineData("", false)]
    public void EnvironmentKey_follows_the_key_format(string value, bool expected)
    {
        Assert.Equal(expected, EnvironmentKey.TryParse(value, out _));
    }
}
