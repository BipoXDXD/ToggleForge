namespace ToggleForge.Domain.Authoring;

/// <summary>
/// Escolhido na criação e fixo enquanto a flag existe. A chave pode voltar com o outro tipo depois de apagada, ou por
/// rollback; o build antigo que a lê com a classe de antes recebe <c>TypeMismatch</c> e fica no default (ADR 0011).
/// </summary>
public enum FlagType
{
    Boolean,
    Json,
}
