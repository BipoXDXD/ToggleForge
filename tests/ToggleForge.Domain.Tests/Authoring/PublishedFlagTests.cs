using ToggleForge.Domain.Authoring;
using ToggleForge.Evaluation;

namespace ToggleForge.Domain.Tests.Authoring;

public sealed class PublishedFlagTests
{
    private static readonly FlagKey _newShop = FlagKey.Parse("new-shop");

    [Fact]
    public void A_published_boolean_flag_keeps_its_state_and_each_value_on_its_side()
    {
        var published = new PublishedFlag(
            _newShop, new FlagSettings(IsEnabled: false, new BooleanValues(On: true, Off: false), RolloutPercentage.All), RolloutSeed.Parse("s1"));

        var flag = Assert.IsType<BooleanFlag>(published.ToFlag());

        Assert.Equal((false, true, false), (flag.IsEnabled, flag.OnValue, flag.OffValue));
    }

    [Fact]
    public void A_published_json_flag_keeps_its_state_and_each_value_on_its_side()
    {
        var values = new JsonValues(JsonFlagValue.Parse("{\"discount\":0.2}"), JsonFlagValue.Parse("{}"));
        var published = new PublishedFlag(_newShop, new FlagSettings(IsEnabled: false, values, RolloutPercentage.All), RolloutSeed.Parse("s1"));

        var flag = Assert.IsType<JsonFlag>(published.ToFlag());

        Assert.Equal((false, "{\"discount\":0.2}", "{}"), (flag.IsEnabled, flag.OnValue, flag.OffValue));
    }
}
