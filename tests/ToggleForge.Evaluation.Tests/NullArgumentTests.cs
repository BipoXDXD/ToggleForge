namespace ToggleForge.Evaluation.Tests;

/// <summary>
/// Scripts Unity costumam compilar sem nullable reference types, então null chega à API pública.
/// Cada construtor falha cedo com o nome do parâmetro, em vez de um NullReferenceException adiante.
/// </summary>
public sealed class NullArgumentTests
{
    private static ProjectKey Project { get; } = ProjectKey.Parse("studio-game");
    private static EnvironmentKey Environment { get; } = EnvironmentKey.Parse("prod");
    private static FlagKey Flag { get; } = FlagKey.Parse("new-shop");
    private static PercentageRollout Rollout { get; } = new(RolloutPercentage.All, RolloutSeed.Parse("seed-1"));

    [Fact]
    public void FlagIdentity_rejects_null_keys()
    {
        AssertNullRejected("projectKey", () => new FlagIdentity(null!, Environment, Flag));
        AssertNullRejected("environmentKey", () => new FlagIdentity(Project, null!, Flag));
        AssertNullRejected("flagKey", () => new FlagIdentity(Project, Environment, null!));
    }

    [Fact]
    public void EvaluationContext_ForPlayer_rejects_null_player_id()
    {
        AssertNullRejected("playerId", () => EvaluationContext.ForPlayer(null!));
    }

    [Fact]
    public void PercentageRollout_rejects_null_percentage()
    {
        AssertNullRejected("percentage", () => new PercentageRollout(null!, RolloutSeed.Parse("seed-1")));
    }

    [Fact]
    public void BooleanFlag_rejects_null_key_and_targeting()
    {
        AssertNullRejected("key", () => new BooleanFlag(null!, true, true, false, Rollout));
        AssertNullRejected("targeting", () => new BooleanFlag(Flag, true, true, false, null!));
    }

    [Fact]
    public void ConfigurationSnapshot_rejects_null_keys_and_flag_list()
    {
        AssertNullRejected("projectKey", () => new ConfigurationSnapshot(null!, Environment, 1, []));
        AssertNullRejected("environmentKey", () => new ConfigurationSnapshot(Project, null!, 1, []));
        AssertNullRejected("flags", () => new ConfigurationSnapshot(Project, Environment, 1, null!));
    }

    private static void AssertNullRejected(string parameterName, Func<object> construct)
    {
        var error = Assert.Throws<ArgumentNullException>(construct);

        Assert.Equal(parameterName, error.ParamName);
    }
}
