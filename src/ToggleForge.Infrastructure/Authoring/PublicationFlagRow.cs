namespace ToggleForge.Infrastructure.Authoring;

internal sealed class PublicationFlagRow
{
    public required string Key { get; init; }

    public required bool IsEnabled { get; init; }

    public required bool OnValue { get; init; }

    public required bool OffValue { get; init; }

    public required int RolloutBasisPoints { get; init; }

    public required string Seed { get; init; }
}
