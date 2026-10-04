namespace ToggleForge.Domain.Authoring;

/// <summary>
/// Escolhido na criação e fixo enquanto a flag existe. Depois de publicada num ambiente, a chave mantém o tipo em toda
/// publicação seguinte ali, mesmo apagada e recriada ou por rollback (<see cref="PublishedFlagTypes"/>, ADR 0011).
/// </summary>
public enum FlagType
{
    Boolean,
    Json,
}
