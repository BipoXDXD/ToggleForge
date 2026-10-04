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

    public async Task<Result<FlagView>> GetFlagAsync(
        Guid memberId, EnvironmentLocator locator, FlagKey key, CancellationToken cancellationToken)
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

        return environment.Flags.FirstOrDefault(flag => flag.Key == key) is { } found
            ? FlagView.Of(found)
            : AuthoringFailures.FlagNotFound;
    }

    public async Task<Result<FlagView>> CreateFlagAsync(
        Guid memberId, EnvironmentLocator locator, FlagKey key, FlagSettings settings, CancellationToken cancellationToken) =>
        (await EditAsync(
            memberId,
            locator,
            async (environment, ct) => environment.AddFlag(
                key, settings, RolloutSeeds.Generate(), await publications.FindPublishedTypesAsync(environment.Id, ct)),
            cancellationToken))
        .Map(FlagView.Of);

    public async Task<Result<FlagView>> ChangeFlagAsync(
        Guid memberId, EnvironmentLocator locator, FlagKey key, long expectedRevision, FlagChange change, CancellationToken cancellationToken) =>
        (await EditAsync(memberId, locator, (environment, _) => Task.FromResult(environment.ChangeFlag(key, expectedRevision, change)), cancellationToken))
        .Map(FlagView.Of);

    public async Task<Result<FlagView>> RemoveFlagAsync(
        Guid memberId, EnvironmentLocator locator, FlagKey key, long expectedRevision, CancellationToken cancellationToken) =>
        (await EditAsync(memberId, locator, (environment, _) => Task.FromResult(environment.RemoveFlag(key, expectedRevision)), cancellationToken))
        .Map(FlagView.Of);

    /// <summary>
    /// Toda escrita no rascunho: 404 antes de 403, ambiente travado até o commit (ADR 0005) e a edição do
    /// domínio decide o resto.
    /// </summary>
    private async Task<Result<DraftFlag>> EditAsync(
        Guid memberId,
        EnvironmentLocator locator,
        Func<ProjectEnvironment, CancellationToken, Task<Result<DraftFlag>>> edit,
        CancellationToken cancellationToken)
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

        if (!(await edit(environment, cancellationToken)).TryGetValue(out var flag, out failure))
        {
            return failure;
        }

        await unitOfWork.SaveOrFailAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return flag;
    }
}
