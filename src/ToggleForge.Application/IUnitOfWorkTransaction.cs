namespace ToggleForge.Application;

/// <summary>Descartar sem <see cref="CommitAsync"/> desfaz tudo.</summary>
public interface IUnitOfWorkTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken);
}
