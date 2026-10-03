using System;

namespace ToggleForge.Evaluation
{
    /// <summary>Avaliador puro, sem estado: o mesmo snapshot e contexto produzem sempre o mesmo resultado.</summary>
    public sealed class FlagEvaluator : IFlagEvaluator
    {
        public EvaluationResult<bool> EvaluateBoolean(
            ConfigurationSnapshot? snapshot, string? flagKey, EvaluationContext context, bool defaultValue)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (snapshot is null)
            {
                return Fallback(EvaluationReason.SnapshotMissing, snapshotVersion: null);
            }

            if (!FlagKey.TryParse(flagKey, out var key))
            {
                return Fallback(EvaluationReason.InvalidFlagKey, snapshot.Version);
            }

            if (!snapshot.TryGetFlag(key, out var flag))
            {
                return Fallback(EvaluationReason.FlagNotFound, snapshot.Version);
            }

            var identity = new FlagIdentity(snapshot.ProjectKey, snapshot.EnvironmentKey, flag.Key);
            return flag.Evaluate(identity, context, defaultValue, snapshot.Version);

            EvaluationResult<bool> Fallback(EvaluationReason reason, long? snapshotVersion) =>
                new(defaultValue, reason, snapshotVersion);
        }
    }
}
