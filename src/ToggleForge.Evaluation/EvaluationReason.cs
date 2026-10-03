namespace ToggleForge.Evaluation
{
    /// <summary>Por que a avaliação devolveu o valor. Os quatro primeiros são fallback para o valor padrão.</summary>
    public enum EvaluationReason
    {
        /// <summary>Ainda não há configuração válida (por exemplo, primeira execução sem rede).</summary>
        SnapshotMissing,

        /// <summary>A chave pedida pelo jogo não segue o formato de chave.</summary>
        InvalidFlagKey,

        /// <summary>A flag não existe na versão publicada.</summary>
        FlagNotFound,

        /// <summary>O rollout é parcial e o contexto não traz o ID do jogador.</summary>
        MissingPlayerId,

        /// <summary>A flag está inativa e devolve o valor desligado.</summary>
        Disabled,

        /// <summary>A regra de targeting incluiu o jogador: valor ligado.</summary>
        Included,

        /// <summary>A regra de targeting excluiu o jogador: valor desligado.</summary>
        Excluded,
    }
}
