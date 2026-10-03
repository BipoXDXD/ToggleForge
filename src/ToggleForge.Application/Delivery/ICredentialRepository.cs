using ToggleForge.Domain.Delivery;

namespace ToggleForge.Application.Delivery;

public interface ICredentialRepository
{
    /// <summary>Serializa a emissão de credenciais do ambiente até o fim da transação.</summary>
    Task LockEnvironmentAsync(Guid environmentId, CancellationToken cancellationToken);

    Task<int> CountActiveAsync(Guid environmentId, CancellationToken cancellationToken);

    Task<Credential?> FindActiveByHashAsync(string secretHash, CancellationToken cancellationToken);

    Task<IReadOnlyList<Credential>> ListActiveAsync(Guid environmentId, CancellationToken cancellationToken);

    /// <summary>Até <paramref name="limit"/> revogadas, da revogação mais recente à mais antiga.</summary>
    Task<IReadOnlyList<Credential>> ListRecentlyRevokedAsync(Guid environmentId, int limit, CancellationToken cancellationToken);

    /// <summary>Rastreada, para gravar a revogação; nula se a credencial for de outro ambiente.</summary>
    Task<Credential?> FindAsync(Guid environmentId, Guid credentialId, CancellationToken cancellationToken);

    void Add(Credential credential);
}
