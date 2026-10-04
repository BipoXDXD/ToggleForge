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
                return Fallback(defaultValue, EvaluationReason.SnapshotMissing, snapshotVersion: null);
            }

            var flag = Find(snapshot, flagKey, out var missing);
            if (flag is null)
            {
                return Fallback(defaultValue, missing, snapshot.Version);
            }

            return flag is BooleanFlag boolean
                ? boolean.Evaluate(IdentityOf(snapshot, boolean), context, defaultValue, snapshot.Version)
                : Fallback(defaultValue, EvaluationReason.TypeMismatch, snapshot.Version);
        }

        public EvaluationResult<T> EvaluateJson<T>(
            ConfigurationSnapshot? snapshot, string? flagKey, EvaluationContext context, T defaultValue, Func<string, T> parse)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (parse is null)
            {
                throw new ArgumentNullException(nameof(parse));
            }

            if (snapshot is null)
            {
                return Fallback(defaultValue, EvaluationReason.SnapshotMissing, snapshotVersion: null);
            }

            var flag = Find(snapshot, flagKey, out var missing);
            if (flag is null)
            {
                return Fallback(defaultValue, missing, snapshot.Version);
            }

            return flag is JsonFlag json
                ? json.Evaluate(IdentityOf(snapshot, json), context, defaultValue, parse, snapshot.Version)
                : Fallback(defaultValue, EvaluationReason.TypeMismatch, snapshot.Version);
        }

        /// <returns>Nula quando a chave é inválida ou não está no snapshot; <paramref name="missing"/> diz qual dos dois.</returns>
        private static Flag? Find(ConfigurationSnapshot snapshot, string? flagKey, out EvaluationReason missing)
        {
            if (!FlagKey.TryParse(flagKey, out var key))
            {
                missing = EvaluationReason.InvalidFlagKey;
                return null;
            }

            if (!snapshot.TryGetFlag(key, out var flag))
            {
                missing = EvaluationReason.FlagNotFound;
                return null;
            }

            missing = default;
            return flag;
        }

        private static FlagIdentity IdentityOf(ConfigurationSnapshot snapshot, Flag flag) =>
            new FlagIdentity(snapshot.ProjectKey, snapshot.EnvironmentKey, flag.Key);

        private static EvaluationResult<T> Fallback<T>(T defaultValue, EvaluationReason reason, long? snapshotVersion) =>
            new EvaluationResult<T>(defaultValue, reason, snapshotVersion, bucket: null);
    }
}
