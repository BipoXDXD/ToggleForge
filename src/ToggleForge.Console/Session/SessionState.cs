using ToggleForge.Console.Api;

namespace ToggleForge.Console.Session;

/// <summary>
/// A sessão do membro como a API a viu por último, e o token antiforgery preso a ela. Quem decide se a sessão
/// vale é sempre a API: aqui só fica o que a interface precisa para se desenhar.
/// </summary>
public sealed class SessionState
{
    public Api.Session? Current { get; private set; }

    /// <summary>Disparado quando a API recusa a sessão ou o membro sai; o layout leva ao login.</summary>
    public event Action? Ended;

    internal AntiforgeryToken? AntiforgeryToken { get; set; }

    public void Start(Api.Session session)
    {
        Current = session;
        AntiforgeryToken = null;
    }

    public void End()
    {
        Current = null;
        AntiforgeryToken = null;
        Ended?.Invoke();
    }

    /// <returns>O papel do membro na organização, ou <see langword="null"/> se ele não é membro dela.</returns>
    public Membership? MembershipIn(Guid organizationId) =>
        Current?.Organizations.FirstOrDefault(membership => membership.OrganizationId == organizationId);
}
