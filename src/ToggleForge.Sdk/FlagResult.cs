using ToggleForge.Evaluation;

namespace ToggleForge.Sdk
{
    /// <summary>O resultado de uma avaliação com a origem dos dados que a decidiram.</summary>
    public sealed class FlagResult<T>
    {
        internal FlagResult(EvaluationResult<T> evaluation, SnapshotSource source)
            : this(evaluation.Value, evaluation.Reason, evaluation.SnapshotVersion, evaluation.Bucket, evaluation.IsFallback, source)
        {
        }

        private FlagResult(T value, EvaluationReason reason, long? snapshotVersion, int? bucket, bool isFallback, SnapshotSource source)
        {
            Value = value;
            Reason = reason;
            SnapshotVersion = snapshotVersion;
            Bucket = bucket;
            IsFallback = isFallback;
            Source = source;
        }

        public T Value { get; }

        public EvaluationReason Reason { get; }

        /// <summary>A versão publicada usada; nula sem snapshot.</summary>
        public long? SnapshotVersion { get; }

        /// <summary>O bucket do jogador (0 a 9999) que decidiu o resultado; nulo quando não dependeu dele.</summary>
        public int? Bucket { get; }

        /// <summary>Verdadeiro quando <see cref="Value"/> é o valor padrão informado pelo jogo.</summary>
        public bool IsFallback { get; }

        public SnapshotSource Source { get; }

        /// <summary>O snapshot passou da idade máxima: o jogo recebe o próprio valor padrão (ADR 0010).</summary>
        internal static FlagResult<T> Expired(T defaultValue) =>
            new(defaultValue, EvaluationReason.SnapshotExpired, snapshotVersion: null, bucket: null, isFallback: true, SnapshotSource.None);
    }
}
