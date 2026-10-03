using ToggleForge.Domain;
using ToggleForge.Domain.Access;

namespace ToggleForge.Application.Access;

/// <summary>
/// Ordem de checagem do contrato: primeiro "existe para este membro?" (404), depois "o papel permite?" (403).
/// </summary>
public sealed class MemberAccess(IMembershipDirectory memberships)
{
    public async Task<Result<Role>> FindRoleAsync(Guid memberId, Guid organizationId, CancellationToken cancellationToken)
    {
        var role = await memberships.FindRoleAsync(memberId, organizationId, cancellationToken);
        return role is { } found ? found : AccessFailures.NotFound;
    }

    public static Failure? Require(Role role, Func<Role, bool> permission)
    {
        ArgumentNullException.ThrowIfNull(permission);
        return permission(role) ? null : AccessFailures.Forbidden;
    }
}
