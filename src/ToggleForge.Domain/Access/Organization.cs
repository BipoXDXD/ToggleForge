namespace ToggleForge.Domain.Access;

public sealed class Organization
{
    private readonly List<Membership> _memberships = [];

    private Organization(Guid id, DisplayName name)
    {
        Id = id;
        Name = name;
    }

    public Guid Id { get; }

    public DisplayName Name { get; }

    public IReadOnlyList<Membership> Memberships => _memberships;

    public static Organization Found(DisplayName name, Guid founderId)
    {
        ArgumentNullException.ThrowIfNull(name);

        var organization = new Organization(Guid.NewGuid(), name);
        organization._memberships.Add(new Membership(organization.Id, founderId, Role.Owner));
        return organization;
    }
}
