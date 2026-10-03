namespace ToggleForge.Evaluation
{
    public sealed class EvaluationResult<T>
    {
        internal EvaluationResult(T value, EvaluationReason reason, long? snapshotVersion, int? bucket)
        {
            Value = value;
            Reason = reason;
            SnapshotVersion = snapshotVersion;
            Bucket = bucket;
        }

        public T Value { get; }

        public EvaluationReason Reason { get; }

        /// <summary>
        /// Versão publicada usada na avaliação; nula sem snapshot ou quando a flag foi avaliada fora de um,
        /// como na simulação do rascunho.
        /// </summary>
        public long? SnapshotVersion { get; }

        /// <summary>Bucket do jogador (0 a 9999) que decidiu o resultado; nulo quando o resultado não dependeu dele.</summary>
        public int? Bucket { get; }

        public bool IsFallback =>
            Reason is EvaluationReason.SnapshotMissing
                or EvaluationReason.InvalidFlagKey
                or EvaluationReason.FlagNotFound
                or EvaluationReason.MissingPlayerId;
    }
}
