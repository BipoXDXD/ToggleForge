using Microsoft.Extensions.Time.Testing;
using ToggleForge.Evaluation;

namespace ToggleForge.Sdk.Tests;

public sealed class SnapshotCacheTests
{
    private static readonly EvaluationContext _player = EvaluationContext.ForPlayer(PlayerId.Parse("player-42"));
    private static readonly TimeSpan _week = TimeSpan.FromDays(7);

    private readonly FakeTimeProvider _time = new();
    private readonly ManualWaits _waits = new();

    [Fact]
    public async Task A_stored_snapshot_is_in_use_before_the_server_answers()
    {
        var store = StoredAtVersion(3, confirmedAgo: TimeSpan.FromHours(5));
        var transport = new HangingTransport();
        await using var client = Client(transport, store);

        var start = client.StartAsync(TestContext.Current.CancellationToken);
        await transport.Arrived;

        var result = client.EvaluateBoolean("new-shop", _player, defaultValue: false);
        Assert.Equal((true, SnapshotSource.Cache, (long?)3), (result.Value, result.Source, result.SnapshotVersion));
        transport.Answer(SnapshotResponse.NotModified);
        await start;
    }

    [Fact]
    public async Task The_server_confirming_the_stored_snapshot_makes_it_current()
    {
        var transport = new StubTransport().Then(SnapshotResponse.NotModified);
        await using var client = Client(transport, StoredAtVersion(3, confirmedAgo: TimeSpan.FromHours(5)));

        await client.StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["\"v3\""], transport.SentETags);
        Assert.Equal((SnapshotSource.Server, (long?)3), Origin(client));
    }

    [Fact]
    public async Task Without_the_server_the_game_keeps_playing_on_the_stored_snapshot()
    {
        await using var client = Client(new StubTransport().Then(SnapshotResponse.Failed(statusCode: null)), StoredAtVersion(3, confirmedAgo: TimeSpan.FromDays(2)));

        await client.StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal((SnapshotSource.Cache, (long?)3), Origin(client));
    }

    [Fact]
    public async Task A_newer_version_from_the_server_replaces_the_stored_one_and_is_saved()
    {
        var store = StoredAtVersion(3, confirmedAgo: TimeSpan.FromHours(5));
        await using var client = Client(new StubTransport().Then(SnapshotResponse.Fetched(Snapshots.Disabled(version: 4), "\"v4\"")), store);

        await client.StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal((SnapshotSource.Server, (long?)4), Origin(client));
        var saved = Assert.Single(store.Saves);
        Assert.Equal((Snapshots.Disabled(version: 4), "\"v4\"", _time.GetUtcNow()), (saved.Body, saved.ETag, saved.ConfirmedAt));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public async Task A_snapshot_is_valid_for_seven_days_after_the_last_confirmation(long ticksOverAWeek, bool valid)
    {
        await using var client = Client(new StubTransport().Then(SnapshotResponse.Failed(statusCode: null)), StoredAtVersion(3, confirmedAgo: _week + TimeSpan.FromTicks(ticksOverAWeek)));

        await client.StartAsync(TestContext.Current.CancellationToken);

        var result = client.EvaluateBoolean("new-shop", _player, defaultValue: false);
        Assert.Equal(valid, result.Reason != EvaluationReason.SnapshotExpired);
    }

    [Fact]
    public async Task An_expired_snapshot_gives_the_game_its_defaults()
    {
        await using var client = Client(new StubTransport().Then(SnapshotResponse.Failed(statusCode: null)), StoredAtVersion(3, confirmedAgo: TimeSpan.FromDays(8)));

        await client.StartAsync(TestContext.Current.CancellationToken);

        var result = client.EvaluateBoolean("new-shop", _player, defaultValue: false);
        Assert.Equal((false, EvaluationReason.SnapshotExpired, SnapshotSource.None, (long?)null, true), (result.Value, result.Reason, result.Source, result.SnapshotVersion, result.IsFallback));
    }

    [Fact]
    public async Task A_not_modified_answer_brings_an_expired_snapshot_back()
    {
        var transport = new StubTransport().Then(SnapshotResponse.NotModified);
        await using var client = Client(transport, StoredAtVersion(3, confirmedAgo: TimeSpan.FromDays(30)));

        await client.StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["\"v3\""], transport.SentETags);
        Assert.Equal((SnapshotSource.Server, (long?)3), Origin(client));
    }

    [Fact]
    public async Task A_snapshot_in_memory_expires_when_the_server_stays_silent()
    {
        await using var client = Client(new StubTransport().Then(SnapshotResponse.Fetched(Snapshots.Json(version: 1), "\"v1\"")), store: null);
        await client.StartAsync(TestContext.Current.CancellationToken);

        _time.Advance(_week + TimeSpan.FromTicks(1));

        Assert.Equal(EvaluationReason.SnapshotExpired, client.EvaluateBoolean("new-shop", _player, defaultValue: false).Reason);
    }

    [Fact]
    public async Task A_configured_max_age_replaces_the_seven_days()
    {
        var options = new ToggleForgeOptions { MaxCacheAge = TimeSpan.FromHours(1) };
        await using var client = Client(new StubTransport().Then(SnapshotResponse.Failed(statusCode: null)), StoredAtVersion(3, confirmedAgo: TimeSpan.FromHours(2)), options);

        await client.StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal(EvaluationReason.SnapshotExpired, client.EvaluateBoolean("new-shop", _player, defaultValue: false).Reason);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_max_age_that_is_not_positive_is_refused(int seconds) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ToggleForgeClient(new StubTransport(), options: new ToggleForgeOptions { MaxCacheAge = TimeSpan.FromSeconds(seconds) }));

    [Fact]
    public async Task An_unreadable_stored_snapshot_is_ignored()
    {
        var store = new MemoryStore { Stored = new StoredSnapshot("{\"not\":\"a snapshot\"}", "\"v3\"", _time.GetUtcNow()) };
        var transport = new StubTransport().Then(SnapshotResponse.Failed(statusCode: null));
        await using var client = Client(transport, store);

        await client.StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal([null], transport.SentETags);
        Assert.Equal(SnapshotSource.None, client.EvaluateBoolean("new-shop", _player, defaultValue: false).Source);
    }

    [Fact]
    public async Task A_broken_disk_never_reaches_the_game()
    {
        var store = new MemoryStore { Failure = new IOException("disk full") };
        await using var client = Client(new StubTransport().Then(SnapshotResponse.Fetched(Snapshots.Json(version: 1), "\"v1\"")), store);

        await client.StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal((SnapshotSource.Server, (long?)1), Origin(client));
    }

    [Fact]
    public async Task Confirmations_are_saved_at_most_once_an_hour()
    {
        var store = new MemoryStore();
        var transport = new StubTransport()
            .Then(SnapshotResponse.Fetched(Snapshots.Json(version: 1), "\"v1\""))
            .Then(SnapshotResponse.NotModified)
            .Then(SnapshotResponse.NotModified);
        await using var client = Client(transport, store);
        await client.StartAsync(TestContext.Current.CancellationToken);

        await SyncAfterAsync(TimeSpan.FromMinutes(59));
        Assert.Single(store.Saves);

        await SyncAfterAsync(TimeSpan.FromMinutes(1));
        Assert.Equal(2, store.Saves.Count);
        Assert.Equal(_time.GetUtcNow(), store.Saves[^1].ConfirmedAt);
    }

    [Fact]
    public async Task A_stored_snapshot_never_replaces_one_from_the_server()
    {
        var store = new MemoryStore();
        var transport = new StubTransport().Then(SnapshotResponse.Fetched(Snapshots.Json(version: 2), "\"v2\"")).Then(SnapshotResponse.NotModified);
        await using var client = Client(transport, store);
        await client.RefreshAsync(TestContext.Current.CancellationToken);

        // Outro arquivo no lugar do que o refresh gravou, como um cache antigo que outro processo escreveu.
        store.Stored = new StoredSnapshot(Snapshots.Json(version: 9), "\"v9\"", _time.GetUtcNow());
        await client.StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal((SnapshotSource.Server, (long?)2), Origin(client));
    }

    private static (SnapshotSource Source, long? Version) Origin(ToggleForgeClient client)
    {
        var result = client.EvaluateBoolean("new-shop", _player, defaultValue: false);
        return (result.Source, result.SnapshotVersion);
    }

    private MemoryStore StoredAtVersion(long version, TimeSpan confirmedAgo) =>
        new() { Stored = new StoredSnapshot(Snapshots.Json(version), $"\"v{version}\"", _time.GetUtcNow() - confirmedAgo) };

    private ToggleForgeClient Client(ISnapshotTransport transport, ISnapshotStore? store, ToggleForgeOptions? options = null) =>
        new(transport, store, options, _time, _waits.WaitAsync, () => 0.5);

    /// <summary>
    /// Deixa o tempo passar e o laço fazer a próxima sincronização; volta quando ela terminou, que é quando o laço
    /// pede a espera seguinte.
    /// </summary>
    private async Task SyncAfterAsync(TimeSpan elapsed)
    {
        if (!_waits.IsWaiting)
        {
            await _waits.NextAsync();
        }

        _time.Advance(elapsed);
        _waits.Elapse();
        await _waits.NextAsync();
    }
}
