using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ToggleForge.Domain.Access;
using ToggleForge.Infrastructure.Persistence;

namespace ToggleForge.Infrastructure.Access;

/// <summary>Só usuários do Identity: papéis são por organização (<see cref="Membership"/>), não globais.</summary>
internal sealed class AccessDbContext(DbContextOptions<AccessDbContext> options)
    : IdentityUserContext<IdentityUser<Guid>, Guid>(options)
{
    public const string Schema = "access";

    public DbSet<Organization> Organizations => Set<Organization>();

    public DbSet<Membership> Memberships => Set<Membership>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema(Schema);

        // O Identity fixa nomes como "AspNetUsers"; aqui eles seguem o snake_case do resto do banco.
        builder.Entity<IdentityUser<Guid>>().ToTable("users");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");

        // O Identity checa e-mail repetido antes de inserir; só o índice único fecha a corrida.
        builder.Entity<IdentityUser<Guid>>().HasIndex(user => user.NormalizedEmail).IsUnique().HasDatabaseName("ix_users_normalized_email");
        builder.Entity<IdentityUser<Guid>>().HasIndex(user => user.NormalizedUserName).HasDatabaseName("ix_users_normalized_user_name");

        builder.Entity<Organization>(organization =>
        {
            organization.HasKey(o => o.Id);
            organization.Property(o => o.Id).ValueGeneratedNever();
            organization.Property(o => o.Name).HasConversion(ValueConverters.DisplayName);
            organization.ToTable(table => table.HasCheckConstraint("ck_organizations_name", SqlChecks.DisplayName("name")));
            organization.HasMany(o => o.Memberships).WithOne().HasForeignKey(m => m.OrganizationId);
            organization.Navigation(o => o.Memberships).HasField("_memberships").UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        builder.Entity<Membership>(membership =>
        {
            membership.HasKey(m => new { m.OrganizationId, m.MemberId });
            membership.Property(m => m.Role).HasConversion(role => role.ToString().ToLowerInvariant(), value => Enum.Parse<Role>(value, true));
            membership.ToTable(table => table.HasCheckConstraint("ck_memberships_role", "role in ('viewer', 'editor', 'owner')"));
            membership.HasOne<IdentityUser<Guid>>().WithMany().HasForeignKey(m => m.MemberId);
            membership.HasIndex(m => m.MemberId);
        });
    }
}
