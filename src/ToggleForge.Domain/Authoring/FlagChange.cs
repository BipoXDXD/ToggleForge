using ToggleForge.Evaluation;

namespace ToggleForge.Domain.Authoring;

/// <summary>O que o Editor muda numa flag. Nulo mantém o valor atual; a chave e a seed nunca mudam.</summary>
public sealed record FlagChange(
    bool? IsEnabled = null, bool? OnValue = null, bool? OffValue = null, RolloutPercentage? Rollout = null)
{
    internal FlagSettings ApplyTo(FlagSettings current) =>
        new(IsEnabled ?? current.IsEnabled, OnValue ?? current.OnValue, OffValue ?? current.OffValue, Rollout ?? current.Rollout);
}
