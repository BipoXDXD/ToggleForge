namespace ToggleForge.Domain.Access;

public sealed class Membership
{
    internal Membership(Guid organizationId, Guid memberId, Role role)
    {
        OrganizationId = organizationId;
        MemberId = memberId;
        Role = role;
    }

    public Guid OrganizationId { get; }

    public Guid MemberId { get; }

    public Role Role { get; }
}
