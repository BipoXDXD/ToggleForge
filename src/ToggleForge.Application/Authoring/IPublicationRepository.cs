using ToggleForge.Domain.Authoring;

namespace ToggleForge.Application.Authoring;

public interface IPublicationRepository
{
    Task<Publication?> FindAsync(Guid environmentId, long version, CancellationToken cancellationToken);

    /// <summary>Até <paramref name="limit"/> versões abaixo de <paramref name="beforeVersion"/>, da mais nova à mais antiga.</summary>
    Task<IReadOnlyList<PublicationHeader>> ListAsync(
        Guid environmentId, long? beforeVersion, int limit, CancellationToken cancellationToken);

    /// <summary>A publicação em vigor de cada ambiente dos projetos dados que já publicou, numa só consulta.</summary>
    Task<IReadOnlyDictionary<Guid, Publication>> FindCurrentByEnvironmentAsync(
        IReadOnlyCollection<Guid> projectIds, CancellationToken cancellationToken);

    /// <summary>O tipo de cada chave já publicada no ambiente, inclusive as que saíram do rascunho.</summary>
    Task<PublishedFlagTypes> FindPublishedTypesAsync(Guid environmentId, CancellationToken cancellationToken);

    /// <summary>
    /// Grava a publicação e trava o tipo das chaves que ela publica pela primeira vez, fora de
    /// <paramref name="alreadyPublished"/>. O banco recusa uma chave com tipo diferente do travado.
    /// </summary>
    void Add(Publication publication, PublishedFlagTypes alreadyPublished);
}
