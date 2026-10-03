using System;

namespace ToggleForge.Evaluation
{
    /// <summary>
    /// Inclui a fração de jogadores cujo bucket (<see cref="RolloutBucket"/>) fica abaixo do percentual.
    /// Ampliar o percentual mantém a seed, por isso preserva quem já estava incluído.
    /// </summary>
    public sealed class PercentageRollout : ITargetingRule
    {
        public PercentageRollout(RolloutPercentage percentage, string seed)
        {
            if (seed is null)
            {
                throw new ArgumentNullException(nameof(seed));
            }

            if (seed.Trim().Length == 0)
            {
                throw new ArgumentException("The rollout seed must not be blank.", nameof(seed));
            }

            Percentage = percentage ?? throw new ArgumentNullException(nameof(percentage));
            Seed = seed;
        }

        public RolloutPercentage Percentage { get; }

        public string Seed { get; }

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

            var bucket = RolloutBucket.Compute(flag.ProjectKey, flag.EnvironmentKey, flag.FlagKey, Seed, context.PlayerId);
            return bucket < Percentage.BasisPoints ? TargetingDecision.Included : TargetingDecision.Excluded;
        }
    }
}
