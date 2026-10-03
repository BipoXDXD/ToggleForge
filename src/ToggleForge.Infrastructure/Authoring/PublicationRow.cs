namespace ToggleForge.Infrastructure.Authoring;

/// <summary>
/// Modelo de persistência da publicação. O domínio é imutável e passa as flags pelo construtor, o que o
/// mapeamento direto do EF não comporta; como publicações só são inseridas e lidas, a tradução é trivial.
/// </summary>
internal sealed class PublicationRow
{
    public required Guid EnvironmentId { get; init; }

    public required long Version { get; init; }

    public required DateTimeOffset PublishedAt { get; init; }

    public required Guid PublishedBy { get; init; }

    public required string Origin { get; init; }

    public long? RestoredVersion { get; init; }

    public string? Summary { get; init; }

    public List<PublicationFlagRow> Flags { get; init; } = [];
}
