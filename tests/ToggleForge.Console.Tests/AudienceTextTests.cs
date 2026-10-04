using ToggleForge.Console.Drafts;

namespace ToggleForge.Console.Tests;

public sealed class AudienceTextTests
{
    [Theory]
    [InlineData("10", true, "The 10% of players inside the rollout.", "The other 90%, and every player while the flag is disabled.")]
    [InlineData("12.5", true, "The 12.5% of players inside the rollout.", "The other 87.5%, and every player while the flag is disabled.")]
    [InlineData("0", true, "No player while the rollout is 0%.", "Every player.")]
    [InlineData("100", true, "Every player while the flag is enabled.", "Every player while the flag is disabled.")]
    [InlineData("10", false, "Nobody while the flag is disabled.", "The other 90%, and every player while the flag is disabled.")]
    [InlineData("0", false, "Nobody while the flag is disabled.", "Every player.")]
    [InlineData("12.345", true, "The players inside the rollout.", "Everyone else, and every player while the flag is disabled.")]
    public void Each_value_says_who_gets_it(string percent, bool enabled, string included, string others)
    {
        var rollout = decimal.Parse(percent, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(included, AudienceText.Included(rollout, enabled));
        Assert.Equal(others, AudienceText.Others(rollout));
    }
}
