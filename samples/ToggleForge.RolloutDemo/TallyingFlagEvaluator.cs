using ToggleForge.Evaluation;

namespace ToggleForge.RolloutDemo;

/// <summary>Não é thread-safe: a demo avalia num único thread.</summary>
internal sealed class TallyingFlagEvaluator(IFlagEvaluator inner) : IFlagEvaluator
{
    private readonly Dictionary<EvaluationReason, int> _countsByReason = [];

    public IReadOnlyDictionary<EvaluationReason, int> CountsByReason => _countsByReason;

    public EvaluationResult<bool> EvaluateBoolean(
        ConfigurationSnapshot? snapshot, string? flagKey, EvaluationContext context, bool defaultValue)
    {
        var result = inner.EvaluateBoolean(snapshot, flagKey, context, defaultValue);
        _countsByReason[result.Reason] = _countsByReason.GetValueOrDefault(result.Reason) + 1;
        return result;
    }
}
