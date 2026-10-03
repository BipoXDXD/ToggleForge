using ToggleForge.Application;
using ToggleForge.Application.Delivery;
using ToggleForge.Infrastructure.Persistence;

namespace ToggleForge.Infrastructure.Delivery;

internal sealed class DeliveryUnitOfWork(DeliveryDbContext context)
    : UnitOfWork<DeliveryDbContext>(context), IDeliveryUnitOfWork
{
    protected override SaveOutcome? OutcomeOf(string? constraintName) => null;
}
