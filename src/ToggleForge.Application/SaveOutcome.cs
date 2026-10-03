namespace ToggleForge.Application;

public enum SaveOutcome
{
    Saved,

    /// <summary>Outra operação alterou o mesmo agregado depois da leitura.</summary>
    ConcurrencyConflict,

    /// <summary>Outra requisição gravou a mesma chave de idempotência primeiro.</summary>
    IdempotencyKeyTaken,
}
