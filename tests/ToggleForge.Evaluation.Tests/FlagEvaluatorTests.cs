namespace ToggleForge.Evaluation.Tests;

public sealed class FlagEvaluatorTests
{
    private const long SnapshotVersion = 7;

    private readonly FlagEvaluator _evaluator = new();

    [Fact]
    public void Missing_snapshot_returns_the_default_value()
    {
        var result = _evaluator.EvaluateBoolean(null, "new-shop", AnyPlayer(), defaultValue: true);

        AssertResult(result, true, EvaluationReason.SnapshotMissing, expectedVersion: null);
        Assert.True(result.IsFallback);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("New Shop")]
    public void Invalid_flag_key_returns_the_default_value(string? flagKey)
    {
        var snapshot = SnapshotWith(Flag("new-shop", enabled: true, RolloutPercentage.All));

        var result = _evaluator.EvaluateBoolean(snapshot, flagKey, AnyPlayer(), defaultValue: true);

        AssertResult(result, true, EvaluationReason.InvalidFlagKey, SnapshotVersion);
        Assert.True(result.IsFallback);
    }

    [Fact]
    public void Unknown_flag_returns_the_default_value()
    {
        var snapshot = SnapshotWith(Flag("new-shop", enabled: true, RolloutPercentage.All));

        var result = _evaluator.EvaluateBoolean(snapshot, "battle-pass", AnyPlayer(), defaultValue: true);

        AssertResult(result, true, EvaluationReason.FlagNotFound, SnapshotVersion);
        Assert.True(result.IsFallback);
    }

    [Fact]
    public void Disabled_flag_returns_the_off_value_even_at_full_rollout()
    {
        var snapshot = SnapshotWith(Flag("new-shop", enabled: false, RolloutPercentage.All));

        var result = _evaluator.EvaluateBoolean(snapshot, "new-shop", AnyPlayer(), defaultValue: true);

        AssertResult(result, false, EvaluationReason.Disabled, SnapshotVersion);
        Assert.False(result.IsFallback);
    }

    [Fact]
    public void Partial_rollout_without_player_id_returns_the_default_value()
    {
        var snapshot = SnapshotWith(Flag("new-shop", enabled: true, RolloutPercentage.FromPercent(10m)));

        var result = _evaluator.EvaluateBoolean(snapshot, "new-shop", EvaluationContext.Anonymous, defaultValue: true);

        AssertResult(result, true, EvaluationReason.MissingPlayerId, SnapshotVersion);
        Assert.True(result.IsFallback);
    }

    // player-1 cai no bucket 7370 para studio-game/prod/new-shop/seed-1 (Vectors/rollout-v1.json).
    [Theory]
    [InlineData(7_371, true, EvaluationReason.Included)]
    [InlineData(7_370, false, EvaluationReason.Excluded)]
    public void Enabled_flag_returns_on_value_to_included_players_and_off_value_to_the_rest(
        int basisPoints, bool expectedValue, EvaluationReason expectedReason)
    {
        var snapshot = SnapshotWith(Flag("new-shop", enabled: true, RolloutPercentage.FromBasisPoints(basisPoints)));

        var result = _evaluator.EvaluateBoolean(snapshot, "new-shop", Player("player-1"), defaultValue: false);

        AssertResult(result, expectedValue, expectedReason, SnapshotVersion);
        Assert.False(result.IsFallback);
    }

    [Theory]
    [InlineData(10_000, false, EvaluationReason.Included)]
    [InlineData(0, true, EvaluationReason.Excluded)]
    public void Inverted_flag_returns_its_configured_values(
        int basisPoints, bool expectedValue, EvaluationReason expectedReason)
    {
        var inverted = new BooleanFlag(
            FlagKey.Parse("legacy-shop"),
            isEnabled: true,
            onValue: false,
            offValue: true,
            new PercentageRollout(RolloutPercentage.FromBasisPoints(basisPoints), "seed-1"));

        var result = _evaluator.EvaluateBoolean(SnapshotWith(inverted), "legacy-shop", AnyPlayer(), defaultValue: false);

        AssertResult(result, expectedValue, expectedReason, SnapshotVersion);
    }

    [Fact]
    public void Evaluation_rejects_null_context()
    {
        var snapshot = SnapshotWith(Flag("new-shop", enabled: true, RolloutPercentage.All));

        // Consumidores sem nullable reference types (scripts Unity) podem passar null.
        Assert.Throws<ArgumentNullException>(() => _evaluator.EvaluateBoolean(snapshot, "new-shop", null!, defaultValue: false));
    }

    [Fact]
    public void Snapshot_rejects_duplicate_flag_keys()
    {
        var duplicate = Flag("new-shop", enabled: true, RolloutPercentage.All);

        var error = Assert.Throws<ArgumentException>(() => SnapshotWith(duplicate, duplicate));

        Assert.Equal("flags", error.ParamName);
    }

    [Fact]
    public void Snapshot_rejects_null_flags_in_the_list()
    {
        var error = Assert.Throws<ArgumentException>(() => SnapshotWith([null!]));

        Assert.Equal("flags", error.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Snapshot_rejects_versions_below_1(long version)
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => new ConfigurationSnapshot(
            ProjectKey.Parse("studio-game"), EnvironmentKey.Parse("prod"), version, []));

        Assert.Equal("version", error.ParamName);
    }

    [Fact]
    public void Snapshot_keeps_its_flags_when_the_source_list_changes()
    {
        var flags = new List<BooleanFlag> { Flag("new-shop", enabled: true, RolloutPercentage.All) };
        var snapshot = new ConfigurationSnapshot(ProjectKey.Parse("studio-game"), EnvironmentKey.Parse("prod"), 1, flags);

        flags.Clear();

        Assert.Equal(EvaluationReason.Included, _evaluator.EvaluateBoolean(snapshot, "new-shop", AnyPlayer(), false).Reason);
    }

    private static void AssertResult(
        EvaluationResult<bool> result, bool expectedValue, EvaluationReason expectedReason, long? expectedVersion)
    {
        Assert.Equal(expectedValue, result.Value);
        Assert.Equal(expectedReason, result.Reason);
        Assert.Equal(expectedVersion, result.SnapshotVersion);
    }

    private static BooleanFlag Flag(string key, bool enabled, RolloutPercentage percentage) =>
        new(FlagKey.Parse(key), enabled, onValue: true, offValue: false, new PercentageRollout(percentage, "seed-1"));

    private static ConfigurationSnapshot SnapshotWith(params BooleanFlag[] flags) =>
        new(ProjectKey.Parse("studio-game"), EnvironmentKey.Parse("prod"), SnapshotVersion, flags);

    private static EvaluationContext Player(string playerId) => EvaluationContext.ForPlayer(PlayerId.Parse(playerId));

    private static EvaluationContext AnyPlayer() => Player("player-1");
}
