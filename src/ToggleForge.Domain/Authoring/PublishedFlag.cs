using ToggleForge.Evaluation;

namespace ToggleForge.Domain.Authoring;

public sealed record PublishedFlag(FlagKey Key, FlagSettings Settings, RolloutSeed Seed)
{
    public Flag ToFlag()
    {
        var rollout = new PercentageRollout(Settings.Rollout, Seed);
        return Settings.Values switch
        {
            BooleanValues values => new BooleanFlag(Key, Settings.IsEnabled, values.On, values.Off, rollout),
            JsonValues values => new JsonFlag(Key, Settings.IsEnabled, values.OnValue.Text, values.OffValue.Text, rollout),
            var unknown => throw new InvalidOperationException($"Unknown flag values '{unknown.GetType().Name}'."),
        };
    }
}
