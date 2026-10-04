using System;

namespace ToggleForge.Evaluation
{
    /// <summary>
    /// Flag booleana publicada. Inativa, devolve <see cref="OffValue"/>; ativa, a regra de targeting decide
    /// entre <see cref="OnValue"/> e <see cref="OffValue"/>.
    /// </summary>
    public sealed class BooleanFlag : Flag
    {
        public BooleanFlag(FlagKey key, bool isEnabled, bool onValue, bool offValue, ITargetingRule targeting)
            : base(key, isEnabled, targeting)
        {
            OnValue = onValue;
            OffValue = offValue;
        }

        public bool OnValue { get; }

        public bool OffValue { get; }

        /// <summary>
        /// Avalia a flag fora de um snapshot, no projeto e ambiente dados, que entram no hash do rollout. É o
        /// que o <see cref="FlagEvaluator"/> faz depois de achar a flag; o resultado sai sem versão.
        /// </summary>
        public EvaluationResult<bool> Evaluate(
            ProjectKey projectKey, EnvironmentKey environmentKey, EvaluationContext context, bool defaultValue)
        {
            var identity = IdentityIn(projectKey, environmentKey);
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            return Evaluate(identity, context, defaultValue, snapshotVersion: null);
        }

        internal EvaluationResult<bool> Evaluate(
            FlagIdentity identity, EvaluationContext context, bool defaultValue, long? snapshotVersion)
        {
            var choice = Choose(identity, context);
            var value = choice.UsesDefault ? defaultValue : choice.Pick(OnValue, OffValue);
            return new EvaluationResult<bool>(value, choice.Reason, snapshotVersion, choice.Bucket);
        }
    }
}
