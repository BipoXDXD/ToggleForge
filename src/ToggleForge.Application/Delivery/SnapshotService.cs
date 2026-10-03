using ToggleForge.Application.Authoring;
using ToggleForge.Domain;
using ToggleForge.Domain.Delivery;

namespace ToggleForge.Application.Delivery;

public sealed class SnapshotService(ICredentialRepository credentials, IPublishedSnapshots snapshots, TimeProvider clock)
{
    /// <summary>Nulo para segredo malformado, desconhecido ou revogado: os três respondem igual.</summary>
    public async Task<Credential?> AuthenticateAsync(string? presentedSecret, CancellationToken cancellationToken)
    {
        if (!CredentialSecret.TryParse(presentedSecret, out var secret))
        {
            return null;
        }

        return await credentials.FindActiveByHashAsync(secret.ComputeHash(), cancellationToken);
    }

    /// <summary>Grava o download como último uso, no máximo uma vez por janela de <see cref="Credential.UsePrecision"/>.</summary>
    public async Task RecordUseAsync(Credential credential, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(credential);

        var now = clock.GetUtcNowInMicroseconds();
        if (credential.IsUseWorthRecording(now))
        {
            await credentials.RecordUseAsync(credential.Id, now, cancellationToken);
        }
    }

    public async Task<Result<SnapshotView>> FindCurrentAsync(Guid environmentId, CancellationToken cancellationToken)
    {
        var snapshot = await snapshots.FindCurrentAsync(environmentId, cancellationToken);
        return snapshot is null ? SnapshotFailures.NoPublication : snapshot;
    }
}
