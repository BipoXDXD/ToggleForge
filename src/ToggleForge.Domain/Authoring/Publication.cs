namespace ToggleForge.Domain.Authoring;

/// <summary>Versão publicada de um ambiente. Imutável: rollback cria outra publicação.</summary>
public sealed class Publication
{
    internal Publication(
        Guid environmentId,
        long version,
        DateTimeOffset publishedAt,
        Guid publishedBy,
        PublicationOrigin origin,
        long? restoredVersion,
        ChangeSummary? summary,
        IReadOnlyList<PublishedFlag> flags)
    {
        EnvironmentId = environmentId;
        Version = version;
        PublishedAt = publishedAt;
        PublishedBy = publishedBy;
        Origin = origin;
        RestoredVersion = restoredVersion;
        Summary = summary;
        Flags = flags;
    }

    /// <summary>Reconstrói uma publicação gravada; não valida nada porque ela já foi validada ao nascer.</summary>
    public static Publication Restore(
        Guid environmentId,
        long version,
        DateTimeOffset publishedAt,
        Guid publishedBy,
        PublicationOrigin origin,
        long? restoredVersion,
        ChangeSummary? summary,
        IReadOnlyList<PublishedFlag> flags) =>
        new(environmentId, version, publishedAt, publishedBy, origin, restoredVersion, summary, flags);

    public Guid EnvironmentId { get; }

    public long Version { get; }

    public DateTimeOffset PublishedAt { get; }

    public Guid PublishedBy { get; }

    public PublicationOrigin Origin { get; }

    public long? RestoredVersion { get; }

    public ChangeSummary? Summary { get; }

    /// <summary>Ordenadas pela chave.</summary>
    public IReadOnlyList<PublishedFlag> Flags { get; }
}
