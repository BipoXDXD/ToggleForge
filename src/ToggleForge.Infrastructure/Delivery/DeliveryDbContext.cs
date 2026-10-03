using Microsoft.EntityFrameworkCore;
using ToggleForge.Domain.Delivery;
using ToggleForge.Infrastructure.Persistence;

namespace ToggleForge.Infrastructure.Delivery;

/// <summary>
/// <c>environment_id</c> aponta para o módulo Authoring, por isso não tem FK: nenhum módulo amarra o
/// schema de outro.
/// </summary>
internal sealed class DeliveryDbContext(DbContextOptions<DeliveryDbContext> options) : DbContext(options)
{
    public const string Schema = "delivery";

    public DbSet<Credential> Credentials => Set<Credential>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<Credential>(credential =>
        {
            credential.ToTable(table =>
            {
                table.HasCheckConstraint("ck_credentials_name", SqlChecks.DisplayName("name"));
                table.HasCheckConstraint("ck_credentials_prefix", "prefix ~ '^tfsdk_[A-Za-z0-9_-]{4}$'");
                table.HasCheckConstraint("ck_credentials_secret_hash", "secret_hash ~ '^[0-9a-f]{64}$'");
                table.HasCheckConstraint("ck_credentials_last_used_after_creation", "last_used_at >= created_at");
            });
            credential.HasKey(c => c.Id);
            credential.Property(c => c.Id).ValueGeneratedNever();
            credential.Property(c => c.EnvironmentId);
            credential.Property(c => c.Name).HasConversion(ValueConverters.DisplayName);
            credential.Property(c => c.Prefix);
            credential.Property(c => c.SecretHash);
            credential.Property(c => c.CreatedAt);
            credential.Property(c => c.RevokedAt);
            credential.Property(c => c.LastUsedAt);
            credential.HasIndex(c => c.SecretHash).IsUnique();
            credential.HasIndex(c => c.EnvironmentId);
        });
    }
}
