using ToggleForge.Application.Access;

namespace ToggleForge.Api.Tests.Support;

/// <summary>Faz toda leitura de membros falhar, para provocar o 500 do exception handler.</summary>
public sealed class BrokenMembershipDirectory : IMembershipDirectory
{
    public Task<Domain.Access.Role?> FindRoleAsync(Guid memberId, Guid organizationId, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Broken on purpose.");

    public Task<IReadOnlyList<MembershipView>> ListAsync(Guid memberId, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Broken on purpose.");

    public Task<IReadOnlyDictionary<Guid, string>> FindEmailsAsync(IReadOnlyCollection<Guid> memberIds, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Broken on purpose.");
}
