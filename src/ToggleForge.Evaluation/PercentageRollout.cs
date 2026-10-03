using System;

namespace ToggleForge.Evaluation
{
    /// <summary>
    /// Inclui a fração de jogadores cujo bucket (<see cref="RolloutBucket"/>) fica abaixo do percentual.
    /// Ampliar o percentual mantém a seed, por isso preserva quem já estava incluído.
    /// </summary>
    public sealed class PercentageRollout : ITargetingRule
    {
        public PercentageRollout(RolloutPercentage percentage, RolloutSeed seed)
        {
            Percentage = percentage ?? throw new ArgumentNullException(nameof(percentage));
            Seed = seed ?? throw new ArgumentNullException(nameof(seed));
        }

        public RolloutPercentage Percentage { get; }

        public RolloutSeed Seed { get; }

        public TargetingDecision Decide(FlagIdentity flag, EvaluationContext context)
        {
            if (flag is null)
            {
                throw new ArgumentNullException(nameof(flag));
            }

            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (Percentage.BasisPoints == 0)
            {
                return TargetingDecision.Excluded;
            }

            if (Percentage.BasisPoints == RolloutPercentage.MaxBasisPoints)
            {
                return TargetingDecision.Included;
            }

            if (context.PlayerId is null)
            {
                return TargetingDecision.PlayerIdRequired;
            }

            var bucket = RolloutBucket.Compute(flag, Seed, context.PlayerId);
            return bucket < Percentage.BasisPoints ? TargetingDecision.Included : TargetingDecision.Excluded;
        }
    }
}
