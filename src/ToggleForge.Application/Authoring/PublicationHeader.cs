using ToggleForge.Domain.Authoring;

namespace ToggleForge.Application.Authoring;

/// <summary>Uma publicação sem as flags: o que o histórico lista.</summary>
public sealed record PublicationHeader(
    long Version, DateTimeOffset PublishedAt, Guid PublishedBy, PublicationOrigin Origin, long? RestoredVersion, ChangeSummary? Summary);
