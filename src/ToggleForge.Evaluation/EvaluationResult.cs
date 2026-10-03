namespace ToggleForge.Evaluation
{
    /// <summary>Valor avaliado, com o motivo e a versão do snapshot que o produziu.</summary>
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

        /// <summary>Verdadeiro quando o valor é o padrão informado pelo jogo, e não a configuração.</summary>
        public bool IsFallback =>
            Reason is EvaluationReason.SnapshotMissing
                or EvaluationReason.InvalidFlagKey
                or EvaluationReason.FlagNotFound
                or EvaluationReason.MissingPlayerId;
    }
}
