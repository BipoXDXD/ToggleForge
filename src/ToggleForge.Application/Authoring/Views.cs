using ToggleForge.Domain;
using ToggleForge.Domain.Authoring;
using ToggleForge.Evaluation;

namespace ToggleForge.Application.Authoring;

public sealed record ProjectView(ProjectKey Key, DisplayName Name, IReadOnlyList<EnvironmentView> Environments);

public sealed record EnvironmentView(
    EnvironmentKey Key, DisplayName Name, long? CurrentVersion, long DraftRevision, bool HasUnpublishedChanges)
{
    /// <summary><paramref name="current"/> é a publicação em vigor do ambiente, nula se ele nunca publicou.</summary>
    internal static EnvironmentView Of(ProjectEnvironment environment, Publication? current) =>
        new(
            environment.Key,
            environment.Name,
            environment.CurrentVersion,
            environment.DraftRevision,
            HasUnpublishedChanges: environment.PendingChanges(current).Count > 0);
}

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

public sealed record PublicationSummaryView(
    long Version,
    DateTimeOffset PublishedAt,
    MemberView PublishedBy,
    PublicationOrigin Origin,
    long? RestoredVersion,
    ChangeSummary? Summary)
{
    internal static PublicationSummaryView Of(PublicationHeader header, MemberView publisher) =>
        new(header.Version, header.PublishedAt, publisher, header.Origin, header.RestoredVersion, header.Summary);
}

/// <summary><see cref="NextBeforeVersion"/> é nulo na última página.</summary>
public sealed record PublicationPage(IReadOnlyList<PublicationSummaryView> Items, long? NextBeforeVersion);

/// <summary><see cref="Version"/> é a publicação simulada; nula quando foi o rascunho.</summary>
public sealed record SimulationView(long? Version, IReadOnlyList<SimulationResultView> Results);

public sealed record SimulationResultView(FlagKey FlagKey, bool Value, EvaluationReason Reason, int? Bucket)
{
    internal static SimulationResultView Of(FlagKey key, EvaluationResult<bool> result) =>
        new(key, result.Value, result.Reason, result.Bucket);
}

/// <summary>O que o SDK recebe: nada além do necessário para avaliar.</summary>
public sealed record SnapshotView(
    ProjectKey ProjectKey, EnvironmentKey EnvironmentKey, long Version, DateTimeOffset PublishedAt, IReadOnlyList<PublishedFlag> Flags);
