using ToggleForge.Domain.Authoring;
using ToggleForge.Evaluation;

namespace ToggleForge.Application.Authoring;

public interface IProjectRepository
{
    /// <summary>Serializa a criação de projetos da organização até o fim da transação.</summary>
    Task LockOrganizationAsync(Guid organizationId, CancellationToken cancellationToken);

    Task<int> CountAsync(Guid organizationId, CancellationToken cancellationToken);

    /// <summary>Serializa a criação de ambientes do projeto até o fim da transação.</summary>
    Task LockEnvironmentsAsync(Guid projectId, CancellationToken cancellationToken);

    Task<Project?> FindAsync(Guid organizationId, ProjectKey key, CancellationToken cancellationToken);

    /// <summary>Ordenados pela chave.</summary>
    Task<IReadOnlyList<Project>> ListAsync(Guid organizationId, CancellationToken cancellationToken);

    void Add(Project project);
}
