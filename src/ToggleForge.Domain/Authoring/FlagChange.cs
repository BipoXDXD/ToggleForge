using ToggleForge.Evaluation;

namespace ToggleForge.Domain.Authoring;

/// <summary>O que o Editor muda numa flag. Nulo mantém o valor atual; a chave, a seed e o tipo nunca mudam.</summary>
public sealed record FlagChange(
    bool? IsEnabled = null, JsonFlagValue? OnValue = null, JsonFlagValue? OffValue = null, RolloutPercentage? Rollout = null)
{
    internal Result<FlagSettings> ApplyTo(FlagSettings current)
    {
        if (!current.Values.With(OnValue, OffValue).TryGetValue(out var values, out var failure))
        {
            return failure;
        }

        return new FlagSettings(IsEnabled ?? current.IsEnabled, values, Rollout ?? current.Rollout);
    }
}
