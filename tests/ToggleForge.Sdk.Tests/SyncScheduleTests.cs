namespace ToggleForge.Sdk.Tests;

public sealed class SyncScheduleTests
{
    private const double NoJitter = 0.5;

    private static readonly SyncSchedule _everyMinute = new(TimeSpan.FromSeconds(60));

    [Theory]
    [InlineData(SyncOutcome.Updated)]
    [InlineData(SyncOutcome.NotModified)]
    [InlineData(SyncOutcome.OutOfOrder)]
    public void After_a_sync_that_reached_the_server_the_next_one_comes_after_the_interval(SyncOutcome outcome) =>
        Assert.Equal(TimeSpan.FromSeconds(60), _everyMinute.DelayAfter(new SyncAttempt(outcome), consecutiveFailures: 0, NoJitter));

    [Theory]
    [InlineData(0.0, 54)]
    [InlineData(0.999999, 66)]
    public void The_interval_varies_by_ten_percent_either_way(double sample, int expectedSeconds) =>
        Assert.Equal(expectedSeconds, _everyMinute.DelayAfter(new SyncAttempt(SyncOutcome.Updated), 0, sample).TotalSeconds, precision: 0);

    [Theory]
    [InlineData(1, 60)]
    [InlineData(2, 120)]
    [InlineData(3, 240)]
    [InlineData(4, 480)]
    [InlineData(5, 600)]
    [InlineData(60, 600)]
    public void Each_failure_in_a_row_doubles_the_wait_up_to_ten_minutes(int failures, int expectedSeconds) =>
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), _everyMinute.DelayAfter(new SyncAttempt(SyncOutcome.Failed, statusCode: 503), failures, NoJitter));

    [Fact]
    public void A_network_failure_backs_off_like_a_server_error() =>
        Assert.Equal(TimeSpan.FromSeconds(120), _everyMinute.DelayAfter(new SyncAttempt(SyncOutcome.Failed), consecutiveFailures: 2, NoJitter));

    [Fact]
    public void An_unreadable_snapshot_backs_off_too() =>
        Assert.Equal(TimeSpan.FromSeconds(240), _everyMinute.DelayAfter(new SyncAttempt(SyncOutcome.Invalid), consecutiveFailures: 3, NoJitter));

    [Fact]
    public void An_environment_never_published_is_checked_at_the_normal_pace() =>
        Assert.Equal(TimeSpan.FromSeconds(60), _everyMinute.DelayAfter(new SyncAttempt(SyncOutcome.Failed, statusCode: 404), consecutiveFailures: 0, NoJitter));

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    public void A_refused_credential_waits_the_longest_backoff_at_once(int status) =>
        Assert.Equal(SyncSchedule.MaxBackoff, _everyMinute.DelayAfter(new SyncAttempt(SyncOutcome.Failed, status), consecutiveFailures: 1, NoJitter));

    [Fact]
    public void A_retry_after_longer_than_the_backoff_wins() =>
        Assert.Equal(TimeSpan.FromSeconds(300), _everyMinute.DelayAfter(new SyncAttempt(SyncOutcome.Failed, 429, TimeSpan.FromSeconds(300)), consecutiveFailures: 1, sample: 0.0));

    [Theory]
    [InlineData(0.0, 300)]
    [InlineData(0.5, 315)]
    [InlineData(0.999999, 330)]
    public void A_retry_after_is_spread_only_upwards(double sample, int expectedSeconds) =>
        Assert.Equal(expectedSeconds, _everyMinute.DelayAfter(new SyncAttempt(SyncOutcome.Failed, 429, TimeSpan.FromSeconds(300)), 1, sample).TotalSeconds, precision: 3);

    [Fact]
    public void A_retry_after_shorter_than_the_backoff_is_not_a_shortcut() =>
        Assert.Equal(TimeSpan.FromSeconds(240), _everyMinute.DelayAfter(new SyncAttempt(SyncOutcome.Failed, 429, TimeSpan.FromSeconds(5)), consecutiveFailures: 3, NoJitter));

    [Theory]
    [InlineData(3600, 3600)]
    [InlineData(3601, 3600)]
    [InlineData(86_400, 3600)]
    public void A_retry_after_beyond_an_hour_is_capped(int askedSeconds, int expectedSeconds) =>
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), _everyMinute.DelayAfter(new SyncAttempt(SyncOutcome.Failed, 429, TimeSpan.FromSeconds(askedSeconds)), 1, sample: 0.0));

    [Fact]
    public void An_interval_longer_than_the_backoff_ceiling_is_never_shortened_by_a_failure()
    {
        var hourly = new SyncSchedule(TimeSpan.FromHours(1));

        Assert.Equal(TimeSpan.FromHours(1), hourly.DelayAfter(new SyncAttempt(SyncOutcome.Failed, 503), consecutiveFailures: 1, NoJitter));
        Assert.Equal(TimeSpan.FromHours(1), hourly.DelayAfter(new SyncAttempt(SyncOutcome.Failed, 401), consecutiveFailures: 1, NoJitter));
    }

    [Theory]
    [InlineData(SyncOutcome.Updated, null, false)]
    [InlineData(SyncOutcome.NotModified, null, false)]
    [InlineData(SyncOutcome.OutOfOrder, null, false)]
    [InlineData(SyncOutcome.Invalid, null, true)]
    [InlineData(SyncOutcome.Failed, null, true)]
    [InlineData(SyncOutcome.Failed, 404, false)]
    [InlineData(SyncOutcome.Failed, 500, true)]
    public void Only_syncs_that_could_not_reach_a_readable_answer_count_as_failures(SyncOutcome outcome, int? status, bool expected) =>
        Assert.Equal(expected, SyncSchedule.CountsAsFailure(new SyncAttempt(outcome, status)));

    [Theory]
    [InlineData(SyncOutcome.Updated, null, null, 15)]
    [InlineData(SyncOutcome.Failed, 503, null, 15)]
    [InlineData(SyncOutcome.Failed, 429, 5, 15)]
    [InlineData(SyncOutcome.Failed, 429, 120, 120)]
    [InlineData(SyncOutcome.Failed, 429, 86_400, 3600)]
    [InlineData(SyncOutcome.Failed, 401, null, 600)]
    [InlineData(SyncOutcome.Failed, 403, null, 600)]
    public void A_refresh_asked_by_the_game_waits_the_floor_or_what_the_server_asked(SyncOutcome outcome, int? status, int? retryAfterSeconds, int expectedSeconds)
    {
        var attempt = new SyncAttempt(outcome, status, retryAfterSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : null);

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), SyncSchedule.PauseBeforeRefresh(attempt));
    }
}
