using ToggleForge.Domain.Authoring;
using ToggleForge.Evaluation;

namespace ToggleForge.Application.Authoring;

public interface IProjectRepository
{
    /// <summary>Serializa a criação de projetos da organização até o fim da transação.</summary>
    Task LockOrganizationAsync(Guid organizationId, CancellationToken cancellationToken);

    Task<int> CountAsync(Guid organizationId, CancellationToken cancellationToken);

    Task<Project?> FindAsync(Guid organizationId, ProjectKey key, CancellationToken cancellationToken);

    void Add(Project project);
}
