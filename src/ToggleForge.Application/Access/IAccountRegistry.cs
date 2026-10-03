using ToggleForge.Domain;

namespace ToggleForge.Application.Access;

public interface IAccountRegistry
{
    /// <summary>Cria o membro, a organização e a participação como owner numa só transação.</summary>
    Task<Result<Guid>> RegisterAsync(AccountRegistration registration, CancellationToken cancellationToken);
}
