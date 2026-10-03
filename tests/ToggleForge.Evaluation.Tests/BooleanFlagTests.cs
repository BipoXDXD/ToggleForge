namespace ToggleForge.Evaluation.Tests;

/// <summary>A avaliação direta da flag, usada pela simulação do rascunho, que não tem snapshot.</summary>
public sealed class BooleanFlagTests
{
    [Theory]
    [InlineData(NewShopFlag.ReferencePlayerBucket + 1, true, EvaluationReason.Included)]
    [InlineData(NewShopFlag.ReferencePlayerBucket, false, EvaluationReason.Excluded)]
    public void A_partial_rollout_explains_the_result_with_the_player_bucket(
        int basisPoints, bool expectedValue, EvaluationReason expectedReason)
    {
        var flag = NewShop(enabled: true, RolloutPercentage.FromBasisPoints(basisPoints));

        var result = flag.Evaluate(NewShopFlag.Project, NewShopFlag.Environment, ReferencePlayer(), defaultValue: false);

        Assert.Equal(expectedValue, result.Value);
        Assert.Equal(expectedReason, result.Reason);
        Assert.Equal(NewShopFlag.ReferencePlayerBucket, result.Bucket);
        Assert.Null(result.SnapshotVersion);
    }

    [Fact]
    public void A_disabled_flag_has_no_bucket_even_in_a_partial_rollout()
    {
        var flag = NewShop(enabled: false, RolloutPercentage.FromPercent(50m));

        var result = flag.Evaluate(NewShopFlag.Project, NewShopFlag.Environment, ReferencePlayer(), defaultValue: true);

        Assert.False(result.Value);
        Assert.Equal(EvaluationReason.Disabled, result.Reason);
        Assert.Null(result.Bucket);
    }

    [Fact]
    public void The_environment_takes_part_in_the_bucket()
    {
        var flag = NewShop(enabled: true, RolloutPercentage.FromPercent(50m));

        var prod = flag.Evaluate(NewShopFlag.Project, NewShopFlag.Environment, ReferencePlayer(), defaultValue: false);
        var staging = flag.Evaluate(NewShopFlag.Project, EnvironmentKey.Parse("staging"), ReferencePlayer(), defaultValue: false);

        Assert.NotEqual(prod.Bucket, staging.Bucket);
    }

    private static BooleanFlag NewShop(bool enabled, RolloutPercentage percentage) =>
        new(NewShopFlag.Key, enabled, onValue: true, offValue: false, new PercentageRollout(percentage, NewShopFlag.Seed));

    private static EvaluationContext ReferencePlayer() =>
        EvaluationContext.ForPlayer(PlayerId.Parse(NewShopFlag.ReferencePlayer));
}
