namespace ToggleForge.Application.Authoring;

/// <summary>API pública do módulo Authoring para a distribuição.</summary>
public interface IPublishedSnapshots
{
    /// <summary>Nulo enquanto o ambiente não tem publicação.</summary>
    Task<SnapshotView?> FindCurrentAsync(Guid environmentId, CancellationToken cancellationToken);
}
