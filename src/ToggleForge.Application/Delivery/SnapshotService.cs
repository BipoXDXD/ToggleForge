using ToggleForge.Application.Authoring;
using ToggleForge.Domain;
using ToggleForge.Domain.Delivery;

namespace ToggleForge.Application.Delivery;

public sealed class SnapshotService(ICredentialRepository credentials, IPublishedSnapshots snapshots)
{
    /// <summary>Nulo para segredo malformado, desconhecido ou revogado: os três respondem igual.</summary>
    public async Task<Guid?> AuthenticateAsync(string? presentedSecret, CancellationToken cancellationToken)
    {
        if (!CredentialSecret.TryParse(presentedSecret, out var secret))
        {
            return null;
        }

        var credential = await credentials.FindActiveByHashAsync(secret.ComputeHash(), cancellationToken);
        return credential?.EnvironmentId;
    }

    public async Task<Result<SnapshotView>> FindCurrentAsync(Guid environmentId, CancellationToken cancellationToken)
    {
        var snapshot = await snapshots.FindCurrentAsync(environmentId, cancellationToken);
        return snapshot is null ? SnapshotFailures.NoPublication : snapshot;
    }
}
