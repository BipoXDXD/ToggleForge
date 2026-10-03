using System.ComponentModel.DataAnnotations;

namespace ToggleForge.Api.Http;

/// <summary>Teto de requisições por minuto de cada tipo de cliente (STRIDE I4 e D2), configurável por ambiente.</summary>
internal sealed class RateLimits
{
    public const string Section = "RateLimits";

    /// <summary>Cada membro do console, pela conta, em todas as rotas autenticadas por sessão.</summary>
    [Range(1, int.MaxValue)]
    public int MemberPerMinute { get; set; } = 300;

    /// <summary>
    /// Cada cópia do jogo, pela credencial e pelo IP. Generoso porque CGNAT põe muitos jogadores atrás do
    /// mesmo IP.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int GameClientPerMinute { get; set; } = 120;

    /// <summary>Quem ainda não tem sessão nem credencial, pelo IP: cadastro, login e token antiforgery.</summary>
    [Range(1, int.MaxValue)]
    public int AnonymousPerMinute { get; set; } = 20;
}
