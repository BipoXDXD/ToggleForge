using ToggleForge.Console.Api;
using ToggleForge.Console.Environments;

namespace ToggleForge.Console.Tests;

public sealed class EnvironmentTextTests
{
    [Fact]
    public void Before_the_environment_loads_the_title_is_the_key_from_the_address() =>
        Assert.Equal("prod", EnvironmentText.Title(loaded: null, "prod"));

    [Fact]
    public void A_loaded_environment_is_titled_by_its_name_and_key() =>
        Assert.Equal(
            "Production (prod)",
            EnvironmentText.Title(new ProjectEnvironment("prod", "Production", CurrentVersion: 3, HasUnpublishedChanges: false), "prod"));
}
