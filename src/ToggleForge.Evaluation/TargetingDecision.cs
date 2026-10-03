namespace ToggleForge.Evaluation
{
    public enum TargetingDecision
    {
        /// <summary>O jogador recebe o valor ligado da flag.</summary>
        Included,

        /// <summary>O jogador recebe o valor desligado da flag.</summary>
        Excluded,

        /// <summary>A regra depende do jogador, mas o contexto não traz um ID; a avaliação usa o fallback.</summary>
        PlayerIdRequired,
    }
}
