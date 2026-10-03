using ToggleForge.Domain.Authoring;

namespace ToggleForge.Application.Authoring;

public interface IPublicationRepository
{
    Task<Publication?> FindAsync(Guid environmentId, long version, CancellationToken cancellationToken);

    /// <summary>A publicação em vigor de cada ambiente do projeto que já publicou, numa só consulta.</summary>
    Task<IReadOnlyDictionary<Guid, Publication>> FindCurrentByEnvironmentAsync(Guid projectId, CancellationToken cancellationToken);

    void Add(Publication publication);
}
