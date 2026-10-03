namespace ToggleForge.Evaluation.Tests;

/// <summary>
/// Scripts Unity costumam compilar sem nullable reference types, então null chega à API pública.
/// Cada construtor falha cedo com o nome do parâmetro, em vez de um NullReferenceException adiante.
/// </summary>
public sealed class NullArgumentTests
{
    private static ProjectKey Project => NewShopFlag.Project;

    private static EnvironmentKey Environment => NewShopFlag.Environment;

    private static FlagKey Flag => NewShopFlag.Key;

    private static RolloutSeed Seed => NewShopFlag.Seed;

    private static FlagIdentity Identity => NewShopFlag.Identity;

    private static PercentageRollout Rollout { get; } = new(RolloutPercentage.All, Seed);

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
    public void PercentageRollout_rejects_null_percentage_and_seed()
    {
        AssertNullRejected("percentage", () => new PercentageRollout(null!, Seed));
        AssertNullRejected("seed", () => new PercentageRollout(RolloutPercentage.All, null!));
    }

    [Fact]
    public void PercentageRollout_Decide_rejects_null_flag_and_context()
    {
        AssertNullRejected("flag", () => Rollout.Decide(null!, EvaluationContext.Anonymous));
        AssertNullRejected("context", () => Rollout.Decide(Identity, null!));
    }

    [Fact]
    public void RolloutBucket_Compute_rejects_null_arguments()
    {
        var player = PlayerId.Parse("player");

        AssertNullRejected("flag", () => RolloutBucket.Compute(null!, Seed, player));
        AssertNullRejected("seed", () => RolloutBucket.Compute(Identity, null!, player));
        AssertNullRejected("playerId", () => RolloutBucket.Compute(Identity, Seed, null!));
    }

    [Fact]
    public void FlagEvaluator_rejects_null_context()
    {
        var snapshot = new ConfigurationSnapshot(Project, Environment, 1, []);

        AssertNullRejected("context", () => new FlagEvaluator().EvaluateBoolean(snapshot, "new-shop", null!, false));
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
