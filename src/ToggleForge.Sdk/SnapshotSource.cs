namespace ToggleForge.Sdk
{
    /// <summary>De onde veio a configuração que decidiu uma avaliação (ADR 0010).</summary>
    public enum SnapshotSource
    {
        /// <summary>Não há snapshot válido: o resultado é o valor padrão que o jogo informou.</summary>
        None,

        /// <summary>Recebido do servidor nesta execução do jogo.</summary>
        Server,
    }
}
