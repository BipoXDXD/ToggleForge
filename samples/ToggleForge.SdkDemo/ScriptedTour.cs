using System.Globalization;
using System.Net;
using ToggleForge.Evaluation;
using ToggleForge.Sdk;

namespace ToggleForge.SdkDemo;

/// <summary>
/// A história do Marco 4 contada com o SDK de verdade: rede caindo, ampliação do rollout, resposta fora de ordem,
/// reinício com o cache em disco, cache expirado e uma configuração JSON lida com a classe do jogo. Cada passo
/// confere o que promete; se algo não acontecer, o processo termina com erro, e o smoke test do CI falha.
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
        var credential = SdkCredential.Parse("tfsdk_demo-scripted-tour-credential-not-a-secret1");
        var store = new FileSnapshotStore(cacheDirectory, credential);
        var tour = new Tour(new SimulatedServer(Flag, Seed), store, new DemoClock());
        IntroduceTheTour();

        await FirstOpeningWithoutNetworkAsync(tour);
        var atTenPercent = await NetworkReturnsWithTheFirstPublicationAsync(tour);
        await WidenTheRolloutAsync(tour, atTenPercent);
        await IgnoreALateResponseAsync(tour);
        await ReopenWithoutNetworkAsync(tour);
        StayOfflinePastTheCacheAge(tour);
        await ConfirmTheCacheWithNotModifiedAsync(tour);
        await ReadTheShopConfigAsync(tour);
        await tour.Game.DisposeAsync();

        Console.WriteLine();
        Console.WriteLine("Tudo conferido.");
    }

    private static void IntroduceTheTour()
    {
        Console.WriteLine("ToggleForge: demonstração do SDK (Marco 4)");
        Console.WriteLine(string.Create(
            _culture,
            $"Flag {Flag}, {PlayerCount:N0} jogadores, cache em disco com idade máxima de {_maxCacheAge.TotalDays} dias"));
    }

    private static async Task FirstOpeningWithoutNetworkAsync(Tour tour)
    {
        Step(1, "Primeira abertura, sem rede");
        await tour.Game.StartAsync();
        var first = tour.EvaluateForFirstPlayer();
        Show(first);
        Expect(
            first.Source == SnapshotSource.None && first.Reason == EvaluationReason.SnapshotMissing,
            "sem rede e sem cache, o jogo usa os próprios valores padrão");
    }

    /// <returns>Os índices dos jogadores incluídos na v1.</returns>
    private static async Task<HashSet<int>> NetworkReturnsWithTheFirstPublicationAsync(Tour tour)
    {
        Step(2, "A rede volta: o estúdio publica a v1 com 10%");
        tour.Server.IsOnline = true;
        tour.Server.Publish(rolloutBasisPoints: 1_000);
        await ExpectTheNextSyncToBringAsync(tour, version: 1);
        var atTenPercent = Included(tour.Game);
        Show(tour, atTenPercent);
        Expect(atTenPercent.Count == IncludedAtTenPercent, $"a v1 inclui {IncludedAtTenPercent} jogadores");
        return atTenPercent;
    }

    private static async Task WidenTheRolloutAsync(Tour tour, HashSet<int> atTenPercent)
    {
        Step(3, "O estúdio amplia para 25% (v2)");
        tour.Server.Publish(rolloutBasisPoints: 2_500);
        await ExpectTheNextSyncToBringAsync(tour, version: 2);
        var atQuarter = Included(tour.Game);
        var lost = atTenPercent.Except(atQuarter).Count();
        Show(tour, atQuarter);
        Console.WriteLine(string.Create(_culture, $"   perdidos desde a v1: {lost}"));
        Expect(atQuarter.Count == IncludedAtQuarter, $"a v2 inclui {IncludedAtQuarter} jogadores");
        Expect(lost == 0, "ampliar o rollout não tira ninguém que já estava incluído");
    }

    private static async Task IgnoreALateResponseAsync(Tour tour)
    {
        Step(4, "Uma resposta atrasada com a v1 chega depois da v2");
        tour.Server.DeliverLateOnce(version: 1);
        var late = await tour.SyncAsync();
        Console.WriteLine($"   {late}: o jogo continua na v{tour.Version}");
        Expect(
            late == SyncOutcome.OutOfOrder && tour.Version == 2,
            "a ordem vem da versão da publicação, nunca da chegada");
    }

    private static async Task ReopenWithoutNetworkAsync(Tour tour)
    {
        Step(5, "O jogador fecha o jogo e abre de novo, sem rede");
        tour.Server.IsOnline = false;
        await tour.ReopenGameAsync();
        var restarted = tour.EvaluateForFirstPlayer();
        Show(restarted);
        Expect(
            restarted.Value && CameFrom(restarted, SnapshotSource.Cache, version: 2),
            "o snapshot do disco vale enquanto a rede não volta");
    }

    private static void StayOfflinePastTheCacheAge(Tour tour)
    {
        var offline = _maxCacheAge + TimeSpan.FromDays(1);
        Step(6, $"{offline.TotalDays} dias sem rede");
        tour.Clock.Advance(offline);
        var expired = tour.EvaluateForFirstPlayer();
        Show(expired);
        Expect(
            expired.Reason == EvaluationReason.SnapshotExpired && expired.IsFallback,
            $"depois de {_maxCacheAge.TotalDays} dias sem confirmação, o jogo volta aos valores padrão");
    }

    private static async Task ConfirmTheCacheWithNotModifiedAsync(Tour tour)
    {
        Step(7, "A rede volta: o servidor confirma a v2 com 304");
        tour.Server.IsOnline = true;
        var confirmed = await tour.SyncAsync();
        var back = tour.EvaluateForFirstPlayer();
        Console.WriteLine($"   {tour.Server.LastStatusCode} {confirmed}");
        Show(back);
        var revalidated = confirmed == SyncOutcome.NotModified
            && tour.Server.LastStatusCode == (int)HttpStatusCode.NotModified;
        Expect(
            revalidated && back.Value && CameFrom(back, SnapshotSource.Server, version: 2),
            "a ETag guardada no disco rende um 304, que confirma o snapshot sem baixá-lo de novo");
    }

    private static async Task ReadTheShopConfigAsync(Tour tour)
    {
        Step(8, $"O estúdio publica a configuração JSON {ShopConfigFlag} (v3)");
        tour.Server.Publish(rolloutBasisPoints: 2_500, jsonFlag: (ShopConfigFlag, ShopConfigJson));
        await ExpectTheNextSyncToBringAsync(tour, version: 3);
        var shop = tour.Game.EvaluateJson(ShopConfigFlag, _players[0], ShopConfig.Closed, LoggingFailures(ShopConfig.Parse));
        Show(ShopConfigFlag, shop, shop.Value.ToString());
        var asPublished = shop.Value.Discount == 0.2m && shop.Value.FeaturedItems.SequenceEqual(["sword", "shield"]);
        Expect(
            shop.Reason == EvaluationReason.Included && CameFrom(shop, SnapshotSource.Server, version: 3) && asPublished,
            "o jogo lê a configuração com a própria classe, sem recompilar");

        var refusingParser = LoggingFailures(json => int.Parse(json, CultureInfo.InvariantCulture));
        var refused = tour.Game.EvaluateJson(ShopConfigFlag, _players[0], defaultValue: -1, refusingParser);
        Show(ShopConfigFlag, refused, refused.Value.ToString(CultureInfo.InvariantCulture));
        Expect(
            refused.Value == -1 && refused.Reason == EvaluationReason.InvalidValue,
            "um parser que recusa o valor não derruba o jogo: ele recebe o próprio valor padrão");
    }

    private static async Task ExpectTheNextSyncToBringAsync(Tour tour, int version) =>
        Expect(
            await tour.SyncAsync() == SyncOutcome.Updated && tour.Version == version,
            $"a v{version} chega na sincronização seguinte");

    private static bool CameFrom<T>(FlagResult<T> result, SnapshotSource source, long version) =>
        result.Source == source && result.SnapshotVersion == version;

    private static void Step(int number, string title)
    {
        Console.WriteLine();
        Console.WriteLine($"{number}. {title}");
    }

    private static void Show(FlagResult<bool> result) => Console.WriteLine($"   {FlagResultText.Describe(Flag, result)}");

    private static void Show(Tour tour, HashSet<int> included) =>
        Console.WriteLine(string.Create(
            _culture,
            $"   v{tour.Version}: incluídos {included.Count:N0} de {PlayerCount:N0} ({(decimal)included.Count / PlayerCount:P1})"));

    private static void Show<T>(string flagKey, FlagResult<T> result, string value) =>
        Console.WriteLine($"   {FlagResultText.Describe(flagKey, result, value)}");

    private static void Expect(bool held, string promise)
    {
        if (!held)
        {
            throw new BrokenPromiseException(promise);
        }
    }

    private static HashSet<int> Included(ToggleForgeClient game) =>
        Enumerable.Range(0, PlayerCount)
            .Where(index => game.EvaluateBoolean(Flag, _players[index], defaultValue: false).Value)
            .ToHashSet();

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

    private sealed class BrokenPromiseException(string promise) : Exception(promise);

    /// <summary>
    /// O que os passos do roteiro compartilham: o servidor simulado, o relógio e o jogo, que o passo 5 fecha e abre
    /// de novo sobre o mesmo cache em disco.
    /// </summary>
    private sealed class Tour
    {
        private readonly FileSnapshotStore _store;

        public Tour(SimulatedServer server, FileSnapshotStore store, DemoClock clock)
        {
            _store = store;
            Server = server;
            Clock = clock;
            Game = new ToggleForgeClient(server, store, timeProvider: clock);
        }

        public SimulatedServer Server { get; }

        public DemoClock Clock { get; }

        public ToggleForgeClient Game { get; private set; }

        public long? Version => EvaluateForFirstPlayer().SnapshotVersion;

        public FlagResult<bool> EvaluateForFirstPlayer() => Game.EvaluateBoolean(Flag, _players[0], defaultValue: false);

        /// <summary>Um minuto depois, como o laço faria; o SDK não deixa o jogo sincronizar mais rápido que 15 s.</summary>
        public async Task<SyncOutcome> SyncAsync()
        {
            Clock.Advance(TimeSpan.FromMinutes(1));
            return await Game.RefreshAsync();
        }

        public async Task ReopenGameAsync()
        {
            await Game.DisposeAsync();
            Game = new ToggleForgeClient(Server, _store, timeProvider: Clock);
            await Game.StartAsync();
        }
    }
}
