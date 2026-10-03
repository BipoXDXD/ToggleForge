using ToggleForge.Application.Access;
using ToggleForge.Application.Authoring;
using ToggleForge.Domain;
using ToggleForge.Domain.Access;
using ToggleForge.Domain.Delivery;

namespace ToggleForge.Application.Delivery;

public sealed class CredentialService(
    MemberAccess access,
    IEnvironmentDirectory environments,
    ICredentialRepository credentials,
    IDeliveryUnitOfWork unitOfWork,
    TimeProvider clock)
{
    /// <summary>Teto da lista: as ativas (no máximo 5) sempre aparecem, e as revogadas mais recentes completam.</summary>
    public const int MaxListed = 50;

    /// <summary>Da mais nova para a mais antiga. Revogadas antigas saem da lista, mas continuam gravadas.</summary>
    public async Task<Result<IReadOnlyList<Credential>>> ListAsync(
        Guid memberId, EnvironmentLocator locator, CancellationToken cancellationToken)
    {
        if (!(await FindAdministeredEnvironmentAsync(memberId, locator, cancellationToken)).TryGetValue(out var environmentId, out var failure))
        {
            return failure;
        }

        var active = await credentials.ListActiveAsync(environmentId, cancellationToken);
        var revoked = await credentials.ListRecentlyRevokedAsync(environmentId, MaxListed - active.Count, cancellationToken);
        return active.Concat(revoked)
            .OrderByDescending(credential => credential.CreatedAt)
            .ThenBy(credential => credential.Id)
            .ToList();
    }

    /// <summary>
    /// Sob o mesmo lock da emissão: uma revogação simultânea a outra espera e devolve o mesmo instante, e uma
    /// emissão concorrente conta as ativas já sem a revogada.
    /// </summary>
    public async Task<Result<Credential>> RevokeAsync(
        Guid memberId, EnvironmentLocator locator, Guid credentialId, CancellationToken cancellationToken)
    {
        if (!(await FindAdministeredEnvironmentAsync(memberId, locator, cancellationToken)).TryGetValue(out var environmentId, out var failure))
        {
            return failure;
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        await credentials.LockEnvironmentAsync(environmentId, cancellationToken);
        if (await credentials.FindAsync(environmentId, credentialId, cancellationToken) is not { } credential)
        {
            return CredentialFailures.CredentialNotFound;
        }

        credential.Revoke(clock.GetUtcNowInMicroseconds());
        await unitOfWork.SaveOrFailAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return credential;
    }

    public async Task<Result<IssuedCredential>> CreateAsync(
        Guid memberId, EnvironmentLocator locator, DisplayName name, CancellationToken cancellationToken)
    {
        if (!(await FindAdministeredEnvironmentAsync(memberId, locator, cancellationToken)).TryGetValue(out var environmentId, out var failure))
        {
            return failure;
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        await credentials.LockEnvironmentAsync(environmentId, cancellationToken);
        var active = await credentials.CountActiveAsync(environmentId, cancellationToken);
        if (!Credential.Issue(environmentId, name, clock.GetUtcNowInMicroseconds(), active).TryGetValue(out var issued, out failure))
        {
            return failure;
        }

        credentials.Add(issued.Credential);
        await unitOfWork.SaveOrFailAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return issued;
    }

    /// <summary>Credenciais são do Owner: 404 para quem não é da organização, 403 para os outros papéis.</summary>
    private async Task<Result<Guid>> FindAdministeredEnvironmentAsync(
        Guid memberId, EnvironmentLocator locator, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(locator);

        if (!(await access.FindRoleAsync(memberId, locator.OrganizationId, cancellationToken)).TryGetValue(out var role, out var failure))
        {
            return failure;
        }

        if (await environments.FindIdAsync(locator, cancellationToken) is not { } environmentId)
        {
            return AccessFailures.NotFound;
        }

        return MemberAccess.Require(role, RolePermissions.CanAdminister) is { } forbidden ? forbidden : environmentId;
    }
}
