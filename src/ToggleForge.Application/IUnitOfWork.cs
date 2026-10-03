namespace ToggleForge.Application;

/// <summary>Unidade de trabalho de um módulo; cada módulo tem a sua, sobre o próprio schema.</summary>
public interface IUnitOfWork
{
    Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken);

    Task<SaveOutcome> SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>Descarta o que foi carregado ou alterado, para reler o estado depois de um conflito.</summary>
    void DiscardChanges();
}
