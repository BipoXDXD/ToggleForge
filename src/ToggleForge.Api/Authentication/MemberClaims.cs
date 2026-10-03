using System.Security.Claims;

namespace ToggleForge.Api.Authentication;

internal static class MemberClaims
{
    /// <summary>Só é chamado em endpoints autenticados pelo cookie, onde o Identity sempre põe o id.</summary>
    public static Guid MemberId(this ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        return Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : throw new InvalidOperationException("Authenticated member without an id claim.");
    }
}
