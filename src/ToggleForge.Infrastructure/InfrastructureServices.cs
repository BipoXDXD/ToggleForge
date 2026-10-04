using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Npgsql;
using ToggleForge.Application.Access;
using ToggleForge.Application.Authoring;
using ToggleForge.Application.Delivery;
using ToggleForge.Infrastructure.Access;
using ToggleForge.Infrastructure.Authoring;
using ToggleForge.Infrastructure.Delivery;

namespace ToggleForge.Infrastructure;

public static class InfrastructureServices
{
    private const string MigrationsHistoryTable = "__ef_migrations_history";

    public static IServiceCollection AddToggleForgeInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<AccessDbContext>(options => UsePostgres(options, connectionString, AccessDbContext.Schema));
        services.AddDbContext<AuthoringDbContext>(options => UsePostgres(options, connectionString, AuthoringDbContext.Schema));
        services.AddDbContext<DeliveryDbContext>(options => UsePostgres(options, connectionString, DeliveryDbContext.Schema));

        services.AddIdentityCore<IdentityUser<Guid>>(ConfigureIdentity)
            .AddEntityFrameworkStores<AccessDbContext>()
            .AddSignInManager()
            .AddPasswordValidator<BreachedPasswordValidator>();
        services.AddHttpClient<PwnedPasswordsClient>(client =>
            {
                client.BaseAddress = PwnedPasswordsClient.BaseAddress;
                client.DefaultRequestHeaders.UserAgent.ParseAdd("ToggleForge");
            })
            .AddStandardResilienceHandler(ConfigurePwnedPasswordsResilience);

        services.AddScoped<IMembershipDirectory, MembershipDirectory>();
        services.AddScoped<IAccountRegistry, AccountRegistry>();

        services.AddScoped<IAuthoringUnitOfWork, AuthoringUnitOfWork>();
        services.AddScoped<IProjectRepository, ProjectRepository>();
        services.AddScoped<IEnvironmentRepository, EnvironmentRepository>();
        services.AddScoped<IPublicationRepository, PublicationRepository>();
        services.AddScoped<IIdempotencyStore, IdempotencyStore>();
        services.AddScoped<AuthoringQueries>();
        services.AddScoped<IEnvironmentDirectory>(provider => provider.GetRequiredService<AuthoringQueries>());
        services.AddScoped<IPublishedSnapshots>(provider => provider.GetRequiredService<AuthoringQueries>());

        services.AddScoped<IDeliveryUnitOfWork, DeliveryUnitOfWork>();
        services.AddScoped<ICredentialRepository, CredentialRepository>();
        return services;
    }

    /// <summary>Chaves do Data Protection na tabela do módulo Access, compartilhadas por todas as instâncias (ADR 0008).</summary>
    public static IDataProtectionBuilder PersistKeysToToggleForgeDatabase(this IDataProtectionBuilder builder) =>
        builder.PersistKeysToDbContext<AccessDbContext>();

    /// <summary>Aplica as migrations dos três módulos, na ordem das dependências.</summary>
    public static async Task MigrateToggleForgeDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AccessDbContext>().Database.MigrateAsync(cancellationToken);
        await scope.ServiceProvider.GetRequiredService<AuthoringDbContext>().Database.MigrateAsync(cancellationToken);
        await scope.ServiceProvider.GetRequiredService<DeliveryDbContext>().Database.MigrateAsync(cancellationToken);
    }

    private static void UsePostgres(DbContextOptionsBuilder options, string connectionString, string schema)
    {
        // application_name por módulo aparece em pg_stat_activity e nos logs do Postgres (AoP cap. 7).
        var moduleConnection = new NpgsqlConnectionStringBuilder(connectionString) { ApplicationName = $"toggleforge-{schema}" };
        options
            .UseNpgsql(moduleConnection.ConnectionString, npgsql => npgsql.MigrationsHistoryTable(MigrationsHistoryTable, schema))
            .UseSnakeCaseNamingConvention();
    }

    /// <summary>O cadastro espera no máximo 2 segundos pelo Pwned Passwords; depois segue sem a checagem (ADR 0007).</summary>
    private static void ConfigurePwnedPasswordsResilience(HttpStandardResilienceOptions options)
    {
        options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(2);
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(1);
        options.Retry.MaxRetryAttempts = 1;
        options.Retry.Delay = TimeSpan.FromMilliseconds(200);
    }

    /// <summary>Regras de senha do NIST 800-63B: tamanho mínimo, sem exigência de composição, sem senha vazada.</summary>
    private static void ConfigureIdentity(IdentityOptions options)
    {
        options.Password.RequiredLength = 12;
        options.Password.RequireDigit = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequiredUniqueChars = 1;
        options.User.RequireUniqueEmail = true;
        options.User.AllowedUserNameCharacters = string.Empty;
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    }
}
