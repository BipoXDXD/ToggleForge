using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace ToggleForge.Api.Http;

/// <summary>
/// E-mail desconhecido também paga a verificação de senha, para o tempo de resposta não revelar quais
/// contas existem (SA cap. 4). Singleton: o hash de referência é calculado uma vez, fora das requisições.
/// </summary>
internal sealed class SignInTiming
{
    private static readonly IdentityUser<Guid> _nobody = new();

    private readonly PasswordHasher<IdentityUser<Guid>> _hasher;
    private readonly string _referenceHash;

    public SignInTiming(IOptions<PasswordHasherOptions> options)
    {
        _hasher = new PasswordHasher<IdentityUser<Guid>>(options);
        _referenceHash = _hasher.HashPassword(_nobody, Guid.NewGuid().ToString());
    }

    public void SpendPasswordCheck(string password) => _hasher.VerifyHashedPassword(_nobody, _referenceHash, password);
}
