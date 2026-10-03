namespace ToggleForge.Evaluation.Tests;

/// <summary>
/// A validação de ID de jogador e de seed faz parte do contrato do rollout: um SDK que aceite IDs
/// diferentes mandaria jogadores para o fallback em vez do bucket. O oráculo Python decide cada caso.
/// </summary>
public sealed class OpaqueTextContractTests
{
    public static TheoryData<string, int[], bool> PlayerIdCases => RolloutVectors.OpaqueTextCases("playerIdCases");

    public static TheoryData<string, int[], bool> SeedCases => RolloutVectors.OpaqueTextCases("seedCases");

    [Theory]
    [MemberData(nameof(PlayerIdCases))]
    public void PlayerId_validation_matches_the_reference(string name, int[] codeUnits, bool expectedValid)
    {
        var parsed = PlayerId.TryParse(RolloutVectors.FromCodeUnits(codeUnits), out _);

        Assert.True(expectedValid == parsed, $"Case '{name}': expected valid={expectedValid}.");
    }

    [Theory]
    [MemberData(nameof(SeedCases))]
    public void RolloutSeed_validation_matches_the_reference(string name, int[] codeUnits, bool expectedValid)
    {
        var parsed = RolloutSeed.TryParse(RolloutVectors.FromCodeUnits(codeUnits), out _);

        Assert.True(expectedValid == parsed, $"Case '{name}': expected valid={expectedValid}.");
    }

    [Fact]
    public void RolloutSeed_Parse_throws_ArgumentException_for_invalid_seed()
    {
        var error = Assert.Throws<ArgumentException>(() => RolloutSeed.Parse(" "));

        Assert.Equal("value", error.ParamName);
    }

    [Fact]
    public void RolloutSeeds_with_same_value_are_equal()
    {
        Assert.Equal(RolloutSeed.Parse("seed-1"), RolloutSeed.Parse("seed-1"));
    }
}
