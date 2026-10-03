using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using ToggleForge.Application;

namespace ToggleForge.Infrastructure.Persistence;

internal abstract class UnitOfWork<TContext>(TContext context) : IUnitOfWork
    where TContext : DbContext
{
    public async Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken) =>
        new Transaction(await context.Database.BeginTransactionAsync(cancellationToken));

    public async Task<SaveOutcome> SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return SaveOutcome.Saved;
        }
        catch (DbUpdateConcurrencyException)
        {
            return SaveOutcome.ConcurrencyConflict;
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } violation
            && OutcomeOf(violation.ConstraintName) is { } outcome)
        {
            return outcome;
        }
    }

    public void DiscardChanges() => context.ChangeTracker.Clear();

    /// <summary>Violações de unicidade que significam disputa esperada; as outras são defeito e propagam.</summary>
    protected abstract SaveOutcome? OutcomeOf(string? constraintName);

    private sealed class Transaction(IDbContextTransaction transaction) : IUnitOfWorkTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken) => transaction.CommitAsync(cancellationToken);

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
