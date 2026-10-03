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

        public TargetingOutcome Decide(FlagIdentity flag, EvaluationContext context)
        {
            if (flag is null)
            {
                throw new ArgumentNullException(nameof(flag));
            }

            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (Percentage.IncludesNobody)
            {
                return TargetingOutcome.Unbucketed(TargetingDecision.Excluded);
            }

            if (Percentage.IncludesEveryone)
            {
                return TargetingOutcome.Unbucketed(TargetingDecision.Included);
            }

            if (context.PlayerId is null)
            {
                return TargetingOutcome.Unbucketed(TargetingDecision.PlayerIdRequired);
            }

            var bucket = RolloutBucket.Compute(flag, Seed, context.PlayerId);
            return TargetingOutcome.Bucketed(bucket, Percentage.Includes(bucket));
        }
    }
}
