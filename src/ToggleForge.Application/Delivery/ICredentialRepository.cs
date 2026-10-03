using ToggleForge.Domain.Delivery;

namespace ToggleForge.Application.Delivery;

public interface ICredentialRepository
{
    /// <summary>Serializa a emissão de credenciais do ambiente até o fim da transação.</summary>
    Task LockEnvironmentAsync(Guid environmentId, CancellationToken cancellationToken);

    Task<int> CountActiveAsync(Guid environmentId, CancellationToken cancellationToken);

    Task<Credential?> FindActiveByHashAsync(string secretHash, CancellationToken cancellationToken);

    void Add(Credential credential);
}
