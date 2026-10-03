namespace ToggleForge.Evaluation
{
    /// <summary>Só é consultada para flags ativas; decide entre o valor ligado e o desligado.</summary>
    public interface ITargetingRule
    {
        TargetingDecision Decide(FlagIdentity flag, EvaluationContext context);
    }
}
