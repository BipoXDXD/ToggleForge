namespace ToggleForge.Sdk
{
    /// <summary>O que uma sincronização fez com o snapshot em uso.</summary>
    public enum SyncOutcome
    {
        /// <summary>Um snapshot mais novo passou a valer.</summary>
        Updated,

        /// <summary>O servidor confirmou que o snapshot em uso é o atual.</summary>
        NotModified,

        /// <summary>Chegou uma versão igual ou anterior à que está em uso, e ela foi descartada.</summary>
        OutOfOrder,

        /// <summary>A resposta não é um snapshot que este SDK sabe avaliar; o anterior continua em uso.</summary>
        Invalid,

        /// <summary>Rede, timeout ou erro do servidor; o anterior continua em uso.</summary>
        Failed,

        /// <summary>
        /// Pedido cedo demais, antes do ritmo mínimo ou do prazo que o servidor pediu; nada foi enviado.
        /// </summary>
        Skipped,
    }
}
