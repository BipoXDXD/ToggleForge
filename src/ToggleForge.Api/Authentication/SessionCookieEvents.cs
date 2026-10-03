using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using ToggleForge.Api.Http;
using ToggleForge.Application.Access;

namespace ToggleForge.Api.Authentication;

/// <summary>
/// Confere o security stamp em toda requisição sem renovar o cookie: o logout troca o stamp e derruba na hora
/// qualquer cópia do cookie antigo (STRIDE S2). O <c>SecurityStampValidator</c> do Identity renovaria o cookie a
/// cada conferência, com Set-Cookie e cabeçalhos de cache em todas as respostas.
/// </summary>
internal sealed class SessionCookieEvents(SignInManager<IdentityUser<Guid>> signIn) : CookieAuthenticationEvents
{
    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (await signIn.ValidateSecurityStampAsync(context.Principal) is null)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
        }
    }

    /// <summary>Uma API não redireciona para a tela de login: responde com Problem Details.</summary>
    public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Problems.Unauthorized("Sign in to use the console API.").ExecuteAsync(context.HttpContext);
    }

    public override Task RedirectToAccessDenied(RedirectContext<CookieAuthenticationOptions> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Problems.From(AccessFailures.Forbidden).ExecuteAsync(context.HttpContext);
    }
}
