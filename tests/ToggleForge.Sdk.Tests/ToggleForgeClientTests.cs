using Microsoft.Extensions.Time.Testing;
using ToggleForge.Evaluation;

namespace ToggleForge.Sdk.Tests;

public sealed class ToggleForgeClientTests
{
    private static readonly EvaluationContext _player = EvaluationContext.ForPlayer(PlayerId.Parse("player-42"));

    // Relógio parado: cada teste avança o piso entre sincronizações quando precisa de mais de uma.
    private readonly FakeTimeProvider _time = new();

    [Fact]
    public void Before_any_sync_the_game_gets_its_default_value()
    {
        var client = Client(new StubTransport());

        var result = client.EvaluateBoolean("new-shop", _player, defaultValue: false);

        Assert.Equal((false, EvaluationReason.SnapshotMissing, SnapshotSource.None, (long?)null), (result.Value, result.Reason, result.Source, result.SnapshotVersion));
        Assert.True(result.IsFallback);
    }

    [Fact]
    public async Task A_synced_snapshot_decides_the_flags_from_then_on()
    {
        var client = Client(new StubTransport().Then(SnapshotResponse.Fetched(Snapshots.Json(version: 3), "\"v3\"")));

        var outcome = await client.RefreshAsync(TestContext.Current.CancellationToken);

        var result = client.EvaluateBoolean("new-shop", _player, defaultValue: false);
        Assert.Equal(SyncOutcome.Updated, outcome);
        Assert.Equal((true, EvaluationReason.Included, SnapshotSource.Server, (long?)3), (result.Value, result.Reason, result.Source, result.SnapshotVersion));
        Assert.False(result.IsFallback);
    }

    [Fact]
    public async Task The_next_sync_asks_whether_the_snapshot_changed()
    {
        var transport = new StubTransport()
            .Then(SnapshotResponse.Fetched(Snapshots.Json(version: 3), "\"v3\""))
            .Then(SnapshotResponse.NotModified);
        var client = Client(transport);
        await client.RefreshAsync(TestContext.Current.CancellationToken);
        _time.Advance(ToggleForgeOptions.MinSyncInterval);

        var outcome = await client.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.Equal(SyncOutcome.NotModified, outcome);
        Assert.Equal([null, "\"v3\""], transport.SentETags);
        Assert.Equal(3, client.EvaluateBoolean("new-shop", _player, defaultValue: false).SnapshotVersion);
    }

    [Fact]
    public async Task An_older_version_arriving_late_is_discarded()
    {
        var client = await SyncedAtVersionThree(SnapshotResponse.Fetched(Snapshots.Disabled(version: 2), "\"v2\""));

        var outcome = await client.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.Equal(SyncOutcome.OutOfOrder, outcome);
        AssertStillAtVersionThree(client);
    }

    [Fact]
    public async Task The_same_version_again_is_not_swapped_in()
    {
        var client = await SyncedAtVersionThree(SnapshotResponse.Fetched(Snapshots.Disabled(version: 3), "\"other\""));

        Assert.Equal(SyncOutcome.OutOfOrder, await client.RefreshAsync(TestContext.Current.CancellationToken));
        AssertStillAtVersionThree(client);
    }

    [Fact]
    public async Task A_newer_version_replaces_the_one_in_use()
    {
        var client = await SyncedAtVersionThree(SnapshotResponse.Fetched(Snapshots.Disabled(version: 4), "\"v4\""));

        Assert.Equal(SyncOutcome.Updated, await client.RefreshAsync(TestContext.Current.CancellationToken));
        var result = client.EvaluateBoolean("new-shop", _player, defaultValue: true);
        Assert.Equal((false, EvaluationReason.Disabled, (long?)4), (result.Value, result.Reason, result.SnapshotVersion));
    }

