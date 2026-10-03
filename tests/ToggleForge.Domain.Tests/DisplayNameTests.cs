namespace ToggleForge.Domain.Tests;

public sealed class DisplayNameTests
{
    [Theory]
    [InlineData("A")]
    [InlineData("Studio Game")]
    [InlineData("Jogo da Ação 🎮")]
    public void TryParse_accepts_visible_text(string value)
    {
        Assert.True(DisplayName.TryParse(value, out var name));
        Assert.Equal(value, name.Value);
    }

    [Fact]
    public void TryParse_accepts_100_characters_and_rejects_101()
    {
        Assert.True(DisplayName.TryParse(new string('n', 100), out _));
        Assert.False(DisplayName.TryParse(new string('n', 101), out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Studio\nGame")]
    [InlineData("Studio\0")]
    public void TryParse_rejects_blank_text_and_control_characters(string? value)
    {
        Assert.False(DisplayName.TryParse(value, out var name));
        Assert.Null(name);
    }
}
