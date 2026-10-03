using ToggleForge.Evaluation;

namespace ToggleForge.Domain.Authoring;

public sealed class DraftFlag
{
    private DraftFlag(
        FlagKey key, bool isEnabled, bool onValue, bool offValue, RolloutPercentage rollout, RolloutSeed seed, long changedAtRevision)
    {
        Key = key;
        IsEnabled = isEnabled;
        OnValue = onValue;
        OffValue = offValue;
        Rollout = rollout;
        Seed = seed;
        ChangedAtRevision = changedAtRevision;
    }

    public FlagKey Key { get; }

    public bool IsEnabled { get; private set; }

    public bool OnValue { get; private set; }

    public bool OffValue { get; private set; }

    public RolloutPercentage Rollout { get; private set; }

    public RolloutSeed Seed { get; }

    /// <summary>Revisão do rascunho em que a flag mudou pela última vez; é a ETag da flag.</summary>
    public long ChangedAtRevision { get; private set; }

    public FlagSettings Settings => new(IsEnabled, OnValue, OffValue, Rollout);

    internal static DraftFlag Create(FlagKey key, FlagSettings settings, RolloutSeed seed, long revision) =>
        new(key, settings.IsEnabled, settings.OnValue, settings.OffValue, settings.Rollout, seed, revision);

    public PublishedFlag ToPublished() => new(Key, Settings, Seed);

    internal void Change(FlagSettings settings, long revision)
    {
        IsEnabled = settings.IsEnabled;
        OnValue = settings.OnValue;
        OffValue = settings.OffValue;
        Rollout = settings.Rollout;
        ChangedAtRevision = revision;
    }
}