    [Fact]
    public async Task A_snapshot_of_another_environment_replaces_the_one_in_use_whatever_its_version()
    {
        var client = await SyncedAtVersionThree(SnapshotResponse.Fetched(Snapshots.Disabled(version: 1).Replace("\"dev\"", "\"prod\"", StringComparison.Ordinal), "\"p1\""));

        Assert.Equal(SyncOutcome.Updated, await client.RefreshAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, client.EvaluateBoolean("new-shop", _player, defaultValue: false).SnapshotVersion);
    }

    [Fact]
    public async Task A_snapshot_of_another_project_replaces_the_one_in_use_whatever_its_version()
    {
        var client = await SyncedAtVersionThree(SnapshotResponse.Fetched(Snapshots.Disabled(version: 1).Replace("\"space-race\"", "\"moon-race\"", StringComparison.Ordinal), "\"m1\""));

        Assert.Equal(SyncOutcome.Updated, await client.RefreshAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, client.EvaluateBoolean("new-shop", _player, defaultValue: false).SnapshotVersion);
    }

    [Fact]
    public async Task A_rollout_result_carries_the_bucket_of_the_player()
    {
        var halfRollout = Snapshots.Json().Replace("\"rolloutBasisPoints\":10000", "\"rolloutBasisPoints\":5000", StringComparison.Ordinal);
        var client = Client(new StubTransport().Then(SnapshotResponse.Fetched(halfRollout, "\"v1\"")));
        await client.RefreshAsync(TestContext.Current.CancellationToken);

        // O bucket de player-42 em space-race/dev/new-shop/s1, calculado pelo oráculo em tools/rollout-vectors.
        Assert.Equal(3693, client.EvaluateBoolean("new-shop", _player, defaultValue: false).Bucket);
    }

    [Fact]
    public async Task An_invalid_snapshot_keeps_the_one_in_use()
    {
        var client = await SyncedAtVersionThree(SnapshotResponse.Fetched(Snapshots.Json(version: 4).Replace("rollout.v1", "rollout.v2", StringComparison.Ordinal), "\"v4\""));

        Assert.Equal(SyncOutcome.Invalid, await client.RefreshAsync(TestContext.Current.CancellationToken));
        AssertStillAtVersionThree(client);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(404)]
    [InlineData(429)]
    [InlineData(500)]
    public async Task A_failed_sync_keeps_the_one_in_use(int? statusCode)
    {
        var client = await SyncedAtVersionThree(SnapshotResponse.Failed(statusCode));

        Assert.Equal(SyncOutcome.Failed, await client.RefreshAsync(TestContext.Current.CancellationToken));
        AssertStillAtVersionThree(client);
    }

    [Fact]
    public async Task A_failed_first_sync_leaves_the_game_on_its_defaults()
    {
        var client = Client(new StubTransport().Then(SnapshotResponse.Failed(statusCode: null)));

        Assert.Equal(SyncOutcome.Failed, await client.RefreshAsync(TestContext.Current.CancellationToken));
        Assert.Equal(SnapshotSource.None, client.EvaluateBoolean("new-shop", _player, defaultValue: true).Source);
    }

