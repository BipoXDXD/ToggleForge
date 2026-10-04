namespace ToggleForge.Console.Drafts;

/// <summary>Para quem vai cada valor de uma flag, no vocabulário do estúdio, a partir do rollout digitado.</summary>
public static class AudienceText
{
    private const int AllBasisPoints = 10_000;

    public static string Included(decimal rolloutPercent, bool enabled)
    {
        if (!enabled)
        {
            return "Nobody while the flag is disabled.";
        }

        return BasisPointsOf(rolloutPercent) switch
        {
            null => "The players inside the rollout.",
            0 => "No player while the rollout is 0%.",
            AllBasisPoints => "Every player while the flag is enabled.",
            { } points => $"The {RolloutShare.Format(points)} of players inside the rollout.",
        };
    }

    /// <remarks>Não depende de a flag estar ligada: desligada, todo jogador recebe este valor, e o texto já diz isso.</remarks>
    public static string Others(decimal rolloutPercent) => BasisPointsOf(rolloutPercent) switch
    {
        null => "Everyone else, and every player while the flag is disabled.",
        0 => "Every player.",
        AllBasisPoints => "Every player while the flag is disabled.",
        { } points => $"The other {RolloutShare.Format(AllBasisPoints - points)}, and every player while the flag is disabled.",
    };

    // Um rollout que não vira pontos-base ainda está sendo digitado; o texto fica sem número em vez de errado.
    private static int? BasisPointsOf(decimal rolloutPercent) =>
        RolloutShare.TryToBasisPoints(rolloutPercent, out var points) ? points : null;
}
