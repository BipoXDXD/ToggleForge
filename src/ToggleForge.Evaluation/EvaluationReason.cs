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
    }
}
