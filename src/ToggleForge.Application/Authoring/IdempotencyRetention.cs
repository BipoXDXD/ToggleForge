namespace ToggleForge.Application.Authoring;

/// <summary>
/// Esquece as chaves de idempotência depois do prazo que o contrato promete. Roda num job agendado, fora
/// das requisições; até ele passar, uma chave vencida ainda pode ser lembrada.
/// </summary>
public sealed class IdempotencyRetention(IIdempotencyStore idempotency, TimeProvider clock)
{
    /// <summary>O contrato guarda cada chave por pelo menos 24 horas.</summary>
    public static readonly TimeSpan Period = TimeSpan.FromHours(24);

    /// <summary>Apaga os registros com mais de <see cref="Period"/>; devolve quantos apagou.</summary>
    public Task<int> PurgeExpiredAsync(CancellationToken cancellationToken) =>
        idempotency.DeleteCreatedBeforeAsync(clock.GetUtcNow() - Period, cancellationToken);
}
