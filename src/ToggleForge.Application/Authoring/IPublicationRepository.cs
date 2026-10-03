using ToggleForge.Domain.Authoring;

namespace ToggleForge.Application.Authoring;

public interface IPublicationRepository
{
    Task<Publication?> FindAsync(Guid environmentId, long version, CancellationToken cancellationToken);

    /// <summary>Até <paramref name="limit"/> versões abaixo de <paramref name="beforeVersion"/>, da mais nova à mais antiga.</summary>
    Task<IReadOnlyList<PublicationHeader>> ListAsync(
        Guid environmentId, long? beforeVersion, int limit, CancellationToken cancellationToken);

    /// <summary>A publicação em vigor de cada ambiente do projeto que já publicou, numa só consulta.</summary>
    Task<IReadOnlyDictionary<Guid, Publication>> FindCurrentByEnvironmentAsync(Guid projectId, CancellationToken cancellationToken);

    void Add(Publication publication);
}
