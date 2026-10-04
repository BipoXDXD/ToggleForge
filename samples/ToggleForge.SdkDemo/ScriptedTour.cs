using System.Globalization;
using ToggleForge.Evaluation;
using ToggleForge.Sdk;

namespace ToggleForge.SdkDemo;

/// <summary>
/// A história do Marco 4 contada com o SDK de verdade: rede caindo, ampliação do rollout, resposta fora de ordem,
/// reinício com o cache em disco e cache expirado. Cada passo confere o que promete; se algo não acontecer, o
/// processo termina com erro, e o smoke test do CI falha.
/// </summary>
internal static class ScriptedTour
{
    private const int PlayerCount = 10_000;
    private const string Flag = "new-shop";

    private static readonly CultureInfo _culture = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly List<EvaluationContext> _players =
        Enumerable.Range(1, PlayerCount).Select(n => EvaluationContext.ForPlayer(PlayerId.Parse($"player-{n}"))).ToList();

    public static async Task RunAsync()
    {
        var cacheDirectory = Path.Combine(Path.GetTempPath(), "toggleforge-sdk-demo", Guid.NewGuid().ToString("N"));
        try
        {
            await TellAsync(cacheDirectory);
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
        var server = new SimulatedServer();
        var credential = SdkCredential.Parse("tfsdk_demo" + new string('x', 39));
        var store = new FileSnapshotStore(cacheDirectory, credential);
        Console.WriteLine("ToggleForge: demonstração do SDK (Marco 4)");
        Console.WriteLine(string.Create(_culture, $"Flag {Flag}, {PlayerCount:N0} jogadores, cache em disco com idade máxima de 7 dias"));

        var game = new ToggleForgeClient(server, store, timeProvider: clock);
        Step(1, "Primeira abertura, sem rede");
        await game.StartAsync();
        var first = game.EvaluateBoolean(Flag, _players[0], defaultValue: false);
        Show(first);
        Expect(first.Source == SnapshotSource.None && first.Reason == EvaluationReason.SnapshotMissing, "sem rede e sem cache, o jogo usa os próprios valores padrão");

        Step(2, "A rede volta: o estúdio publica a v1 com 10%");
        server.IsOnline = true;
        server.Publish(rolloutBasisPoints: 1_000);
        Expect(await SyncAsync(game, clock) == SyncOutcome.Updated, "a v1 chega na sincronização seguinte");
        var atTenPercent = Included(game);
        Show(game, atTenPercent);

        Step(3, "O estúdio amplia para 25% (v2)");
        server.Publish(rolloutBasisPoints: 2_500);
        await SyncAsync(game, clock);
        var atQuarter = Included(game);
        var lost = atTenPercent.Except(atQuarter).Count();
        Show(game, atQuarter);
        Console.WriteLine(string.Create(_culture, $"   perdidos desde a v1: {lost}"));
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
        Expect(restarted.Source == SnapshotSource.Cache && restarted.SnapshotVersion == 2, "o snapshot do disco vale enquanto a rede não volta");

        Step(6, "Oito dias sem rede");
        clock.Advance(TimeSpan.FromDays(8));
        var expired = game.EvaluateBoolean(Flag, _players[0], defaultValue: false);
        Show(expired);
        Expect(expired.Reason == EvaluationReason.SnapshotExpired && expired.IsFallback, "depois de 7 dias sem confirmação, o jogo volta aos valores padrão");

        Step(7, "A rede volta: o servidor confirma a v2 com 304");
        server.IsOnline = true;
        var confirmed = await SyncAsync(game, clock);
        var back = game.EvaluateBoolean(Flag, _players[0], defaultValue: false);
        Console.WriteLine($"   {confirmed}");
        Show(back);
        Expect(confirmed == SyncOutcome.NotModified && back.Source == SnapshotSource.Server && back.SnapshotVersion == 2, "um 304 confirma o snapshot guardado sem baixá-lo de novo");
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

    private static long? Version(ToggleForgeClient game) => game.EvaluateBoolean(Flag, _players[0], defaultValue: false).SnapshotVersion;

    private static void Step(int number, string title)
    {
        Console.WriteLine();
        Console.WriteLine($"{number}. {title}");
    }

    private static void Show(FlagResult<bool> result) =>
        Console.WriteLine($"   {Flag} = {(result.Value ? "true" : "false")} ({result.Reason}, {result.Source}{(result.SnapshotVersion is { } version ? $", v{version}" : string.Empty)})");

    private static void Show(ToggleForgeClient game, HashSet<int> included) =>
        Console.WriteLine(string.Create(_culture, $"   v{Version(game)}: incluídos {included.Count:N0} de {PlayerCount:N0} ({(decimal)included.Count / PlayerCount:P1})"));

    private static void Expect(bool held, string promise)
    {
        if (!held)
        {
            throw new InvalidOperationException($"A demonstração não se confirmou: {promise}.");
        }
    }
}
