using System;

namespace ToggleForge.Evaluation
{
    /// <summary>
    /// O que toda flag publicada tem: chave, se está ativa e a regra de targeting. Inativa, a flag entrega o valor
    /// desligado; ativa, a regra decide entre ligado e desligado. Só este assembly cria tipos de flag, então quem
    /// avalia conhece todos eles (ADR 0011).
    /// </summary>
    public abstract class Flag
    {
        private protected Flag(FlagKey key, bool isEnabled, ITargetingRule targeting)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            Targeting = targeting ?? throw new ArgumentNullException(nameof(targeting));
            IsEnabled = isEnabled;
        }

        public FlagKey Key { get; }

        public bool IsEnabled { get; }

        public ITargetingRule Targeting { get; }

        internal FlagChoice Choose(FlagIdentity identity, EvaluationContext context)
        {
            if (!IsEnabled)
            {
                return FlagChoice.Off(EvaluationReason.Disabled, bucket: null);
            }

            var outcome = Targeting.Decide(identity, context);
            return outcome.Decision switch
            {
                TargetingDecision.Included => FlagChoice.On(outcome.Bucket),
                TargetingDecision.Excluded => FlagChoice.Off(EvaluationReason.Excluded, outcome.Bucket),
                TargetingDecision.PlayerIdRequired => FlagChoice.Default(EvaluationReason.MissingPlayerId),
                var unknown => throw new InvalidOperationException($"Unknown targeting decision '{unknown}'."),
            };
        }

        /// <summary>A identidade desta flag no projeto e no ambiente dados, que entram no hash do rollout.</summary>
        private protected FlagIdentity IdentityIn(ProjectKey projectKey, EnvironmentKey environmentKey)
        {
            if (projectKey is null)
            {
                throw new ArgumentNullException(nameof(projectKey));
            }

            if (environmentKey is null)
            {
                throw new ArgumentNullException(nameof(environmentKey));
            }

            return new FlagIdentity(projectKey, environmentKey, Key);
        }
    }
}
