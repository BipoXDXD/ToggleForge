using Microsoft.EntityFrameworkCore;
using ToggleForge.Application.Authoring;
using ToggleForge.Domain.Authoring;
using ToggleForge.Infrastructure.Persistence;

namespace ToggleForge.Infrastructure.Authoring;

internal sealed class AuthoringDbContext(DbContextOptions<AuthoringDbContext> options) : DbContext(options)
{
    public const string Schema = "authoring";

    public DbSet<Project> Projects => Set<Project>();

    public DbSet<ProjectEnvironment> Environments => Set<ProjectEnvironment>();

    public DbSet<PublicationRow> Publications => Set<PublicationRow>();

    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        ConfigureProjects(modelBuilder);
        ConfigureEnvironments(modelBuilder);
        ConfigurePublications(modelBuilder);
        ConfigureIdempotency(modelBuilder);
    }

    private static void ConfigureProjects(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Project>(project =>
        {
            project.HasKey(p => p.Id);
            project.Property(p => p.Id).ValueGeneratedNever();
            project.Property(p => p.OrganizationId);
            project.Property(p => p.Key).HasConversion(ValueConverters.ProjectKey);
            project.Property(p => p.Name).HasConversion(ValueConverters.DisplayName);
            project.HasIndex(p => new { p.OrganizationId, p.Key }).IsUnique();
            project.ToTable(table =>
            {
                table.HasCheckConstraint("ck_projects_key", SqlChecks.Key("key"));
                table.HasCheckConstraint("ck_projects_name", SqlChecks.DisplayName("name"));
            });
        });

    private static void ConfigureEnvironments(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<ProjectEnvironment>(environment =>
        {
            environment.ToTable("environments", table =>
            {
                table.HasCheckConstraint("ck_environments_key", SqlChecks.Key("key"));
                table.HasCheckConstraint("ck_environments_name", SqlChecks.DisplayName("name"));
                table.HasCheckConstraint("ck_environments_draft_revision", "draft_revision >= 1");
                table.HasCheckConstraint("ck_environments_current_version", "current_version >= 1");
            });
            environment.HasKey(e => e.Id);
            environment.Property(e => e.Id).ValueGeneratedNever();
            environment.Property(e => e.ProjectId);
            environment.Property(e => e.Key).HasConversion(ValueConverters.EnvironmentKey);
            environment.Property(e => e.Name).HasConversion(ValueConverters.DisplayName);
            environment.Property(e => e.DraftRevision);
            environment.Property(e => e.CurrentVersion);
            environment.HasOne<Project>().WithMany().HasForeignKey(e => e.ProjectId).OnDelete(DeleteBehavior.Restrict);
            environment.HasIndex(e => new { e.ProjectId, e.Key }).IsUnique();

            // xmin: qualquer mudança no rascunho ou na versão em vigor invalida leituras anteriores.
            environment.Property<uint>("RowVersion").IsRowVersion();

            environment.OwnsMany(e => e.Flags, flag =>
            {
                flag.ToTable("draft_flags", table =>
                {
                    table.HasCheckConstraint("ck_draft_flags_key", SqlChecks.Key("key"));
                    table.HasCheckConstraint("ck_draft_flags_rollout", SqlChecks.BasisPoints("rollout"));
                    table.HasCheckConstraint("ck_draft_flags_type", SqlChecks.FlagType("type"));
                    table.HasCheckConstraint("ck_draft_flags_boolean_values", SqlChecks.BooleanValues("type", "on_value", "off_value"));
                });
                flag.WithOwner().HasForeignKey("EnvironmentId");
                flag.HasKey("EnvironmentId", nameof(DraftFlag.Key));
                flag.Property(f => f.Key).HasConversion(ValueConverters.FlagKey);
                flag.Property(f => f.IsEnabled);
                flag.Property(f => f.Type).HasConversion(ValueConverters.FlagType);
                flag.Property(f => f.OnValue).HasConversion(ValueConverters.JsonFlagValue).HasColumnType("json");
                flag.Property(f => f.OffValue).HasConversion(ValueConverters.JsonFlagValue).HasColumnType("json");
                flag.Property(f => f.Rollout).HasConversion(ValueConverters.RolloutPercentage);
                flag.Property(f => f.Seed).HasConversion(ValueConverters.RolloutSeed);
                flag.Property(f => f.ChangedAtRevision);
                flag.Ignore(f => f.Settings);
            });
            environment.Navigation(e => e.Flags).HasField("_flags").UsePropertyAccessMode(PropertyAccessMode.Field);
        });

    private static void ConfigurePublications(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<PublicationRow>(publication =>
        {
            publication.ToTable("publications", table =>
            {
                table.HasCheckConstraint("ck_publications_version", "version >= 1");
                table.HasCheckConstraint("ck_publications_origin", "origin in ('publication', 'rollback')");
                table.HasCheckConstraint(
                    "ck_publications_restored_version", "(origin = 'rollback') = (restored_version is not null)");
                table.HasCheckConstraint("ck_publications_summary", "char_length(summary) between 1 and 280");
            });
            publication.HasKey(p => new { p.EnvironmentId, p.Version });
            publication.HasOne<ProjectEnvironment>().WithMany().HasForeignKey(p => p.EnvironmentId).OnDelete(DeleteBehavior.Restrict);
            publication.OwnsMany(p => p.Flags, flag =>
            {
                flag.ToTable("publication_flags", table =>
                {
                    table.HasCheckConstraint("ck_publication_flags_key", SqlChecks.Key("key"));
                    table.HasCheckConstraint("ck_publication_flags_rollout", SqlChecks.BasisPoints("rollout_basis_points"));
                    table.HasCheckConstraint("ck_publication_flags_type", SqlChecks.FlagType("type"));
                    table.HasCheckConstraint("ck_publication_flags_boolean_values", SqlChecks.BooleanValues("type", "on_value", "off_value"));
                });
                flag.WithOwner().HasForeignKey("EnvironmentId", "Version");
                flag.HasKey("EnvironmentId", "Version", nameof(PublicationFlagRow.Key));
                flag.Property(f => f.Type).HasConversion(ValueConverters.FlagType);
                flag.Property(f => f.OnValue).HasConversion(ValueConverters.JsonFlagValue).HasColumnType("json");
                flag.Property(f => f.OffValue).HasConversion(ValueConverters.JsonFlagValue).HasColumnType("json");
            });
        });

    private static void ConfigureIdempotency(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<IdempotencyRecord>(record =>
        {
            record.ToTable("idempotency_records", table =>
                table.HasCheckConstraint("ck_idempotency_records_key", "key ~ '^[A-Za-z0-9_-]{16,64}$'"));
            record.HasKey(r => new { r.MemberId, r.Key }).HasName(IdempotencyKeyConstraint);
            record.HasOne<PublicationRow>().WithMany().HasForeignKey(r => new { r.EnvironmentId, r.Version }).OnDelete(DeleteBehavior.Restrict);
        });

    public const string IdempotencyKeyConstraint = "pk_idempotency_records";

    public const string PublicationVersionConstraint = "pk_publications";
}
