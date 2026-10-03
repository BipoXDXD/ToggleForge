using ToggleForge.Domain.Authoring;

namespace ToggleForge.Application.Authoring;

public interface IEnvironmentRepository
{
    /// <summary>Os ambientes de todos os projetos dados numa consulta só, com as flags do rascunho.</summary>
    Task<IReadOnlyList<ProjectEnvironment>> ListAsync(IReadOnlyCollection<Guid> projectIds, CancellationToken cancellationToken);

    /// <summary>Com as flags do rascunho.</summary>
    Task<ProjectEnvironment?> FindAsync(EnvironmentLocator locator, CancellationToken cancellationToken);

    /// <summary>
    /// Como <see cref="FindAsync"/>, mas trava o ambiente até o fim da transação aberta: quem escreve no rascunho
    /// entra em fila e lê o estado deixado pelo anterior.
    /// </summary>
    Task<ProjectEnvironment?> FindForUpdateAsync(EnvironmentLocator locator, CancellationToken cancellationToken);

    void Add(ProjectEnvironment environment);
}
