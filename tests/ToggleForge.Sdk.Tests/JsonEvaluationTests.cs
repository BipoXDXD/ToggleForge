using Microsoft.Extensions.Time.Testing;
using ToggleForge.Evaluation;

namespace ToggleForge.Sdk.Tests;

/// <summary><see cref="ToggleForgeClient.EvaluateJson{T}"/>: o valor JSON passa pelo parser do jogo, e todo problema vira o valor padrão.</summary>
public sealed class JsonEvaluationTests
{
    private const string Fallback = "fallback";
    private const string ShopConfig = """{"discount":0.2,"featuredItems":["sword"]}""";

    private static readonly EvaluationContext _player = EvaluationContext.ForPlayer(PlayerId.Parse("player-42"));

    private readonly FakeTimeProvider _time = new();
    private readonly CountingParser _parser = new();

    [Fact]
    public async Task A_json_flag_reaches_the_game_through_its_parser()
    {
        var client = await SyncedAsync(Snapshots.WithShopConfig(version: 3));

        var result = client.EvaluateJson("shop-config", _player, Fallback, _parser.Parse);

        Assert.Equal(
            (ShopConfig, EvaluationReason.Included, SnapshotSource.Server, (long?)3, false),
            (result.Value, result.Reason, result.Source, result.SnapshotVersion, result.IsFallback));
        Assert.Equal([ShopConfig], _parser.Received);
    }

    [Fact]
    public async Task A_disabled_json_flag_gives_its_off_value_to_the_parser()
    {
        var disabled = Snapshots.JsonFlag(offValue: """{"discount":0}""").Replace("\"enabled\":true", "\"enabled\":false", StringComparison.Ordinal);
        var client = await SyncedAsync(Snapshots.Json(flags: $"[{disabled}]"));

        var result = client.EvaluateJson("shop-config", _player, Fallback, _parser.Parse);

        Assert.Equal(("""{"discount":0}""", EvaluationReason.Disabled), (result.Value, result.Reason));
    }

    [Fact]
    public async Task A_parser_that_throws_gives_the_game_its_default_with_InvalidValue()
    {
        var client = await SyncedAsync(Snapshots.WithShopConfig(version: 3));

        var result = client.EvaluateJson<string>("shop-config", _player, Fallback, _ => throw new FormatException("not a shop config"));

        Assert.Equal(
            (Fallback, EvaluationReason.InvalidValue, SnapshotSource.Server, (long?)3, true),
            (result.Value, result.Reason, result.Source, result.SnapshotVersion, result.IsFallback));
    }

    [Fact]
    public void Before_any_sync_the_game_gets_its_default_without_calling_the_parser()
    {
        var client = Client(new StubTransport());

        var result = client.EvaluateJson("shop-config", _player, Fallback, _parser.Parse);

        Assert.Equal((Fallback, EvaluationReason.SnapshotMissing, SnapshotSource.None), (result.Value, result.Reason, result.Source));
        Assert.Empty(_parser.Received);
    }

    [Fact]
    public async Task A_boolean_flag_read_as_json_is_a_type_mismatch_and_skips_the_parser()
    {
        var client = await SyncedAsync(Snapshots.WithShopConfig(version: 3));

        var result = client.EvaluateJson("new-shop", _player, Fallback, _parser.Parse);

        Assert.Equal((Fallback, EvaluationReason.TypeMismatch, (long?)3, true), (result.Value, result.Reason, result.SnapshotVersion, result.IsFallback));
        Assert.Empty(_parser.Received);
    }

    [Fact]
    public async Task A_json_flag_read_as_boolean_is_a_type_mismatch()
    {
        var client = await SyncedAsync(Snapshots.WithShopConfig(version: 3));

        var result = client.EvaluateBoolean("shop-config", _player, defaultValue: true);

        Assert.Equal((true, EvaluationReason.TypeMismatch, true), (result.Value, result.Reason, result.IsFallback));
    }

    [Fact]
    public async Task A_missing_parser_is_refused_even_after_the_snapshot_expired()
    {
        var client = await SyncedAsync(Snapshots.WithShopConfig(version: 3));
        _time.Advance(new ToggleForgeOptions().MaxCacheAge + TimeSpan.FromTicks(1));

        Assert.Throws<ArgumentNullException>(() => client.EvaluateJson<string>("shop-config", _player, Fallback, parse: null!));
    }

    private ToggleForgeClient Client(ISnapshotTransport transport) => new(transport, timeProvider: _time);

    private async Task<ToggleForgeClient> SyncedAsync(string body)
    {
        var client = Client(new StubTransport().Then(SnapshotResponse.Fetched(body, "\"v\"")));
        Assert.Equal(SyncOutcome.Updated, await client.RefreshAsync(TestContext.Current.CancellationToken));
        return client;
    }

    /// <summary>Um parser que devolve o texto recebido e anota cada chamada, para provar quando ele não roda.</summary>
    private sealed class CountingParser
    {
        public List<string> Received { get; } = [];

        public string Parse(string json)
        {
            Received.Add(json);
            return json;
        }
    }
}
