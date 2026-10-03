using ToggleForge.Evaluation;

namespace ToggleForge.Domain.Authoring;

public sealed record PublishedFlag(FlagKey Key, FlagSettings Settings, RolloutSeed Seed)
{
    public BooleanFlag ToBooleanFlag() =>
        new(Key, Settings.IsEnabled, Settings.OnValue, Settings.OffValue, new PercentageRollout(Settings.Rollout, Seed));
}
