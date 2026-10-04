namespace ToggleForge.Evaluation.Tests;

public sealed class FlagEvaluatorTests
{
    private const long SnapshotVersion = 7;

    private const string ShopConfigJson = """{"discount":0.2}""";

    private readonly FlagEvaluator _evaluator = new();

    [Fact]
    public void Missing_snapshot_returns_the_default_value()
    {
        var result = _evaluator.EvaluateBoolean(null, "new-shop", ReferencePlayer(), defaultValue: true);

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

        var result = _evaluator.EvaluateBoolean(snapshot, flagKey, ReferencePlayer(), defaultValue: true);

        AssertResult(result, true, EvaluationReason.InvalidFlagKey, SnapshotVersion);
        Assert.True(result.IsFallback);
    }

    [Fact]
    public void Unknown_flag_returns_the_default_value()
    {
        var snapshot = SnapshotWith(Flag("new-shop", enabled: true, RolloutPercentage.All));

        var result = _evaluator.EvaluateBoolean(snapshot, "battle-pass", ReferencePlayer(), defaultValue: true);

        AssertResult(result, true, EvaluationReason.FlagNotFound, SnapshotVersion);
        Assert.True(result.IsFallback);
    }

    [Fact]
    public void Disabled_flag_returns_the_off_value_even_at_full_rollout()
    {
        var snapshot = SnapshotWith(Flag("new-shop", enabled: false, RolloutPercentage.All));

        var result = _evaluator.EvaluateBoolean(snapshot, "new-shop", ReferencePlayer(), defaultValue: true);

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

    [Theory]
    [InlineData(NewShopFlag.ReferencePlayerBucket + 1, true, EvaluationReason.Included)]
    [InlineData(NewShopFlag.ReferencePlayerBucket, false, EvaluationReason.Excluded)]
    public void Enabled_flag_returns_on_value_to_included_players_and_off_value_to_the_rest(
        int basisPoints, bool expectedValue, EvaluationReason expectedReason)
    {
        var snapshot = SnapshotWith(Flag("new-shop", enabled: true, RolloutPercentage.FromBasisPoints(basisPoints)));

        var result = _evaluator.EvaluateBoolean(snapshot, "new-shop", ReferencePlayer(), defaultValue: false);

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
            new PercentageRollout(RolloutPercentage.FromBasisPoints(basisPoints), NewShopFlag.Seed));

        var result = _evaluator.EvaluateBoolean(SnapshotWith(inverted), "legacy-shop", ReferencePlayer(), defaultValue: false);

        AssertResult(result, expectedValue, expectedReason, SnapshotVersion);
    }

    [Fact]
    public void A_partial_rollout_result_carries_the_player_bucket()
    {
        var snapshot = SnapshotWith(Flag("new-shop", enabled: true, RolloutPercentage.FromPercent(50m)));

        var result = _evaluator.EvaluateBoolean(snapshot, "new-shop", ReferencePlayer(), defaultValue: false);

        Assert.Equal(NewShopFlag.ReferencePlayerBucket, result.Bucket);
    }

    [Theory]
    [InlineData("new-shop", false, 5_000)]
    [InlineData("new-shop", true, 0)]
    [InlineData("new-shop", true, 10_000)]
    [InlineData("battle-pass", true, 5_000)]
    public void A_result_that_does_not_depend_on_the_player_has_no_bucket(string flagKey, bool enabled, int basisPoints)
    {
        var snapshot = SnapshotWith(Flag("new-shop", enabled, RolloutPercentage.FromBasisPoints(basisPoints)));

        var result = _evaluator.EvaluateBoolean(snapshot, flagKey, ReferencePlayer(), defaultValue: false);

        Assert.Null(result.Bucket);
    }

    [Fact]
    public void A_json_flag_asked_as_boolean_falls_back_with_TypeMismatch()
    {
        var config = new JsonFlag(
            FlagKey.Parse("shop-config"), isEnabled: true, onValue: "{}", offValue: "{}",
            new PercentageRollout(RolloutPercentage.All, NewShopFlag.Seed));

        var result = _evaluator.EvaluateBoolean(SnapshotWith(config), "shop-config", ReferencePlayer(), defaultValue: true);

        AssertResult(result, true, EvaluationReason.TypeMismatch, SnapshotVersion);
        Assert.True(result.IsFallback);
        Assert.Null(result.Bucket);
    }

    [Fact]
    public void EvaluateJson_hands_the_published_json_to_the_parser_with_the_snapshot_version()
    {
        var result = _evaluator.EvaluateJson(SnapshotWith(ShopConfig()), "shop-config", ReferencePlayer(), "game-default", json => json);

        Assert.Equal((ShopConfigJson, EvaluationReason.Included, (long?)SnapshotVersion), (result.Value, result.Reason, result.SnapshotVersion));
    }

    [Fact]
    public void EvaluateJson_without_a_snapshot_returns_the_default_without_calling_the_parser()
    {
        var calls = 0;

        var result = _evaluator.EvaluateJson(null, "shop-config", ReferencePlayer(), "game-default", json => { calls++; return json; });

        Assert.Equal(("game-default", EvaluationReason.SnapshotMissing, (long?)null), (result.Value, result.Reason, result.SnapshotVersion));
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData("Shop Config", EvaluationReason.InvalidFlagKey)]
    [InlineData("battle-pass", EvaluationReason.FlagNotFound)]
    [InlineData("new-shop", EvaluationReason.TypeMismatch)]
    public void EvaluateJson_falls_back_without_calling_the_parser_when_no_json_flag_answers(
        string flagKey, EvaluationReason expectedReason)
    {
        var snapshot = SnapshotWith(ShopConfig(), Flag("new-shop", enabled: true, RolloutPercentage.All));
        var calls = 0;

        var result = _evaluator.EvaluateJson(snapshot, flagKey, ReferencePlayer(), "game-default", json => { calls++; return json; });

        Assert.Equal(("game-default", expectedReason, (long?)SnapshotVersion), (result.Value, result.Reason, result.SnapshotVersion));
        Assert.True(result.IsFallback);
        Assert.Equal(0, calls);
    }

    private static void AssertResult(
        EvaluationResult<bool> result, bool expectedValue, EvaluationReason expectedReason, long? expectedVersion)
    {
        Assert.Equal(expectedValue, result.Value);
        Assert.Equal(expectedReason, result.Reason);
        Assert.Equal(expectedVersion, result.SnapshotVersion);
    }

    private static BooleanFlag Flag(string key, bool enabled, RolloutPercentage percentage) =>
        new(
            FlagKey.Parse(key),
            enabled,
            onValue: true,
            offValue: false,
            new PercentageRollout(percentage, NewShopFlag.Seed));

    private static JsonFlag ShopConfig() =>
        new(FlagKey.Parse("shop-config"), isEnabled: true, ShopConfigJson, offValue: "{}",
            new PercentageRollout(RolloutPercentage.All, NewShopFlag.Seed));

    private static ConfigurationSnapshot SnapshotWith(params Flag[] flags) =>
        new(NewShopFlag.Project, NewShopFlag.Environment, SnapshotVersion, flags);

    private static EvaluationContext ReferencePlayer() =>
        EvaluationContext.ForPlayer(PlayerId.Parse(NewShopFlag.ReferencePlayer));
}
