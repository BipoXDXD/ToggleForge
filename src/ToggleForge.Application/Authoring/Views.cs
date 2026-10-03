using ToggleForge.Domain;
using ToggleForge.Domain.Authoring;
using ToggleForge.Evaluation;

namespace ToggleForge.Application.Authoring;

public sealed record ProjectView(ProjectKey Key, DisplayName Name, IReadOnlyList<EnvironmentView> Environments);

public sealed record EnvironmentView(
    EnvironmentKey Key, DisplayName Name, long? CurrentVersion, long DraftRevision, bool HasUnpublishedChanges);

public sealed record FlagView(FlagKey Key, FlagSettings Settings, long Revision)
{
    internal static FlagView Of(DraftFlag flag) => new(flag.Key, flag.Settings, flag.ChangedAtRevision);
}

public sealed record DraftView(
    long Revision, long? BasedOnVersion, IReadOnlyList<FlagView> Flags, IReadOnlyList<DraftChange> Changes);

public sealed record MemberView(Guid Id, string Email);

public sealed record PublicationView(
    long Version,
    DateTimeOffset PublishedAt,
    MemberView PublishedBy,
    PublicationOrigin Origin,
    long? RestoredVersion,
    ChangeSummary? Summary,
    IReadOnlyList<PublishedFlag> Flags);

/// <summary>O que o SDK recebe: nada além do necessário para avaliar.</summary>
public sealed record SnapshotView(
    ProjectKey ProjectKey, EnvironmentKey EnvironmentKey, long Version, DateTimeOffset PublishedAt, IReadOnlyList<PublishedFlag> Flags);
