using ToggleForge.Evaluation;

namespace ToggleForge.RolloutDemo;

/// <summary>Não é thread-safe: a demo avalia num único thread.</summary>
internal sealed class TallyingFlagEvaluator(IFlagEvaluator inner) : IFlagEvaluator
{
    private readonly Dictionary<EvaluationReason, int> _countsByReason = [];

    public IReadOnlyDictionary<EvaluationReason, int> CountsByReason => _countsByReason;

    public EvaluationResult<bool> EvaluateBoolean(
        ConfigurationSnapshot? snapshot, string? flagKey, EvaluationContext context, bool defaultValue) =>
        Tally(inner.EvaluateBoolean(snapshot, flagKey, context, defaultValue));

    public EvaluationResult<T> EvaluateJson<T>(
        ConfigurationSnapshot? snapshot, string? flagKey, EvaluationContext context, T defaultValue, Func<string, T> parse) =>
        Tally(inner.EvaluateJson(snapshot, flagKey, context, defaultValue, parse));

    private EvaluationResult<T> Tally<T>(EvaluationResult<T> result)
    {
        _countsByReason[result.Reason] = _countsByReason.GetValueOrDefault(result.Reason) + 1;
        return result;
    }
}
