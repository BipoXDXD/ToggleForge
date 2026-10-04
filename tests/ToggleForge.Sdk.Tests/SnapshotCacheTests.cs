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
    public async Task The_first_confirmation_of_a_stored_snapshot_is_saved_at_once()
    {
        var store = StoredAtVersion(3, confirmedAgo: TimeSpan.FromHours(5));
        await using var client = Client(new StubTransport().Then(SnapshotResponse.NotModified), store);

        await client.StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal(_time.GetUtcNow(), Assert.Single(store.Saves).ConfirmedAt);
    }

    [Fact]
    public async Task The_version_in_use_sent_again_without_etag_still_confirms_the_stored_snapshot()
    {
        var store = StoredAtVersion(3, confirmedAgo: TimeSpan.FromDays(6));
        await using var client = Client(new StubTransport().Then(SnapshotResponse.Fetched(Snapshots.Json(version: 3), eTag: null)), store);

        await client.StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal((SnapshotSource.Server, (long?)3), Origin(client));
        Assert.Equal(("\"v3\"", _time.GetUtcNow()), (Assert.Single(store.Saves).ETag, store.Saves[0].ConfirmedAt));
    }

    [Theory]
    [InlineData(5, true)]
    [InlineData(6, false)]
    public async Task A_confirmation_dated_in_the_future_counts_as_expired_beyond_five_minutes(int minutesAhead, bool valid)
    {
        await using var client = Client(new StubTransport().Then(SnapshotResponse.Failed(statusCode: null)), StoredAtVersion(3, confirmedAgo: TimeSpan.FromMinutes(-minutesAhead)));

        await client.StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal(valid, client.EvaluateBoolean("new-shop", _player, defaultValue: false).Reason != EvaluationReason.SnapshotExpired);
    }

    [Fact]
    public async Task Turning_the_clock_back_cannot_stretch_the_max_age()
    {
        var clock = new AdjustableClock();
        var store = new MemoryStore();
        var transport = new StubTransport()
            .Then(SnapshotResponse.Fetched(Snapshots.Json(version: 1), "\"v1\""))
            .Then(SnapshotResponse.NotModified)
            .Then(SnapshotResponse.NotModified);
        var waits = new ManualWaits();
        await using var client = new ToggleForgeClient(transport, store, options: null, clock, waits.WaitAsync, () => 0.5);
        var realNow = clock.GetUtcNow();
        clock.SetWallClock(realNow.AddDays(30));
        await client.StartAsync(TestContext.Current.CancellationToken);

        clock.SetWallClock(realNow);
        Assert.Equal(EvaluationReason.SnapshotExpired, client.EvaluateBoolean("new-shop", _player, defaultValue: false).Reason);
        for (var sync = 0; sync < 2; sync++)
        {
            await waits.NextAsync();
            clock.Advance(TimeSpan.FromHours(1));
            waits.Elapse();
        }

        await waits.NextAsync();
        Assert.Equal(EvaluationReason.Included, client.EvaluateBoolean("new-shop", _player, defaultValue: false).Reason);
        Assert.Equal(clock.GetUtcNow(), store.Saves[^1].ConfirmedAt);
    }

    [Fact]
    public async Task A_short_max_age_saves_confirmations_four_times_within_it()
    {
        var store = new MemoryStore();
        var transport = new StubTransport()
            .Then(SnapshotResponse.Fetched(Snapshots.Json(version: 1), "\"v1\""))
            .Then(SnapshotResponse.NotModified)
            .Then(SnapshotResponse.NotModified);
        await using var client = Client(transport, store, new ToggleForgeOptions { MaxCacheAge = TimeSpan.FromHours(1) });
        await client.StartAsync(TestContext.Current.CancellationToken);

        await SyncAfterAsync(TimeSpan.FromMinutes(14));
        Assert.Single(store.Saves);

        await SyncAfterAsync(TimeSpan.FromMinutes(1));
        Assert.Equal(2, store.Saves.Count);
    }

    [Fact]
    public async Task Overlapping_syncs_always_leave_the_newest_version_on_disk()
    {
        var store = new GatedStore();
        var transport = new ScriptedTransport();
        await using var client = Client(transport, store);
        transport.Next(SnapshotResponse.Fetched(Snapshots.Json(version: 3), "\"v3\""));
        var first = client.RefreshAsync(TestContext.Current.CancellationToken);
        await store.FirstSaveStarted;
        transport.Next(SnapshotResponse.Fetched(Snapshots.Disabled(version: 4), "\"v4\""));
        _time.Advance(ToggleForgeOptions.MinSyncInterval);
        var second = client.RefreshAsync(TestContext.Current.CancellationToken);

        store.ReleaseFirstSave();
        await Task.WhenAll(first, second);

        Assert.Equal("\"v4\"", store.Saves[^1].ETag);
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
        Assert.Equal(
            valid ? (EvaluationReason.Included, SnapshotSource.Cache) : (EvaluationReason.SnapshotExpired, SnapshotSource.None),
            (result.Reason, result.Source));
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
    public async Task A_missing_context_is_refused_even_after_the_snapshot_expired()
    {
        await using var client = Client(new StubTransport().Then(SnapshotResponse.Failed(statusCode: null)), StoredAtVersion(3, confirmedAgo: TimeSpan.FromDays(8)));

        await client.StartAsync(TestContext.Current.CancellationToken);

        Assert.Throws<ArgumentNullException>(() => client.EvaluateBoolean("new-shop", context: null!, defaultValue: false));
    }

    [Fact]
    public async Task An_expired_snapshot_gives_the_game_its_json_default_without_calling_the_parser()
    {
        var store = new MemoryStore { Stored = new StoredSnapshot(Snapshots.WithShopConfig(version: 3), "\"v3\"", _time.GetUtcNow() - TimeSpan.FromDays(8)) };
        await using var client = Client(new StubTransport().Then(SnapshotResponse.Failed(statusCode: null)), store);
        var calls = 0;

        await client.StartAsync(TestContext.Current.CancellationToken);

        var result = client.EvaluateJson("shop-config", _player, "fallback", json =>
        {
            calls++;
            return json;
        });
        Assert.Equal(
            ("fallback", EvaluationReason.SnapshotExpired, SnapshotSource.None, (long?)null, true, 0),
            (result.Value, result.Reason, result.Source, result.SnapshotVersion, result.IsFallback, calls));
    }

    [Fact]
    public async Task A_json_flag_in_the_stored_snapshot_is_in_use_after_a_restart_without_network()
    {
        var store = new MemoryStore { Stored = new StoredSnapshot(Snapshots.WithShopConfig(version: 3), "\"v3\"", _time.GetUtcNow() - TimeSpan.FromHours(5)) };
        await using var client = Client(new StubTransport().Then(SnapshotResponse.Failed(statusCode: null)), store);

        await client.StartAsync(TestContext.Current.CancellationToken);

        var result = client.EvaluateJson("shop-config", _player, "fallback", json => json);
        Assert.Equal(
            ("""{"discount":0.2,"featuredItems":["sword"]}""", SnapshotSource.Cache, (long?)3),
            (result.Value, result.Source, result.SnapshotVersion));
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
