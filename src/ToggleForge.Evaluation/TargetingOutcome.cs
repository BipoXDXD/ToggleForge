using System;

namespace ToggleForge.Evaluation
{
    /// <summary>Decisão de uma regra de targeting e o bucket do jogador, quando a decisão dependeu dele.</summary>
    public sealed class TargetingOutcome
    {
        private TargetingOutcome(TargetingDecision decision, int? bucket)
        {
            Decision = decision;
            Bucket = bucket;
        }

        public TargetingDecision Decision { get; }

        /// <summary>Bucket do jogador (0 a 9999); nulo quando a decisão não dependeu dele.</summary>
        public int? Bucket { get; }

        /// <summary>Decisão igual para qualquer jogador, como em 0%, em 100% ou sem ID de jogador.</summary>
        public static TargetingOutcome Unbucketed(TargetingDecision decision) => new(decision, bucket: null);

        public static TargetingOutcome Bucketed(int bucket, bool isIncluded)
        {
            if (bucket < 0 || bucket >= RolloutBucket.BucketCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(bucket), bucket, $"Buckets go from 0 to {RolloutBucket.BucketCount - 1}.");
            }

            return new TargetingOutcome(isIncluded ? TargetingDecision.Included : TargetingDecision.Excluded, bucket);
        }
    }
}
