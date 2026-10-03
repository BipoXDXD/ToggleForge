namespace ToggleForge.Application.Authoring;

/// <summary>
/// Guarda só a versão criada: publicações são imutáveis, então relê-la reproduz a resposta original.
/// O fingerprint inclui o ambiente, para a mesma chave não devolver a publicação de outro ambiente.
/// </summary>
public sealed record IdempotencyRecord(
    Guid MemberId, string Key, string Fingerprint, Guid EnvironmentId, long Version, DateTimeOffset CreatedAt);
