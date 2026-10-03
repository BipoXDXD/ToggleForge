using ToggleForge.Domain.Authoring;

namespace ToggleForge.Application.Authoring;

public interface IEnvironmentRepository
{
    /// <summary>Com as flags do rascunho, ordenados pela chave.</summary>
    Task<IReadOnlyList<ProjectEnvironment>> ListAsync(Guid projectId, CancellationToken cancellationToken);

    /// <summary>Com as flags do rascunho.</summary>
    Task<ProjectEnvironment?> FindAsync(EnvironmentLocator locator, CancellationToken cancellationToken);

    /// <summary>
    /// Como <see cref="FindAsync"/>, mas trava o ambiente até o fim da transação aberta: quem escreve no rascunho
    /// entra em fila e lê o estado deixado pelo anterior.
    /// </summary>
    Task<ProjectEnvironment?> FindForUpdateAsync(EnvironmentLocator locator, CancellationToken cancellationToken);

    void Add(ProjectEnvironment environment);
}
