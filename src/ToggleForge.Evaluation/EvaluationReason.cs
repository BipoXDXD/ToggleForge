namespace ToggleForge.Evaluation
{
    public enum EvaluationReason
    {
        /// <summary>Ainda não há configuração válida (por exemplo, primeira execução sem rede).</summary>
        SnapshotMissing,

        InvalidFlagKey,

        FlagNotFound,

        /// <summary>O rollout é parcial e o contexto não traz o ID do jogador.</summary>
        MissingPlayerId,

        Disabled,

        Included,

        Excluded,

        /// <summary>
        /// Só o SDK produz este motivo: o snapshot guardado passou da idade máxima sem o servidor confirmá-lo
        /// (ADR 0010). Fica no fim para não mudar o número dos motivos anteriores.
        /// </summary>
        SnapshotExpired,
    }
}
