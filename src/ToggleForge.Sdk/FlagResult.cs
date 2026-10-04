using ToggleForge.Evaluation;

namespace ToggleForge.Sdk
{
    /// <summary>O resultado de uma avaliação com a origem dos dados que a decidiram.</summary>
    public sealed class FlagResult<T>
    {
        internal FlagResult(EvaluationResult<T> evaluation, SnapshotSource source)
        {
            Value = evaluation.Value;
            Reason = evaluation.Reason;
            SnapshotVersion = evaluation.SnapshotVersion;
            Bucket = evaluation.Bucket;
            IsFallback = evaluation.IsFallback;
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
    }
}
