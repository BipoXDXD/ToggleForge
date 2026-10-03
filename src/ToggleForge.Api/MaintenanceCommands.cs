using ToggleForge.Application.Authoring;
using ToggleForge.Infrastructure;

namespace ToggleForge.Api;

/// <summary>
/// Passos de manutenção que um job do deploy roda com o mesmo binário da API, nunca a cada boot de instância:
/// as migrations antes do rollout e a limpeza da idempotência num agendamento.
/// </summary>
internal static partial class MaintenanceCommands
{
    public const string Migrate = "--migrate";
    public const string PurgeIdempotencyRecords = "--purge-idempotency-records";

    /// <summary>Verdadeiro quando um comando rodou e o processo deve terminar sem subir a API.</summary>
    public static async Task<bool> TryRunAsync(WebApplication app, string[] args)
    {
        ArgumentNullException.ThrowIfNull(app);

        if (args.Contains(Migrate))
        {
            await app.Services.MigrateToggleForgeDatabaseAsync(CancellationToken.None);
            return true;
        }

        if (args.Contains(PurgeIdempotencyRecords))
        {
            await using var scope = app.Services.CreateAsyncScope();
            var purged = await scope.ServiceProvider.GetRequiredService<IdempotencyRetention>().PurgeExpiredAsync(CancellationToken.None);
            LogPurged(app.Logger, purged);
            return true;
        }

        return false;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Purged {Count} expired idempotency records")]
    private static partial void LogPurged(ILogger logger, int count);
}
