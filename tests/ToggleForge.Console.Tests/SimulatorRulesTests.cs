using ToggleForge.Console.Api;
using ToggleForge.Console.Simulations;

namespace ToggleForge.Console.Tests;

public sealed class SimulatorRulesTests
{
    [Theory]
    [InlineData("disabled", null, "Disabled: everyone gets the off value.")]
    [InlineData("included", 0, "Bucket 0 is inside the rollout.")]
    [InlineData("included", null, "The rollout includes everyone.")]
    [InlineData("excluded", 9999, "Bucket 9999 is outside the rollout.")]
    [InlineData("excluded", null, "The rollout includes no one.")]
    [InlineData("segment-matched", 120, "segment-matched")]
    public void Each_result_says_what_decided_it(string reason, int? bucket, string expected) =>
        Assert.Equal(expected, SimulationText.Why(new SimulationResult("new-shop", Value: true, reason, bucket)));

    [Theory]
    [InlineData(null, "Draft")]
    [InlineData(12L, "Version 12")]
    public void The_source_is_the_draft_or_a_version(long? version, string expected) =>
        Assert.Equal(expected, SimulationText.Source(new Simulation(version, [])));
}
