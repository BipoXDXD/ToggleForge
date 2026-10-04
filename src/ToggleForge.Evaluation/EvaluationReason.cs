namespace ToggleForge.Evaluation
{
    public enum EvaluationReason
    {
        /// <summary>Ainda não há configuração válida (por exemplo, primeira execução sem rede).</summary>
        SnapshotMissing,

        /// <summary>
        /// Só o SDK produz este motivo: o snapshot guardado passou da idade máxima sem o servidor confirmá-lo
        /// (ADR 0010).
        /// </summary>
        SnapshotExpired,

        InvalidFlagKey,

        FlagNotFound,

        /// <summary>O rollout é parcial e o contexto não traz o ID do jogador.</summary>
        MissingPlayerId,

        Disabled,

        Included,

        Excluded,
    }
}
