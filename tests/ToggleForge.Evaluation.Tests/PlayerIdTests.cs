namespace ToggleForge.Evaluation.Tests;

public sealed class PlayerIdTests
{
    [Theory]
    [InlineData("p")]
    [InlineData("player-42")]
    [InlineData("Jogador Ação 🎮")]
    [InlineData("  padded  ")]
    public void TryParse_accepts_any_visible_identifier(string value)
    {
        Assert.True(PlayerId.TryParse(value, out var playerId));
        Assert.Equal(value, playerId.Value);
    }

    [Fact]
    public void TryParse_accepts_identifier_with_exactly_256_characters()
    {
        Assert.True(PlayerId.TryParse(new string('x', 256), out _));
    }

    [Fact]
    public void TryParse_rejects_identifier_with_257_characters()
    {
        Assert.False(PlayerId.TryParse(new string('x', 257), out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("player\0")]
    [InlineData("player\n42")]
    [InlineData("\u001b[31m")]
    public void TryParse_rejects_empty_whitespace_and_control_characters(string? value)
    {
        var parsed = PlayerId.TryParse(value, out var playerId);

        Assert.False(parsed);
        Assert.Null(playerId);
    }

    [Fact]
    public void Parse_throws_ArgumentException_for_invalid_identifier()
    {
        var error = Assert.Throws<ArgumentException>(() => PlayerId.Parse(""));

        Assert.Equal("value", error.ParamName);
    }
}
