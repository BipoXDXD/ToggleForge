using ToggleForge.Evaluation;

namespace ToggleForge.Domain.Authoring;

/// <summary>O que o Editor configura numa flag booleana; a chave e a seed ficam de fora porque não mudam.</summary>
public sealed record FlagSettings(bool IsEnabled, bool OnValue, bool OffValue, RolloutPercentage Rollout);
