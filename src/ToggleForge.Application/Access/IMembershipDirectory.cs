using ToggleForge.Domain.Access;

namespace ToggleForge.Application.Access;

/// <summary>API pública do módulo Access para os outros módulos.</summary>
public interface IMembershipDirectory
{
    /// <summary>Nulo quando a organização não existe ou o membro não participa dela.</summary>
    Task<Role?> FindRoleAsync(Guid memberId, Guid organizationId, CancellationToken cancellationToken);

    Task<IReadOnlyList<MembershipView>> ListAsync(Guid memberId, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, string>> FindEmailsAsync(
        IReadOnlyCollection<Guid> memberIds, CancellationToken cancellationToken);
}
