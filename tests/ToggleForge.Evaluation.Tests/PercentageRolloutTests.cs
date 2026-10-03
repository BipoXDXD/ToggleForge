namespace ToggleForge.Evaluation.Tests;

public sealed class PercentageRolloutTests
{
    private static RolloutSeed Seed => NewShopFlag.Seed;

    private static FlagIdentity NewShop => NewShopFlag.Identity;

    [Fact]
    public void Zero_percent_excludes_every_player()
    {
        var rollout = new PercentageRollout(RolloutPercentage.None, Seed);

        Assert.All(SamplePlayers(10_000), player => Assert.Equal(TargetingDecision.Excluded, rollout.Decide(NewShop, player).Decision));
    }

    [Fact]
    public void Hundred_percent_includes_every_player()
    {
        var rollout = new PercentageRollout(RolloutPercentage.All, Seed);

        Assert.All(SamplePlayers(10_000), player => Assert.Equal(TargetingDecision.Included, rollout.Decide(NewShop, player).Decision));
    }

    [Theory]
    [InlineData(0, TargetingDecision.Excluded)]
    [InlineData(10_000, TargetingDecision.Included)]
    public void Extremes_do_not_need_a_player_id(int basisPoints, TargetingDecision expected)
    {
        var rollout = new PercentageRollout(RolloutPercentage.FromBasisPoints(basisPoints), Seed);

        Assert.Equal(expected, rollout.Decide(NewShop, EvaluationContext.Anonymous).Decision);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5_000)]
    [InlineData(9_999)]
    public void Partial_rollout_requires_a_player_id(int basisPoints)
    {
        var rollout = new PercentageRollout(RolloutPercentage.FromBasisPoints(basisPoints), Seed);

        var outcome = rollout.Decide(NewShop, EvaluationContext.Anonymous);

        Assert.Equal(TargetingDecision.PlayerIdRequired, outcome.Decision);
        Assert.Null(outcome.Bucket);
    }

    [Theory]
    [InlineData(NewShopFlag.ReferencePlayerBucket, TargetingDecision.Excluded)]
    [InlineData(NewShopFlag.ReferencePlayerBucket + 1, TargetingDecision.Included)]
    public void Player_is_included_only_when_bucket_is_below_the_percentage(int basisPoints, TargetingDecision expected)
    {
        var rollout = new PercentageRollout(RolloutPercentage.FromBasisPoints(basisPoints), Seed);

        Assert.Equal(expected, rollout.Decide(NewShop, EvaluationContext.ForPlayer(PlayerId.Parse(NewShopFlag.ReferencePlayer))).Decision);
    }

    [Theory]
    [InlineData(NewShopFlag.ReferencePlayerBucket)]
    [InlineData(NewShopFlag.ReferencePlayerBucket + 1)]
    public void A_partial_rollout_reports_the_bucket_that_decided(int basisPoints)
    {
        var rollout = new PercentageRollout(RolloutPercentage.FromBasisPoints(basisPoints), Seed);

        var outcome = rollout.Decide(NewShop, EvaluationContext.ForPlayer(PlayerId.Parse(NewShopFlag.ReferencePlayer)));

        Assert.Equal(NewShopFlag.ReferencePlayerBucket, outcome.Bucket);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10_000)]
    public void Extremes_report_no_bucket_even_with_a_player(int basisPoints)
    {
        var rollout = new PercentageRollout(RolloutPercentage.FromBasisPoints(basisPoints), Seed);

        var outcome = rollout.Decide(NewShop, EvaluationContext.ForPlayer(PlayerId.Parse(NewShopFlag.ReferencePlayer)));

        Assert.Null(outcome.Bucket);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)]
    [InlineData(1_000, 2_000)]
    [InlineData(5_000, 9_999)]
    [InlineData(9_999, 10_000)]
    public void Widening_the_rollout_keeps_every_player_already_included(int fromBasisPoints, int toBasisPoints)
    {
        var narrow = new PercentageRollout(RolloutPercentage.FromBasisPoints(fromBasisPoints), Seed);
        var wide = new PercentageRollout(RolloutPercentage.FromBasisPoints(toBasisPoints), Seed);

        var lostPlayers = SamplePlayers(10_000).Count(player =>
            narrow.Decide(NewShop, player).Decision == TargetingDecision.Included
            && wide.Decide(NewShop, player).Decision != TargetingDecision.Included);

        Assert.Equal(0, lostPlayers);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(1_000)]
    [InlineData(5_000)]
    public void Included_share_approximates_the_percentage(int basisPoints)
    {
        const int PlayerCount = 100_000;
        const int ToleranceInPlayers = 1_000; // ±1 ponto percentual: > 6 desvios-padrão da binomial no pior caso (50%).
        var rollout = new PercentageRollout(RolloutPercentage.FromBasisPoints(basisPoints), Seed);

        var included = SamplePlayers(PlayerCount).Count(player => rollout.Decide(NewShop, player).Decision == TargetingDecision.Included);

        var expected = PlayerCount * basisPoints / RolloutPercentage.MaxBasisPoints;
        Assert.InRange(included, expected - ToleranceInPlayers, expected + ToleranceInPlayers);
    }

    [Fact]
    public void Same_player_always_gets_the_same_decision()
    {
        var rollout = new PercentageRollout(RolloutPercentage.FromPercent(50m), Seed);
        var player = EvaluationContext.ForPlayer(PlayerId.Parse("player-42"));

        var decisions = Enumerable.Range(0, 100).Select(_ => rollout.Decide(NewShop, player).Decision).Distinct();

        Assert.Single(decisions);
    }

    private static IEnumerable<EvaluationContext> SamplePlayers(int count) =>
        Enumerable.Range(1, count).Select(n => EvaluationContext.ForPlayer(PlayerId.Parse($"player-{n}")));
}
