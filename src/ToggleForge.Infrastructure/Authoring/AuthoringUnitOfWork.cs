using ToggleForge.Application;
using ToggleForge.Application.Authoring;
using ToggleForge.Infrastructure.Persistence;

namespace ToggleForge.Infrastructure.Authoring;

internal sealed class AuthoringUnitOfWork(AuthoringDbContext context)
    : UnitOfWork<AuthoringDbContext>(context), IAuthoringUnitOfWork
{
    protected override SaveOutcome? OutcomeOf(string? constraintName) => constraintName switch
    {
        AuthoringDbContext.IdempotencyKeyConstraint => SaveOutcome.IdempotencyKeyTaken,
        AuthoringDbContext.PublicationVersionConstraint => SaveOutcome.ConcurrencyConflict,
        _ => null,
    };
}
