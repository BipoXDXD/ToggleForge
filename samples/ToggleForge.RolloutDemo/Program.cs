using System.Globalization;
using ToggleForge.Evaluation;
using ToggleForge.RolloutDemo;

const int PlayerCount = 100_000;
var culture = CultureInfo.GetCultureInfo("pt-BR");

var tallying = new TallyingFlagEvaluator(new FlagEvaluator());
var newShop = new FlagIdentity(
    ProjectKey.Parse("studio-game"), EnvironmentKey.Parse("prod"), FlagKey.Parse("new-shop"));
var seed = RolloutSeed.Parse("seed-1");
var simulation = new RolloutSimulation(tallying, newShop, seed);

var players = Enumerable.Range(1, PlayerCount).Select(n => PlayerId.Parse($"player-{n}")).ToList();
RolloutPercentage[] stages =
[
    RolloutPercentage.FromPercent(10m),
    RolloutPercentage.FromPercent(20m),
    RolloutPercentage.FromPercent(50m),
];

PrintStages(simulation.Run(players, stages));
var sampleStage = stages[1];
PrintSamplePlayers(simulation.Publish(version: 2, sampleStage), sampleStage, ["player-1", "player-3", "player-8"]);
PrintCountsByReason();

void PrintStages(IEnumerable<StageResult> results)
{
    Console.WriteLine("ToggleForge: demonstração do rollout (Marco 1)");
    Console.WriteLine(string.Create(
        culture, $"Flag {newShop.FlagKey} em {newShop.ProjectKey}/{newShop.EnvironmentKey}, {PlayerCount:N0} jogadores simulados"));
    Console.WriteLine();
    Console.WriteLine("Versão  Rollout  Incluídos  Taxa real  Perdidos desde a versão anterior");
    foreach (var stage in results)
    {
        var actualRate = (decimal)stage.IncludedCount / PlayerCount;
        var lostPlayers = stage.LostPlayers?.ToString(culture) ?? "—";
        Console.WriteLine(string.Create(
            culture,
            $"v{stage.Version,-6} {stage.Percentage,-8} {stage.IncludedCount,9:N0}  {actualRate,9:P2}  {lostPlayers}"));
    }
}

void PrintSamplePlayers(ConfigurationSnapshot snapshot, RolloutPercentage percentage, IEnumerable<string> samples)
{
    Console.WriteLine();
    Console.WriteLine($"Jogadores de exemplo na v{snapshot.Version} ({percentage}):");
    foreach (var sample in samples)
    {
        var playerId = PlayerId.Parse(sample);
        var bucket = RolloutBucket.Compute(newShop, seed, playerId);
        var result = Evaluate(snapshot, EvaluationContext.ForPlayer(playerId));
        Console.WriteLine(string.Create(
            culture, $"  {sample,-9} bucket {bucket,5}  ->  {result.Value,-5} ({result.Reason})"));
    }

    var anonymous = Evaluate(snapshot, EvaluationContext.Anonymous);
    Console.WriteLine($"  {"anônimo",-22}  ->  {anonymous.Value,-5} ({anonymous.Reason}, valor padrão)");
}

EvaluationResult<bool> Evaluate(ConfigurationSnapshot snapshot, EvaluationContext context) =>
    tallying.EvaluateBoolean(snapshot, newShop.FlagKey.Value, context, defaultValue: false);

void PrintCountsByReason()
{
    Console.WriteLine();
    Console.WriteLine("Avaliações por motivo (contadas pelo decorator):");
    foreach (var (reason, count) in tallying.CountsByReason.OrderBy(pair => pair.Key))
    {
        Console.WriteLine(string.Create(culture, $"  {reason,-16} {count,9:N0}"));
    }
}
