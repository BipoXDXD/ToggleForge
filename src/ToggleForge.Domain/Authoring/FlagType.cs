namespace ToggleForge.Domain.Authoring;

/// <summary>Escolhido na criação da flag e imutável: um build publicado lê a flag com uma classe dele (ADR 0011).</summary>
public enum FlagType
{
    Boolean,
    Json,
}
