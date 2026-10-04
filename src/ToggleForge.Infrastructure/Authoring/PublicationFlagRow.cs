using ToggleForge.Domain.Authoring;

namespace ToggleForge.Infrastructure.Authoring;

internal sealed class PublicationFlagRow
{
    public required string Key { get; init; }

    public required bool IsEnabled { get; init; }

    public required FlagType Type { get; init; }

    public required JsonFlagValue OnValue { get; init; }

    public required JsonFlagValue OffValue { get; init; }

    public required int RolloutBasisPoints { get; init; }

    public required string Seed { get; init; }
}
