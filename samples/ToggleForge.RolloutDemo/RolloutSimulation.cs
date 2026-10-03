using ToggleForge.Evaluation;

namespace ToggleForge.RolloutDemo;

/// <summary>Publica a flag em etapas crescentes e avalia a mesma população de jogadores em cada uma.</summary>
internal sealed class RolloutSimulation(IFlagEvaluator evaluator, FlagIdentity flag, string seed)
{
    public IReadOnlyList<StageResult> Run(IReadOnlyList<PlayerId> players, IReadOnlyList<RolloutPercentage> stages)
    {
        var results = new List<StageResult>();
        IReadOnlySet<PlayerId> previouslyIncluded = new HashSet<PlayerId>();
        for (var stage = 0; stage < stages.Count; stage++)
        {
            var snapshot = Publish(version: stage + 1, stages[stage]);
            var included = IncludedPlayers(snapshot, players);
            var lostPlayers = previouslyIncluded.Count(player => !included.Contains(player));
            results.Add(new StageResult(snapshot.Version, stages[stage], included.Count, lostPlayers));
            previouslyIncluded = included;
        }

        return results;
    }

    public ConfigurationSnapshot Publish(long version, RolloutPercentage percentage)
    {
        var newShop = new BooleanFlag(
            flag.FlagKey, isEnabled: true, onValue: true, offValue: false, new PercentageRollout(percentage, seed));
        return new ConfigurationSnapshot(flag.ProjectKey, flag.EnvironmentKey, version, [newShop]);
    }

    private HashSet<PlayerId> IncludedPlayers(ConfigurationSnapshot snapshot, IEnumerable<PlayerId> players) =>
        players
            .Where(player => evaluator
                .EvaluateBoolean(snapshot, flag.FlagKey.Value, EvaluationContext.ForPlayer(player), defaultValue: false)
                .Value)
            .ToHashSet();
}

internal sealed record StageResult(long Version, RolloutPercentage Percentage, int IncludedCount, int LostPlayers);
