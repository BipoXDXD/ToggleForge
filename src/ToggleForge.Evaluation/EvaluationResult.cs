namespace ToggleForge.Evaluation
{
    public sealed class EvaluationResult<T>
    {
        internal EvaluationResult(T value, EvaluationReason reason, long? snapshotVersion)
        {
            Value = value;
            Reason = reason;
            SnapshotVersion = snapshotVersion;
        }

        public T Value { get; }

        public EvaluationReason Reason { get; }

        /// <summary>Versão publicada usada na avaliação; nula quando não havia snapshot.</summary>
        public long? SnapshotVersion { get; }

        public bool IsFallback =>
            Reason is EvaluationReason.SnapshotMissing
                or EvaluationReason.InvalidFlagKey
                or EvaluationReason.FlagNotFound
                or EvaluationReason.MissingPlayerId;
    }
}
