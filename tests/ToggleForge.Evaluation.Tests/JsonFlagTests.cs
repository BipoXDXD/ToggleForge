namespace ToggleForge.Evaluation.Tests;

/// <summary>A flag JSON avaliada direto, como a simulação do rascunho faz.</summary>
public sealed class JsonFlagTests
{
    private const string OnJson = """{"discount":0.2}""";
    private const string OffJson = """{"discount":0}""";
    private const string DefaultValue = "game-default";

    [Theory]
    [InlineData(NewShopFlag.ReferencePlayerBucket + 1, OnJson, EvaluationReason.Included)]
    [InlineData(NewShopFlag.ReferencePlayerBucket, OffJson, EvaluationReason.Excluded)]
    public void A_partial_rollout_hands_the_chosen_json_to_the_parser_with_the_bucket_of_the_rollout_vectors(
        int basisPoints, string expectedJson, EvaluationReason expectedReason)
    {
        var flag = ShopConfig(enabled: true, RolloutPercentage.FromBasisPoints(basisPoints));

        var result = flag.Evaluate(NewShopFlag.Project, NewShopFlag.Environment, ReferencePlayer(), DefaultValue, json => json);

        Assert.Equal(expectedJson, result.Value);
        Assert.Equal(expectedReason, result.Reason);
        Assert.Equal(NewShopFlag.ReferencePlayerBucket, result.Bucket);
        Assert.Null(result.SnapshotVersion);
        Assert.False(result.IsFallback);
    }

    [Fact]
    public void A_disabled_flag_hands_the_off_json_to_the_parser()
    {
        var flag = ShopConfig(enabled: false, RolloutPercentage.All);

        var result = flag.Evaluate(NewShopFlag.Project, NewShopFlag.Environment, ReferencePlayer(), DefaultValue, json => json);

        Assert.Equal((OffJson, EvaluationReason.Disabled, (int?)null), (result.Value, result.Reason, result.Bucket));
    }

    [Fact]
    public void The_result_is_what_the_parser_returns_not_the_raw_json()
    {
        var flag = ShopConfig(enabled: false, RolloutPercentage.All);

        var result = flag.Evaluate(NewShopFlag.Project, NewShopFlag.Environment, ReferencePlayer(), defaultValue: -1, json => json.Length);

        Assert.Equal((14, EvaluationReason.Disabled), (result.Value, result.Reason));
    }

    [Fact]
    public void A_json_null_value_reaches_the_parser_like_any_other()
    {
        var flag = new JsonFlag(NewShopFlag.Key, isEnabled: true, onValue: "null", offValue: OffJson, Rollout(RolloutPercentage.All));
        string? received = null;

        var result = flag.Evaluate(
            NewShopFlag.Project, NewShopFlag.Environment, ReferencePlayer(), DefaultValue, json => received = json);

        Assert.Equal("null", received);
        Assert.Equal(("null", EvaluationReason.Included), (result.Value, result.Reason));
    }

    [Fact]
    public void Without_a_player_id_a_partial_rollout_returns_the_default_without_calling_the_parser()
    {
        var flag = ShopConfig(enabled: true, RolloutPercentage.FromPercent(50m));
        var calls = 0;

        var result = flag.Evaluate(
            NewShopFlag.Project, NewShopFlag.Environment, EvaluationContext.Anonymous, DefaultValue, json => { calls++; return json; });

        Assert.Equal((DefaultValue, EvaluationReason.MissingPlayerId), (result.Value, result.Reason));
        Assert.True(result.IsFallback);
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData("format")]
    [InlineData("state")]
    [InlineData("argument")]
    public void A_parser_that_throws_falls_back_with_InvalidValue(string failure)
    {
        Exception exception = failure switch
        {
            "format" => new FormatException("not a number"),
            "state" => new InvalidOperationException("missing field"),
            _ => new ArgumentException("bad shape"),
        };
        // Rollout parcial que inclui o jogador: sem o fallback, o resultado traria o bucket dele.
        var flag = ShopConfig(enabled: true, RolloutPercentage.FromBasisPoints(NewShopFlag.ReferencePlayerBucket + 1));

        var result = flag.Evaluate<string>(
            NewShopFlag.Project, NewShopFlag.Environment, ReferencePlayer(), DefaultValue, _ => throw exception);

        Assert.Equal((DefaultValue, EvaluationReason.InvalidValue, (int?)null), (result.Value, result.Reason, result.Bucket));
        Assert.True(result.IsFallback);
    }

    private static JsonFlag ShopConfig(bool enabled, RolloutPercentage percentage) =>
        new(NewShopFlag.Key, enabled, OnJson, OffJson, Rollout(percentage));

    private static PercentageRollout Rollout(RolloutPercentage percentage) => new(percentage, NewShopFlag.Seed);

    private static EvaluationContext ReferencePlayer() =>
        EvaluationContext.ForPlayer(PlayerId.Parse(NewShopFlag.ReferencePlayer));
}
