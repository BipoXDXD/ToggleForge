using System;

namespace ToggleForge.Sdk
{
    /// <summary>Ajustes do SDK; o <see cref="ToggleForgeClient"/> confere os valores ao ser criado.</summary>
    public sealed class ToggleForgeOptions
    {
        /// <summary>O piso do intervalo, que protege a API de um jogo configurado errado (ADR 0010).</summary>
        public static readonly TimeSpan MinSyncInterval = TimeSpan.FromSeconds(15);

        /// <summary>O teto do intervalo: acima de uma hora, uma correção ou um rollback demorariam demais a chegar.</summary>
        public static readonly TimeSpan MaxSyncInterval = TimeSpan.FromHours(1);

        /// <summary>O intervalo entre sincronizações bem-sucedidas, antes da variação de ±10%; 60 s por padrão.</summary>
        public TimeSpan SyncInterval { get; set; } = TimeSpan.FromSeconds(60);
    }
}
