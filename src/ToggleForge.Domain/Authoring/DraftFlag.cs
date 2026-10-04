using ToggleForge.Evaluation;

namespace ToggleForge.Domain.Authoring;

public sealed class DraftFlag
{
    private DraftFlag(
        FlagKey key,
        bool isEnabled,
        FlagType type,
        JsonFlagValue onValue,
        JsonFlagValue offValue,
        RolloutPercentage rollout,
        RolloutSeed seed,
        long changedAtRevision)
    {
        Key = key;
        IsEnabled = isEnabled;
        Type = type;
        OnValue = onValue;
        OffValue = offValue;
        Rollout = rollout;
        Seed = seed;
        ChangedAtRevision = changedAtRevision;
    }

    public FlagKey Key { get; }

    public bool IsEnabled { get; private set; }

    public FlagType Type { get; }

    public JsonFlagValue OnValue { get; private set; }

    public JsonFlagValue OffValue { get; private set; }

    public RolloutPercentage Rollout { get; private set; }

    public RolloutSeed Seed { get; }

    /// <summary>Revisão do rascunho em que a flag mudou pela última vez; é a ETag da flag.</summary>
    public long ChangedAtRevision { get; private set; }

    public FlagSettings Settings => new(IsEnabled, FlagValues.Restore(Type, OnValue, OffValue), Rollout);

    internal static DraftFlag Create(FlagKey key, FlagSettings settings, RolloutSeed seed, long revision) =>
        new(key, settings.IsEnabled, settings.Type, settings.Values.OnValue, settings.Values.OffValue, settings.Rollout, seed, revision);

    public PublishedFlag ToPublished() => new(Key, Settings, Seed);

    internal void Change(FlagSettings settings, long revision)
    {
        if (settings.Type != Type)
        {
            throw new InvalidOperationException("The type of a flag never changes.");
        }

        IsEnabled = settings.IsEnabled;
        OnValue = settings.Values.OnValue;
        OffValue = settings.Values.OffValue;
        Rollout = settings.Rollout;
        ChangedAtRevision = revision;
    }
}
