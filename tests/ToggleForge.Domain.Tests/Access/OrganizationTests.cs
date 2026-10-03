using ToggleForge.Domain.Access;

namespace ToggleForge.Domain.Tests.Access;

public sealed class OrganizationTests
{
    [Fact]
    public void Founding_an_organization_makes_the_founder_its_only_owner()
    {
        var founderId = Guid.NewGuid();

        var organization = Organization.Found(Names.Of("Studio"), founderId);

        var membership = Assert.Single(organization.Memberships);
        Assert.Equal(organization.Id, membership.OrganizationId);
        Assert.Equal(founderId, membership.MemberId);
        Assert.Equal(Role.Owner, membership.Role);
    }

    [Theory]
    [InlineData(Role.Owner, true, true)]
    [InlineData(Role.Editor, true, false)]
    [InlineData(Role.Viewer, false, false)]
    public void Roles_grant_authoring_and_administration_by_level(Role role, bool canAuthor, bool canAdminister)
    {
        Assert.Equal(canAuthor, role.CanAuthor());
        Assert.Equal(canAdminister, role.CanAdminister());
    }

    [Fact]
    public void Default_role_is_the_least_privileged()
    {
        Assert.Equal(Role.Viewer, default);
    }
}
