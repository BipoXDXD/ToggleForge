using Microsoft.EntityFrameworkCore;
using ToggleForge.Application.Access;
using ToggleForge.Domain.Access;

namespace ToggleForge.Infrastructure.Access;

internal sealed class MembershipDirectory(AccessDbContext context) : IMembershipDirectory
{
    public async Task<Role?> FindRoleAsync(Guid memberId, Guid organizationId, CancellationToken cancellationToken) =>
        await context.Memberships
            .AsNoTracking()
            .Where(m => m.MemberId == memberId && m.OrganizationId == organizationId)
            .Select(m => (Role?)m.Role)
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<MembershipView>> ListAsync(Guid memberId, CancellationToken cancellationToken)
    {
        var memberships = await context.Memberships
            .AsNoTracking()
            .Where(m => m.MemberId == memberId)
            .Join(context.Organizations, m => m.OrganizationId, o => o.Id, (m, o) => new { o.Id, o.Name, m.Role })
            .ToListAsync(cancellationToken);
        return memberships
            .Select(m => new MembershipView(m.Id, m.Name, m.Role))
            .OrderBy(m => m.OrganizationName.Value, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<IReadOnlyDictionary<Guid, string>> FindEmailsAsync(
        IReadOnlyCollection<Guid> memberIds, CancellationToken cancellationToken) =>
        await context.Users
            .AsNoTracking()
            .Where(user => memberIds.Contains(user.Id) && user.Email != null)
            .ToDictionaryAsync(user => user.Id, user => user.Email ?? string.Empty, cancellationToken);
}
