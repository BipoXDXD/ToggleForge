using System;

namespace ToggleForge.Evaluation
{
    /// <summary>
    /// Flag booleana publicada. Inativa, devolve <see cref="OffValue"/>; ativa, a regra de targeting decide
    /// entre <see cref="OnValue"/> e <see cref="OffValue"/>.
    /// </summary>
    public sealed class BooleanFlag
    {
        public BooleanFlag(FlagKey key, bool isEnabled, bool onValue, bool offValue, ITargetingRule targeting)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            Targeting = targeting ?? throw new ArgumentNullException(nameof(targeting));
            IsEnabled = isEnabled;
            OnValue = onValue;
            OffValue = offValue;
        }

        public FlagKey Key { get; }

        public bool IsEnabled { get; }

        public bool OnValue { get; }

        public bool OffValue { get; }

        public ITargetingRule Targeting { get; }

        /// <summary>
        /// Avalia a flag fora de um snapshot, no projeto e ambiente dados, que entram no hash do rollout. É o
        /// que o <see cref="FlagEvaluator"/> faz depois de achar a flag; o resultado sai sem versão.
        /// </summary>
        public EvaluationResult<bool> Evaluate(
            ProjectKey projectKey, EnvironmentKey environmentKey, EvaluationContext context, bool defaultValue)
        {
            if (projectKey is null)
            {
                throw new ArgumentNullException(nameof(projectKey));
            }

            if (environmentKey is null)
            {
                throw new ArgumentNullException(nameof(environmentKey));
            }

            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            return Evaluate(new FlagIdentity(projectKey, environmentKey, Key), context, defaultValue, snapshotVersion: null);
        }

        internal EvaluationResult<bool> Evaluate(
            FlagIdentity identity, EvaluationContext context, bool defaultValue, long? snapshotVersion)
        {
            if (!IsEnabled)
            {
                return Result(OffValue, EvaluationReason.Disabled, bucket: null);
            }

            var outcome = Targeting.Decide(identity, context);
            return outcome.Decision switch
            {
                TargetingDecision.Included => Result(OnValue, EvaluationReason.Included, outcome.Bucket),
                TargetingDecision.Excluded => Result(OffValue, EvaluationReason.Excluded, outcome.Bucket),
                TargetingDecision.PlayerIdRequired => Result(defaultValue, EvaluationReason.MissingPlayerId, bucket: null),
                var unknown => throw new InvalidOperationException($"Unknown targeting decision '{unknown}'."),
            };

            EvaluationResult<bool> Result(bool value, EvaluationReason reason, int? bucket) =>
                new(value, reason, snapshotVersion, bucket);
        }
    }
}
