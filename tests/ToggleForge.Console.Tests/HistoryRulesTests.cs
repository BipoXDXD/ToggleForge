using ToggleForge.Console.Api;
using ToggleForge.Console.Drafts;
using ToggleForge.Console.History;

namespace ToggleForge.Console.Tests;

public sealed class HistoryRulesTests
{
    [Fact]
    public void A_publication_time_is_written_in_utc() =>
        Assert.Equal("2026-10-03 15:04 UTC", PublicationText.When(new DateTimeOffset(2026, 10, 3, 12, 4, 59, TimeSpan.FromHours(-3))));

    [Theory]
    [InlineData("rollback", 2L, "Rollback to version 2")]
    [InlineData("publication", null, "Publication")]
    [InlineData("scheduled", null, "Publication")]
    public void The_origin_says_which_version_a_rollback_restored(string origin, long? restored, string expected) =>
        Assert.Equal(expected, PublicationText.Origin(origin, restored));

    [Fact]
    public void A_rollback_attempt_carries_what_the_member_saw()
    {
        var attempt = RollbackAttempt.Of(targetVersion: 2, expectedCurrentVersion: 5, discardDraftChanges: true);

        Assert.Equal(new RollbackRequest(2, 5, true), attempt.Request);
        Assert.Matches("^[0-9a-f]{32}$", attempt.IdempotencyKey);
    }

    [Theory]
    [InlineData("/problems/unreachable", 0, RollbackOutcome.Unknown)]
    [InlineData("/problems/unexpected", 500, RollbackOutcome.Unknown)]
    [InlineData("/problems/current-version-changed", 409, RollbackOutcome.CurrentVersionChanged)]
    [InlineData("/problems/draft-has-pending-changes", 409, RollbackOutcome.DraftHasPendingChanges)]
    [InlineData("/problems/already-current", 409, RollbackOutcome.Refused)]
    [InlineData("/problems/idempotency-key-reused", 409, RollbackOutcome.Refused)]
    [InlineData("/problems/invalid-request", 400, RollbackOutcome.Refused)]
    public void Each_refusal_of_a_rollback_has_its_way_out(string type, int status, RollbackOutcome expected) =>
        Assert.Equal(expected, RollbackAttempt.Classify(new Problem(type, "Title", status)));
}
