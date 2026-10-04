using System.Globalization;
using System.Net;
using Microsoft.Extensions.Time.Testing;
using ToggleForge.Api.Tests.Support;
using ToggleForge.Evaluation;
using ToggleForge.Sdk;

namespace ToggleForge.Api.Tests;

/// <summary>O SDK do Marco 4 contra a API real: o que o jogo baixa e avalia é o que o console publicou e simulou.</summary>
public sealed class SdkClientTests(ApiFactory api) : IDisposable
{
    private readonly List<HttpClient> _games = [];

    // O relógio do SDK: avançar o piso entre sincronizações deixa o teste pedir uma atrás da outra.
    private readonly FakeTimeProvider _time = new();

    [Fact]
    public async Task The_sdk_evaluates_each_player_like_the_simulation_of_the_live_version()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        await owner.PublishedProjectAsync("space-race");
        var client = await ClientForAsync(owner);

        Assert.Equal(SyncOutcome.Updated, await RefreshLaterAsync(client));

        foreach (var playerId in Enumerable.Range(1, 20).Select(number => $"player-{number.ToString(CultureInfo.InvariantCulture)}"))
        {
            var simulated = (await owner.SimulatePublicationAsync("space-race", playerId, version: 1)).Json["results"]![0]!;
            var evaluated = client.EvaluateBoolean("new-shop", EvaluationContext.ForPlayer(PlayerId.Parse(playerId)), defaultValue: false);
            Assert.Equal(
                (simulated["value"]!.GetValue<bool>(), simulated["bucket"]!.GetValue<int>()),
                (evaluated.Value, evaluated.Bucket ?? -1));
            Assert.Equal((SnapshotSource.Server, (long?)1), (evaluated.Source, evaluated.SnapshotVersion));
        }
    }

    [Fact]
    public async Task The_sdk_follows_each_publication_and_rollback_and_hears_when_nothing_changed()
    {
        var player = EvaluationContext.ForPlayer(PlayerId.Parse("player-42"));
        using var owner = await ConsoleClient.SignUpAsync(api);
        await owner.PublishedProjectAsync("space-race");
        var client = await ClientForAsync(owner);
        await RefreshLaterAsync(client);
        var atTenPercent = client.EvaluateBoolean("new-shop", player, defaultValue: false);

        Assert.Equal(SyncOutcome.NotModified, await RefreshLaterAsync(client));

        var read = await owner.GetFlagAsync("space-race", "new-shop");
        var widened = await owner.UpdateFlagAsync("space-race", "new-shop", read.Header("ETag"), """{"rolloutBasisPoints":10000}""");
        Assert.Equal(HttpStatusCode.Created, (await owner.PublishAsync("space-race", ConsoleClient.Revision(widened), ConsoleClient.NewIdempotencyKey())).Status);
        Assert.Equal(SyncOutcome.Updated, await RefreshLaterAsync(client));
        var everyone = client.EvaluateBoolean("new-shop", player, defaultValue: false);
        Assert.Equal((true, EvaluationReason.Included, (long?)2), (everyone.Value, everyone.Reason, everyone.SnapshotVersion));

        Assert.Equal(HttpStatusCode.Created, (await owner.RollBackAsync("space-race", targetVersion: 1, expectedCurrentVersion: 2, ConsoleClient.NewIdempotencyKey())).Status);
        Assert.Equal(SyncOutcome.Updated, await RefreshLaterAsync(client));
        var rolledBack = client.EvaluateBoolean("new-shop", player, defaultValue: false);
        Assert.Equal((atTenPercent.Value, atTenPercent.Bucket, (long?)3), (rolledBack.Value, rolledBack.Bucket, rolledBack.SnapshotVersion));
    }

    [Fact]
    public async Task Starting_the_sdk_puts_the_live_version_in_use_before_the_game_goes_on()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        await owner.PublishedProjectAsync("space-race");
        await using var client = await ClientForAsync(owner);

        await client.StartAsync(TestContext.Current.CancellationToken);

        var result = client.EvaluateBoolean("new-shop", EvaluationContext.ForPlayer(PlayerId.Parse("player-42")), defaultValue: false);
        Assert.Equal((SnapshotSource.Server, (long?)1), (result.Source, result.SnapshotVersion));
    }

    [Fact]
    public async Task Before_the_first_publication_the_game_stays_on_its_defaults()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        Assert.Equal(HttpStatusCode.Created, (await owner.CreateProjectAsync("space-race")).Status);
        var client = await ClientForAsync(owner);

        Assert.Equal(SyncOutcome.Failed, await RefreshLaterAsync(client));
        var result = client.EvaluateBoolean("new-shop", EvaluationContext.Anonymous, defaultValue: true);
        Assert.Equal((true, SnapshotSource.None), (result.Value, result.Source));
    }

    [Fact]
    public async Task A_revoked_credential_keeps_the_last_snapshot_in_use()
    {
        using var owner = await ConsoleClient.SignUpAsync(api);
        await owner.PublishedProjectAsync("space-race");
        var created = await owner.CreateCredentialAsync("space-race");
        var client = ClientWith(created.Json["secret"]!.GetValue<string>());
        await RefreshLaterAsync(client);

        Assert.Equal(HttpStatusCode.OK, (await owner.RevokeCredentialAsync("space-race", created.Json["id"]!.GetValue<string>())).Status);

        Assert.Equal(SyncOutcome.Failed, await RefreshLaterAsync(client));
        Assert.Equal((long?)1, client.EvaluateBoolean("new-shop", EvaluationContext.Anonymous, defaultValue: false).SnapshotVersion);
    }

    private async Task<ToggleForgeClient> ClientForAsync(ConsoleClient owner) =>
        ClientWith((await owner.CreateCredentialAsync("space-race")).Json["secret"]!.GetValue<string>());

    public void Dispose()
    {
        _games.ForEach(game => game.Dispose());
    }

    private ToggleForgeClient ClientWith(string secret)
    {
        var game = api.CreateDefaultClient(ApiFactory.BaseAddress, new SnapshotContractCheck());
        _games.Add(game);
        return new ToggleForgeClient(new HttpSnapshotTransport(game, SdkCredential.Parse(secret)), options: null, _time);
    }

    /// <summary>Sincroniza depois do piso do intervalo, como o laço faria.</summary>
    private async Task<SyncOutcome> RefreshLaterAsync(ToggleForgeClient client)
    {
        _time.Advance(ToggleForgeOptions.MinSyncInterval);
        return await client.RefreshAsync(TestContext.Current.CancellationToken);
    }
}
