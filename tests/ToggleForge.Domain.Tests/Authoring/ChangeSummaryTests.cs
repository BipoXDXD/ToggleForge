using ToggleForge.Domain.Authoring;

namespace ToggleForge.Domain.Tests.Authoring;

public sealed class ChangeSummaryTests
{
    [Fact]
    public void TryParse_accepts_1_to_280_characters()
    {
        Assert.True(ChangeSummary.TryParse("x", out _));
        Assert.True(ChangeSummary.TryParse(new string('x', 280), out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("line\nbreak")]
    public void TryParse_rejects_empty_text_and_control_characters(string? value)
    {
        Assert.False(ChangeSummary.TryParse(value, out _));
    }

    [Fact]
    public void TryParse_rejects_281_characters()
    {
        Assert.False(ChangeSummary.TryParse(new string('x', 281), out _));
    }
}
