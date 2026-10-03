namespace ToggleForge.Evaluation
{
    /// <summary>
    /// Strategy que decide se um jogador recebe o valor ligado de uma flag ativa. Hoje existe o rollout
    /// percentual; a segmentação por atributos (Marco 5) entra como outra implementação.
    /// </summary>
    public interface ITargetingRule
    {
        TargetingDecision Decide(FlagIdentity flag, EvaluationContext context);
    }
}
