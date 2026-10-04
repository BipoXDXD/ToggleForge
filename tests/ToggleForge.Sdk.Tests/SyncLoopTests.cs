using Microsoft.Extensions.Time.Testing;
using ToggleForge.Evaluation;

namespace ToggleForge.Sdk.Tests;

public sealed class SyncLoopTests
{
    private const double NoJitter = 0.5;

    private static readonly EvaluationContext _player = EvaluationContext.ForPlayer(PlayerId.Parse("player-42"));

    [Fact]
    public async Task Starting_syncs_once_and_then_waits_the_interval()
    {
        var transport = new StubTransport().Then(SnapshotResponse.Fetched(Snapshots.Json(version: 1), "\"v1\""));
        var waits = new ManualWaits();
        await using var client = Client(transport, waits);

        await client.StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, client.EvaluateBoolean("new-shop", _player, defaultValue: false).SnapshotVersion);
        Assert.Equal(TimeSpan.FromSeconds(60), await waits.NextAsync());
        Assert.Single(transport.SentETags);
    }

    [Fact]
    public async Task Each_wait_ends_in_another_sync_that_picks_up_the_new_version()
    {
        var transport = new StubTransport()
            .Then(SnapshotResponse.Fetched(Snapshots.Json(version: 1), "\"v1\""))
            .Then(SnapshotResponse.NotModified)
            .Then(SnapshotResponse.Fetched(Snapshots.Disabled(version: 2), "\"v2\""));
        var waits = new ManualWaits();
        await using var client = Client(transport, waits);
        await client.StartAsync(TestContext.Current.CancellationToken);

        await waits.NextAsync();
        waits.Elapse();
        await waits.NextAsync();
        waits.Elapse();
        await waits.NextAsync();

        Assert.Equal([null, "\"v1\"", "\"v1\""], transport.SentETags);
        Assert.Equal(2, client.EvaluateBoolean("new-shop", _player, defaultValue: true).SnapshotVersion);
    }

    [Fact]
    public async Task Failures_in_a_row_stretch_the_wait_and_a_success_resets_it()
    {
        var transport = new StubTransport()
            .Then(SnapshotResponse.Failed(statusCode: null))
            .Then(SnapshotResponse.Failed(statusCode: 503))
            .Then(SnapshotResponse.Failed(statusCode: 503))
            .Then(SnapshotResponse.Fetched(Snapshots.Json(version: 1), "\"v1\""));
        var waits = new ManualWaits();
        await using var client = Client(transport, waits);
        await client.StartAsync(TestContext.Current.CancellationToken);

        var delays = new List<TimeSpan>();
        for (var sync = 0; sync < 4; sync++)
        {
            delays.Add(await waits.NextAsync());
            waits.Elapse();
        }

        Assert.Equal([TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(120), TimeSpan.FromSeconds(240), TimeSpan.FromSeconds(60)], delays);
    }

    [Fact]
    public async Task Too_many_requests_waits_as_long_as_the_server_asked()
    {
        var transport = new StubTransport().Then(SnapshotResponse.Failed(429, TimeSpan.FromMinutes(5)));
        var waits = new ManualWaits();
        await using var client = new ToggleForgeClient(transport, store: null, options: null, new FakeTimeProvider(), waits.WaitAsync, () => 0.0);

        await client.StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal(TimeSpan.FromMinutes(5), await waits.NextAsync());
    }

    [Fact]
    public async Task The_variation_spreads_the_players_around_the_interval()
    {
        var transport = new StubTransport().Then(SnapshotResponse.NotModified);
        var waits = new ManualWaits();
        await using var client = new ToggleForgeClient(transport, store: null, options: null, new FakeTimeProvider(), waits.WaitAsync, () => 0.0);

        await client.StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal(TimeSpan.FromSeconds(54), await waits.NextAsync());
    }

    [Fact]
    public async Task A_configured_interval_replaces_the_default()
    {
        var transport = new StubTransport().Then(SnapshotResponse.NotModified);
        var waits = new ManualWaits();
        await using var client = Client(transport, waits, new ToggleForgeOptions { SyncInterval = TimeSpan.FromSeconds(15) });

        await client.StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal(TimeSpan.FromSeconds(15), await waits.NextAsync());
    }

    [Theory]
    [InlineData(15, true)]
    [InlineData(14.999, false)]
    [InlineData(3600, true)]
    [InlineData(3600.001, false)]
    public void The_interval_goes_from_fifteen_seconds_to_one_hour(double seconds, bool accepted)
    {
        var options = new ToggleForgeOptions { SyncInterval = TimeSpan.FromSeconds(seconds) };

        var creation = Record.Exception(() => new ToggleForgeClient(new StubTransport(), options: options));

        Assert.Equal(accepted, creation is null);
        Assert.True(accepted || creation is ArgumentOutOfRangeException);
    }

    [Fact]
    public async Task Disposing_stops_the_loop_while_it_waits()
    {
        var transport = new StubTransport().Then(SnapshotResponse.Fetched(Snapshots.Json(version: 1), "\"v1\""));
        var waits = new ManualWaits();
        var client = Client(transport, waits);
        await client.StartAsync(TestContext.Current.CancellationToken);
        await waits.NextAsync();

        // Com prazo: um laço que não para viraria uma suíte travada em vez de um teste falhando.
        await client.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.False(waits.IsWaiting);
        Assert.Single(transport.SentETags);
        Assert.Equal(1, client.EvaluateBoolean("new-shop", _player, defaultValue: false).SnapshotVersion);
    }

    [Fact]
    public async Task Disposing_without_waiting_also_stops_the_loop()
    {
        var waits = new ManualWaits();
        var client = Client(new StubTransport().Then(SnapshotResponse.NotModified), waits);
        await client.StartAsync(TestContext.Current.CancellationToken);
        await waits.NextAsync();

        client.Dispose();

        Assert.False(waits.IsWaiting);
        await client.DisposeAsync();
    }

    [Fact]
    public async Task Disposing_during_the_first_sync_cancels_it()
    {
        var transport = new HangingTransport();
        var waits = new ManualWaits();
        var client = Client(transport, waits);
        var start = client.StartAsync(TestContext.Current.CancellationToken);
        await transport.Arrived;

        await client.DisposeAsync();

        Assert.True(transport.WasCancelled);
        await start;
        Assert.Equal(0, waits.Requested);
    }

    [Fact]
    public async Task A_client_starts_only_once()
    {
        var waits = new ManualWaits();
        await using var client = Client(new StubTransport().Then(SnapshotResponse.NotModified), waits);
        await client.StartAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.StartAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_disposed_client_does_not_start()
    {
        var transport = new StubTransport();
        var client = Client(transport, new ManualWaits());
        await client.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.StartAsync(TestContext.Current.CancellationToken));
        Assert.Empty(transport.SentETags);
    }

    [Fact]
    public async Task Giving_up_on_the_first_sync_leaves_the_sdk_syncing_in_the_background()
    {
        using var giveUp = new CancellationTokenSource();
        var transport = new HangingTransport();
        var waits = new ManualWaits();
        await using var client = Client(transport, waits);
        var start = client.StartAsync(giveUp.Token);
        await transport.Arrived;

        await giveUp.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => start);
        Assert.False(transport.WasCancelled);
        transport.Answer(SnapshotResponse.Fetched(Snapshots.Json(version: 1), "\"v1\""));
        Assert.Equal(TimeSpan.FromSeconds(60), await waits.NextAsync());
        Assert.Equal(1, client.EvaluateBoolean("new-shop", _player, defaultValue: false).SnapshotVersion);
    }

    [Fact]
    public async Task A_failure_after_a_success_starts_the_backoff_over()
    {
        var transport = new StubTransport()
            .Then(SnapshotResponse.Failed(statusCode: 503))
            .Then(SnapshotResponse.Fetched(Snapshots.Json(version: 1), "\"v1\""))
            .Then(SnapshotResponse.Failed(statusCode: 503))
            .Then(SnapshotResponse.Failed(statusCode: 503));
        var waits = new ManualWaits();
        await using var client = Client(transport, waits);
        await client.StartAsync(TestContext.Current.CancellationToken);

        var delays = new List<TimeSpan>();
        for (var sync = 0; sync < 4; sync++)
        {
            delays.Add(await waits.NextAsync());
            waits.Elapse();
        }

        Assert.Equal([TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(120)], delays);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    public async Task A_refused_credential_makes_the_loop_wait_the_longest_backoff(int status)
    {
        var waits = new ManualWaits();
        await using var client = Client(new StubTransport().Then(SnapshotResponse.Failed(status)), waits);

        await client.StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal(TimeSpan.FromMinutes(10), await waits.NextAsync());
    }

    [Fact]
    public async Task An_environment_never_published_keeps_the_loop_at_the_normal_pace()
    {
        var waits = new ManualWaits();
        await using var client = Client(new StubTransport().Then(SnapshotResponse.Failed(404)).Then(SnapshotResponse.Failed(404)), waits);
        await client.StartAsync(TestContext.Current.CancellationToken);

        var first = await waits.NextAsync();
        waits.Elapse();
        var second = await waits.NextAsync();

        Assert.Equal((TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60)), (first, second));
    }

    [Fact]
    public async Task Disposing_again_in_either_way_is_harmless()
    {
        var client = Client(new StubTransport().Then(SnapshotResponse.NotModified), new ManualWaits());
        await client.StartAsync(TestContext.Current.CancellationToken);
        await client.DisposeAsync();

        var again = await Record.ExceptionAsync(async () =>
        {
            client.Dispose();
            await client.DisposeAsync();
        });

        Assert.Null(again);
    }

    [Fact]
    public async Task Disposing_waits_for_a_sync_in_flight_to_end()
    {
        var transport = new SecondSyncInFlight(honoursCancellation: false);
        var waits = new ManualWaits();
        var client = Client(transport, waits);
        await client.StartAsync(TestContext.Current.CancellationToken);
        await waits.NextAsync();
        waits.Elapse();
        await transport.Started.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        var disposing = client.DisposeAsync().AsTask();
        var endedBeforeTheSync = disposing.IsCompleted;
        transport.Finish();
        await disposing;

        Assert.False(endedBeforeTheSync);
    }

    [Fact]
    public async Task Disposing_cancels_a_sync_in_flight()
    {
        var transport = new SecondSyncInFlight(honoursCancellation: true);
        var waits = new ManualWaits();
        var client = Client(transport, waits);
        await client.StartAsync(TestContext.Current.CancellationToken);
        await waits.NextAsync();
        waits.Elapse();
        await transport.Started.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        await client.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.False(waits.IsWaiting);
    }

    private static ToggleForgeClient Client(ISnapshotTransport transport, ManualWaits waits, ToggleForgeOptions? options = null) =>
        new(transport, store: null, options, new FakeTimeProvider(), waits.WaitAsync, () => NoJitter);

    /// <summary>
    /// A primeira sincronização responde na hora; a segunda fica em andamento até o teste mandar terminar, ou até o
    /// cancelamento, se o adaptador o atende.
    /// </summary>
    private sealed class SecondSyncInFlight(bool honoursCancellation) : ISnapshotTransport
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _finish = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;

        public Task Started => _started.Task;

        public void Finish() => _finish.SetResult();

        public async Task<SnapshotResponse> GetAsync(string? eTag, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                return SnapshotResponse.NotModified;
            }

            _started.SetResult();
            await _finish.Task.WaitAsync(honoursCancellation ? cancellationToken : CancellationToken.None);
            return SnapshotResponse.NotModified;
        }
    }
}
