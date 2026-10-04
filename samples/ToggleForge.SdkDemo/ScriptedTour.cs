using System.Globalization;
using ToggleForge.Evaluation;
using ToggleForge.Sdk;

namespace ToggleForge.SdkDemo;

/// <summary>
/// A história do Marco 4 contada com o SDK de verdade: rede caindo, ampliação do rollout, resposta fora de ordem,
/// reinício com o cache em disco, cache expirado e uma configuração JSON lida com a classe do jogo. Cada passo confere o que promete; se algo não acontecer, o
/// processo termina com erro, e o smoke test do CI falha.
/// </summary>
internal static class ScriptedTour
{
    private const int PlayerCount = 10_000;
    private const string Flag = "new-shop";
    private const string Seed = "seed-demo";
    private const string ShopConfigFlag = "shop-config";
    private const string ShopConfigJson = """{"discount":0.2,"featuredItems":["sword","shield"]}""";

    // Calculados pelo oráculo Python do rollout v1 (tools/rollout-vectors) com esta flag, esta semente e os
    // jogadores player-1 a player-10000; outro número quer dizer que o algoritmo, que é contrato público, mudou.
    private const int IncludedAtTenPercent = 981;
    private const int IncludedAtQuarter = 2_446;

    private static readonly CultureInfo _culture = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly TimeSpan _maxCacheAge = new ToggleForgeOptions().MaxCacheAge;
    private static readonly List<EvaluationContext> _players =
        Enumerable.Range(1, PlayerCount).Select(n => EvaluationContext.ForPlayer(PlayerId.Parse($"player-{n}"))).ToList();

