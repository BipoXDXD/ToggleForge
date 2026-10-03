namespace ToggleForge.Evaluation.Tests;

public sealed class TargetingOutcomeTests
{
    [Theory]
    [InlineData(0, true, TargetingDecision.Included)]
    [InlineData(9_999, false, TargetingDecision.Excluded)]
    public void A_bucketed_outcome_keeps_the_bucket_and_the_decision(int bucket, bool isIncluded, TargetingDecision expected)
    {
        var outcome = TargetingOutcome.Bucketed(bucket, isIncluded);

        Assert.Equal(expected, outcome.Decision);
        Assert.Equal(bucket, outcome.Bucket);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10_000)]
    public void A_bucket_outside_0_to_9999_is_rejected(int bucket)
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => TargetingOutcome.Bucketed(bucket, isIncluded: true));

        Assert.Equal("bucket", error.ParamName);
    }

    [Theory]
    [InlineData(TargetingDecision.Included)]
    [InlineData(TargetingDecision.PlayerIdRequired)]
    public void An_unbucketed_outcome_has_no_bucket(TargetingDecision decision)
    {
        var outcome = TargetingOutcome.Unbucketed(decision);

        Assert.Equal(decision, outcome.Decision);
        Assert.Null(outcome.Bucket);
    }
}
