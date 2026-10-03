using ToggleForge.Infrastructure;

namespace ToggleForge.Api;

/// <summary>
/// Passos de manutenção que um job do deploy roda com o mesmo binário da API, nunca a cada boot de instância.
/// </summary>
internal static class MaintenanceCommands
{
    public const string Migrate = "--migrate";

    /// <summary>Verdadeiro quando um comando rodou e o processo deve terminar sem subir a API.</summary>
    public static async Task<bool> TryRunAsync(WebApplication app, string[] args)
    {
        ArgumentNullException.ThrowIfNull(app);

        if (args.Contains(Migrate))
        {
            await app.Services.MigrateToggleForgeDatabaseAsync(CancellationToken.None);
            return true;
        }

        return false;
    }
}