    /// <returns>O código de saída do processo: 1 se uma promessa não se confirmou.</returns>
    public static async Task<int> RunAsync()
    {
        var cacheDirectory = Path.Combine(Path.GetTempPath(), "toggleforge-sdk-demo", Guid.NewGuid().ToString("N"));
        try
        {
            await TellAsync(cacheDirectory);
            return ExitCodes.Success;
        }
        catch (BrokenPromiseException broken)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine($"A demonstração não se confirmou: {broken.Message}.");
            return ExitCodes.BrokenPromise;
        }
        finally
        {
            if (Directory.Exists(cacheDirectory))
            {
                Directory.Delete(cacheDirectory, recursive: true);
            }
        }
    }

    private static async Task TellAsync(string cacheDirectory)
    {
        var clock = new DemoClock();
        var server = new SimulatedServer(Flag, Seed);
        var credential = SdkCredential.Parse("tfsdk_demo-scripted-tour-credential-not-a-secret1");
        var store = new FileSnapshotStore(cacheDirectory, credential);
        Console.WriteLine("ToggleForge: demonstração do SDK (Marco 4)");
        Console.WriteLine(string.Create(_culture, $"Flag {Flag}, {PlayerCount:N0} jogadores, cache em disco com idade máxima de {_maxCacheAge.TotalDays} dias"));

        var game = new ToggleForgeClient(server, store, timeProvider: clock);
        Step(1, "Primeira abertura, sem rede");
        await game.StartAsync();
        var first = game.EvaluateBoolean(Flag, _players[0], defaultValue: false);
        Show(first);
        Expect(first.Source == SnapshotSource.None && first.Reason == EvaluationReason.SnapshotMissing, "sem rede e sem cache, o jogo usa os próprios valores padrão");

        Step(2, "A rede volta: o estúdio publica a v1 com 10%");
        server.IsOnline = true;
        server.Publish(rolloutBasisPoints: 1_000);
        Expect(await SyncAsync(game, clock) == SyncOutcome.Updated && Version(game) == 1, "a v1 chega na sincronização seguinte");
        var atTenPercent = Included(game);
        Show(game, atTenPercent);
        Expect(atTenPercent.Count == IncludedAtTenPercent, $"a v1 inclui {IncludedAtTenPercent} jogadores");

        Step(3, "O estúdio amplia para 25% (v2)");
        server.Publish(rolloutBasisPoints: 2_500);
        Expect(await SyncAsync(game, clock) == SyncOutcome.Updated && Version(game) == 2, "a v2 chega na sincronização seguinte");
        var atQuarter = Included(game);
        var lost = atTenPercent.Except(atQuarter).Count();
        Show(game, atQuarter);
        Console.WriteLine(string.Create(_culture, $"   perdidos desde a v1: {lost}"));
        Expect(atQuarter.Count == IncludedAtQuarter, $"a v2 inclui {IncludedAtQuarter} jogadores");
        Expect(lost == 0, "ampliar o rollout não tira ninguém que já estava incluído");

        Step(4, "Uma resposta atrasada com a v1 chega depois da v2");
        server.DeliverLateOnce(version: 1);
        var late = await SyncAsync(game, clock);
        Console.WriteLine($"   {late}: o jogo continua na v{Version(game)}");
        Expect(late == SyncOutcome.OutOfOrder && Version(game) == 2, "a ordem vem da versão da publicação, nunca da chegada");

        Step(5, "O jogador fecha o jogo e abre de novo, sem rede");
        await game.DisposeAsync();
        server.IsOnline = false;
        game = new ToggleForgeClient(server, store, timeProvider: clock);
        await game.StartAsync();
        var restarted = game.EvaluateBoolean(Flag, _players[0], defaultValue: false);
        Show(restarted);
        Expect(restarted.Value && restarted.Source == SnapshotSource.Cache && restarted.SnapshotVersion == 2, "o snapshot do disco vale enquanto a rede não volta");

        var offline = _maxCacheAge + TimeSpan.FromDays(1);
        Step(6, $"{offline.TotalDays} dias sem rede");
        clock.Advance(offline);
        var expired = game.EvaluateBoolean(Flag, _players[0], defaultValue: false);
        Show(expired);
        Expect(expired.Reason == EvaluationReason.SnapshotExpired && expired.IsFallback, $"depois de {_maxCacheAge.TotalDays} dias sem confirmação, o jogo volta aos valores padrão");

        Step(7, "A rede volta: o servidor confirma a v2 com 304");
        server.IsOnline = true;
        var confirmed = await SyncAsync(game, clock);
        var back = game.EvaluateBoolean(Flag, _players[0], defaultValue: false);
        Console.WriteLine($"   {server.LastStatusCode} {confirmed}");
        Show(back);
        Expect(
            server.LastStatusCode == 304 && confirmed == SyncOutcome.NotModified && back.Value && back.Source == SnapshotSource.Server && back.SnapshotVersion == 2,
            "a ETag guardada no disco rende um 304, que confirma o snapshot sem baixá-lo de novo");

        Step(8, $"O estúdio publica a configuração JSON {ShopConfigFlag} (v3)");
        server.Publish(rolloutBasisPoints: 2_500, jsonFlag: (ShopConfigFlag, ShopConfigJson));
        Expect(await SyncAsync(game, clock) == SyncOutcome.Updated && Version(game) == 3, "a v3 chega na sincronização seguinte");
        var shop = game.EvaluateJson(ShopConfigFlag, _players[0], ShopConfig.Closed, LoggingFailures(ShopConfig.Parse));
        Show(ShopConfigFlag, shop, shop.Value.ToString());
        Expect(
            shop.Reason == EvaluationReason.Included && shop.Value.Discount == 0.2m && shop.Value.FeaturedItems.SequenceEqual(["sword", "shield"]),
            "o jogo lê a configuração com a própria classe, sem recompilar");
        var refused = game.EvaluateJson(ShopConfigFlag, _players[0], defaultValue: -1, LoggingFailures(json => int.Parse(json, CultureInfo.InvariantCulture)));
        Show(ShopConfigFlag, refused, refused.Value.ToString(CultureInfo.InvariantCulture));
        Expect(refused.Value == -1 && refused.Reason == EvaluationReason.InvalidValue, "um parser que recusa o valor não derruba o jogo: ele recebe o próprio valor padrão");
        await game.DisposeAsync();

        Console.WriteLine();
        Console.WriteLine("Tudo conferido.");
    }

    /// <summary>Um minuto depois, como o laço faria; o SDK não deixa o jogo sincronizar mais rápido que 15 s.</summary>
    private static async Task<SyncOutcome> SyncAsync(ToggleForgeClient game, DemoClock clock)
    {
        clock.Advance(TimeSpan.FromMinutes(1));
        return await game.RefreshAsync();
    }

    private static HashSet<int> Included(ToggleForgeClient game) =>
        Enumerable.Range(0, PlayerCount).Where(index => game.EvaluateBoolean(Flag, _players[index], defaultValue: false).Value).ToHashSet();

    /// <summary>
    /// O SDK troca a exceção do parser pelo valor padrão e a descarta; o jogo que quer o diagnóstico a registra
    /// dentro do próprio parser e a deixa seguir, como o XML doc de <c>EvaluateJson</c> orienta.
    /// </summary>
    private static Func<string, T> LoggingFailures<T>(Func<string, T> parse) => json =>
    {
        try
        {
            return parse(json);
        }
        catch (Exception failure)
        {
            Console.WriteLine($"   o parser recusou o valor: {failure.GetType().Name}");
            throw;
        }
    };

    private static long? Version(ToggleForgeClient game) => game.EvaluateBoolean(Flag, _players[0], defaultValue: false).SnapshotVersion;

    private static void Step(int number, string title)
    {
        Console.WriteLine();
        Console.WriteLine($"{number}. {title}");
    }

    private static void Show(FlagResult<bool> result) => Console.WriteLine($"   {FlagResultText.Describe(Flag, result)}");

    private static void Show<T>(string flagKey, FlagResult<T> result, string value) =>
        Console.WriteLine($"   {FlagResultText.Describe(flagKey, result, value)}");

    private static void Show(ToggleForgeClient game, HashSet<int> included) =>
        Console.WriteLine(string.Create(_culture, $"   v{Version(game)}: incluídos {included.Count:N0} de {PlayerCount:N0} ({(decimal)included.Count / PlayerCount:P1})"));

    private static void Expect(bool held, string promise)
    {
        if (!held)
        {
            throw new BrokenPromiseException(promise);
        }
    }

    private sealed class BrokenPromiseException(string promise) : Exception(promise);
}