    [Fact]
    public async Task A_transport_that_throws_is_a_failed_sync_not_an_exception_in_the_game()
    {
        var client = Client(new ThrowingTransport(new InvalidOperationException("adapter bug")));

        Assert.Equal(SyncOutcome.Failed, await client.RefreshAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Cancelling_the_sync_stops_it()
    {
        using var stop = new CancellationTokenSource();
        await stop.CancelAsync();
        var client = Client(new ThrowingTransport(new OperationCanceledException(stop.Token)));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.RefreshAsync(stop.Token));
    }

    [Fact]
    public async Task Concurrent_syncs_never_move_the_version_backwards()
    {
        var versions = Enumerable.Range(1, 200).OrderBy(version => (version * 7919) % 200).ToList();
        var transport = new GatedTransport(versions);
        var client = Client(transport);
        var syncs = versions.Select(_ => Task.Run(() => client.RefreshAsync(TestContext.Current.CancellationToken), TestContext.Current.CancellationToken)).ToList();
        var seen = new List<long>();
        await transport.AllArrivedAsync();
        transport.ReleaseAll();
        while (syncs.Any(sync => !sync.IsCompleted))
        {
            seen.Add(client.EvaluateBoolean("new-shop", _player, defaultValue: false).SnapshotVersion ?? 0);
        }

        await Task.WhenAll(syncs);

        Assert.Equal(200, client.EvaluateBoolean("new-shop", _player, defaultValue: false).SnapshotVersion);
        Assert.Equal(seen.Order(), seen);
    }

    [Fact]
    public async Task A_refresh_right_after_a_sync_is_skipped_without_calling_the_server()
    {
        var transport = new StubTransport().Then(SnapshotResponse.NotModified).Then(SnapshotResponse.NotModified);
        var client = Client(transport);
        await client.RefreshAsync(TestContext.Current.CancellationToken);
        _time.Advance(ToggleForgeOptions.MinSyncInterval - TimeSpan.FromMilliseconds(1));

        Assert.Equal(SyncOutcome.Skipped, await client.RefreshAsync(TestContext.Current.CancellationToken));
        Assert.Single(transport.SentETags);

        _time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(SyncOutcome.NotModified, await client.RefreshAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_refresh_waits_as_long_as_the_server_asked()
    {
        var transport = new StubTransport().Then(SnapshotResponse.Failed(429, TimeSpan.FromMinutes(2))).Then(SnapshotResponse.NotModified);
        var client = Client(transport);
        await client.RefreshAsync(TestContext.Current.CancellationToken);
        _time.Advance(TimeSpan.FromSeconds(119));

        Assert.Equal(SyncOutcome.Skipped, await client.RefreshAsync(TestContext.Current.CancellationToken));

        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(SyncOutcome.NotModified, await client.RefreshAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_first_refresh_goes_out_at_once() =>
        Assert.Equal(SyncOutcome.NotModified, await Client(new StubTransport().Then(SnapshotResponse.NotModified)).RefreshAsync(TestContext.Current.CancellationToken));

    private ToggleForgeClient Client(ISnapshotTransport transport) => new(transport, options: null, _time);

    private async Task<ToggleForgeClient> SyncedAtVersionThree(SnapshotResponse next)
    {
        var client = Client(new StubTransport().Then(SnapshotResponse.Fetched(Snapshots.Json(version: 3), "\"v3\"")).Then(next));
        await client.RefreshAsync(TestContext.Current.CancellationToken);
        _time.Advance(ToggleForgeOptions.MinSyncInterval);
        return client;
    }

    private static void AssertStillAtVersionThree(ToggleForgeClient client)
    {
        var result = client.EvaluateBoolean("new-shop", _player, defaultValue: false);
        Assert.Equal((true, EvaluationReason.Included, (long?)3), (result.Value, result.Reason, result.SnapshotVersion));
    }

    private sealed class ThrowingTransport(Exception failure) : ISnapshotTransport
    {
        public Task<SnapshotResponse> GetAsync(string? eTag, CancellationToken cancellationToken) => Task.FromException<SnapshotResponse>(failure);
    }

    /// <summary>Cada sincronização recebe uma versão, e todas as respostas saem juntas, para disputarem a troca.</summary>
    private sealed class GatedTransport(IEnumerable<int> versions) : ISnapshotTransport
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _allArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly System.Collections.Concurrent.ConcurrentQueue<int> _versions = new(versions);

        public void ReleaseAll() => _gate.SetResult();

        /// <summary>Todas as sincronizações chegaram ao transporte antes de qualquer uma terminar.</summary>
        public Task AllArrivedAsync() => _allArrived.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        public async Task<SnapshotResponse> GetAsync(string? eTag, CancellationToken cancellationToken)
        {
            _versions.TryDequeue(out var version);
            if (_versions.IsEmpty)
            {
                _allArrived.TrySetResult();
            }

            await _gate.Task;
            return SnapshotResponse.Fetched(Snapshots.Json(version), $"\"v{version}\"");
        }
    }
}
