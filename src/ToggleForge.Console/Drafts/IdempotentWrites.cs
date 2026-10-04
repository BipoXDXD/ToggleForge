using ToggleForge.Console.Api;

namespace ToggleForge.Console.Drafts;

/// <summary>
/// O que vale para toda escrita que a API deduplica por <c>Idempotency-Key</c>, como publicar e reverter
/// (ADR 0004): uma chave por tentativa, reenviada igual enquanto o resultado for desconhecido.
/// </summary>
public static class IdempotentWrites
{
    // 32 caracteres hexadecimais de um GUID aleatório: dentro do padrão [A-Za-z0-9_-]{16,64} do contrato.
    public static string NewKey() => Guid.NewGuid().ToString("N");

    /// <summary>
    /// Sem resposta da API, com um erro dela ou com uma resposta fora do contrato, não dá para saber se a escrita
    /// aconteceu: a tentativa só pode ser conferida, reenviando o mesmo pedido com a mesma chave.
    /// </summary>
    public static bool LeftResultUnknown(Problem problem) =>
        problem is { Status: 0 } or { Status: >= 500 } or { Type: "/problems/unexpected-response" };
}
