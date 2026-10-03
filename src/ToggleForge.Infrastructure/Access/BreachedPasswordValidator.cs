using Microsoft.AspNetCore.Identity;

namespace ToggleForge.Infrastructure.Access;

/// <summary>
/// Recusa senhas que aparecem em vazamentos, como o NIST 800-63B recomenda. Roda dentro do Identity, então
/// vale para toda senha nova, inclusive a troca de senha quando ela existir.
/// </summary>
internal sealed class BreachedPasswordValidator(PwnedPasswordsClient pwnedPasswords) : IPasswordValidator<IdentityUser<Guid>>
{
    public const string ErrorCode = "PasswordBreached";

    public async Task<IdentityResult> ValidateAsync(
        UserManager<IdentityUser<Guid>> manager, IdentityUser<Guid> user, string? password)
    {
        if (password is null)
        {
            return IdentityResult.Success;
        }

        // A interface do Identity não recebe CancellationToken; o timeout total do cliente HTTP limita a espera.
        return await pwnedPasswords.CheckAsync(password, CancellationToken.None) == PasswordExposure.Exposed
            ? IdentityResult.Failed(new IdentityError { Code = ErrorCode, Description = "The password appears in known data breaches." })
            : IdentityResult.Success;
    }
}
