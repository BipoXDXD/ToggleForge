using Microsoft.EntityFrameworkCore;

namespace ToggleForge.Infrastructure.Persistence;

/// <summary>
/// Locks de transação do Postgres para invariantes de contagem ("no máximo N"), que nenhuma constraint
/// expressa: checar e depois inserir sem lock é write skew.
/// </summary>
internal static class AdvisoryLocks
{
    public const int ProjectsOfOrganization = 1;
    public const int CredentialsOfEnvironment = 2;
    public const int EnvironmentsOfProject = 3;

    public static async Task AcquireAsync(DbContext context, int space, Guid owner, CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("An advisory transaction lock requires an open transaction.");
        }

        var key = BitConverter.ToInt32(owner.ToByteArray(), 0);
        await context.Database.ExecuteSqlAsync($"select pg_advisory_xact_lock({space}, {key})", cancellationToken);
    }
}
