namespace ToggleForge.Evaluation
{
    /// <summary>Regras sobre os motivos de uma avaliação.</summary>
    public static class EvaluationReasons
    {
        /// <summary>
        /// Verdadeiro quando o motivo faz a avaliação devolver o valor padrão informado pelo jogo, em vez de um
        /// valor decidido pela flag publicada. Classifica o motivo sozinho, sem um resultado em mãos, como um
        /// motivo que o jogo registrou em log.
        /// </summary>
        public static bool IsFallback(this EvaluationReason reason) =>
            reason is EvaluationReason.SnapshotMissing
                or EvaluationReason.InvalidFlagKey
                or EvaluationReason.FlagNotFound
                or EvaluationReason.MissingPlayerId
                or EvaluationReason.TypeMismatch
                or EvaluationReason.InvalidValue;
    }
}
