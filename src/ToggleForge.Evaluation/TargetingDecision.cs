namespace ToggleForge.Evaluation
{
    public enum TargetingDecision
    {
        Included,

        Excluded,

        /// <summary>A regra depende do jogador, mas o contexto não traz um ID; a avaliação usa o fallback.</summary>
        PlayerIdRequired,
    }
}
