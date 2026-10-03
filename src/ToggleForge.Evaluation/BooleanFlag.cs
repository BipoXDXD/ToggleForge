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

        internal EvaluationResult<bool> Evaluate(
            FlagIdentity identity, EvaluationContext context, bool defaultValue, long snapshotVersion)
        {
            if (!IsEnabled)
            {
                return new EvaluationResult<bool>(OffValue, EvaluationReason.Disabled, snapshotVersion);
            }

            return Targeting.Decide(identity, context) switch
            {
                TargetingDecision.Included => new EvaluationResult<bool>(OnValue, EvaluationReason.Included, snapshotVersion),
                TargetingDecision.Excluded => new EvaluationResult<bool>(OffValue, EvaluationReason.Excluded, snapshotVersion),
                TargetingDecision.PlayerIdRequired =>
                    new EvaluationResult<bool>(defaultValue, EvaluationReason.MissingPlayerId, snapshotVersion),
                var unknown => throw new InvalidOperationException($"Unknown targeting decision '{unknown}'."),
            };
        }
    }
}
