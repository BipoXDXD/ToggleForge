using ToggleForge.Application.Access;
using ToggleForge.Domain;
using ToggleForge.Domain.Access;
using ToggleForge.Domain.Authoring;
using ToggleForge.Evaluation;

namespace ToggleForge.Application.Authoring;

public sealed class DraftService(
    MemberAccess access,
    IEnvironmentRepository environments,
    IPublicationRepository publications,
    IAuthoringUnitOfWork unitOfWork)
{
    public async Task<Result<DraftView>> GetAsync(Guid memberId, EnvironmentLocator locator, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(locator);

        if (!(await access.FindRoleAsync(memberId, locator.OrganizationId, cancellationToken)).TryGetValue(out _, out var failure))
        {
            return failure;
        }

        if (await environments.FindAsync(locator, cancellationToken) is not { } environment)
        {
            return AccessFailures.NotFound;
        }

        var current = await publications.FindAsync(environment, cancellationToken);
        return new DraftView(
            environment.DraftRevision,
            environment.CurrentVersion,
            environment.Flags.OrderBy(flag => flag.Key.Value, StringComparer.Ordinal).Select(FlagView.Of).ToList(),
            environment.PendingChanges(current));
    }

    public async Task<Result<FlagView>> CreateFlagAsync(
        Guid memberId, EnvironmentLocator locator, FlagKey key, FlagSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(locator);

        if (!(await access.FindRoleAsync(memberId, locator.OrganizationId, cancellationToken)).TryGetValue(out var role, out var failure))
        {
            return failure;
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        if (await environments.FindForUpdateAsync(locator, cancellationToken) is not { } environment)
        {
            return AccessFailures.NotFound;
        }

        if (MemberAccess.Require(role, RolePermissions.CanAuthor) is { } forbidden)
        {
            return forbidden;
        }

        if (!environment.AddFlag(key, settings, RolloutSeeds.Generate()).TryGetValue(out var flag, out failure))
        {
            return failure;
        }

        await unitOfWork.SaveOrFailAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return FlagView.Of(flag);
    }
}
