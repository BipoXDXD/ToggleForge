namespace ToggleForge.Domain;

/// <summary>Falha esperada de uma operação. <see cref="Code"/> é estável e vira o tipo do erro na API.</summary>
public sealed record Failure(FailureKind Kind, string Code, string Detail);
