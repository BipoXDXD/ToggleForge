using ToggleForge.Console.Api;
using ToggleForge.Console.Drafts;

namespace ToggleForge.Console.Tests;

public sealed class PublicationAttemptTests
{
    [Fact]
    public void An_attempt_publishes_the_revision_reviewed_with_its_summary()
    {
        var attempt = PublicationAttempt.Of(7, "  Open the shop to 25%  ");

        Assert.Equal(new PublicationRequest(7, "Open the shop to 25%"), attempt.Request);
        Assert.Matches("^[A-Za-z0-9_-]{16,64}$", attempt.IdempotencyKey);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_summary_is_left_out(string? summary) =>
        Assert.Null(PublicationAttempt.Of(7, summary).Request.Summary);

    [Fact]
    public void Each_attempt_has_its_own_key() =>
        Assert.NotEqual(PublicationAttempt.Of(7, null).IdempotencyKey, PublicationAttempt.Of(7, null).IdempotencyKey);

    [Theory]
    [InlineData("/problems/unreachable", 0, PublicationOutcome.Unknown)]
    [InlineData("/problems/unexpected", 500, PublicationOutcome.Unknown)]
    [InlineData("/problems/unexpected-response", 502, PublicationOutcome.Unknown)]
    [InlineData("/problems/unexpected-response", 200, PublicationOutcome.Unknown)]
    [InlineData("/problems/draft-changed", 409, PublicationOutcome.DraftChanged)]
    [InlineData("/problems/nothing-to-publish", 409, PublicationOutcome.NothingToPublish)]
    [InlineData("/problems/idempotency-key-reused", 409, PublicationOutcome.Refused)]
    [InlineData("/problems/forbidden", 403, PublicationOutcome.Refused)]
    [InlineData("/problems/too-many-requests", 429, PublicationOutcome.Refused)]
    [InlineData("/problems/invalid-request", 400, PublicationOutcome.Refused)]
    public void Only_a_missing_or_failed_answer_leaves_the_result_unknown(string type, int status, PublicationOutcome expected) =>
        Assert.Equal(expected, PublicationAttempt.Classify(new Problem(type, "Title", status)));
}
