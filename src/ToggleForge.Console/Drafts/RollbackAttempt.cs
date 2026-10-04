using System.Net;
using ToggleForge.Console.Api;

namespace ToggleForge.Console.Drafts;

/// <summary>
/// Uma tentativa de reverter um ambiente para uma versão anterior. Como a publicação, carrega a própria chave de
/// idempotência, e um resultado desconhecido só se confere reenviando o mesmo pedido.
/// </summary>
public sealed record RollbackAttempt(RollbackRequest Request, string IdempotencyKey)
{
    public static RollbackAttempt Of(long targetVersion, long expectedCurrentVersion, bool discardDraftChanges) =>
        new(new RollbackRequest(targetVersion, expectedCurrentVersion, discardDraftChanges), IdempotentWrites.NewKey());

    /// <summary>O que fazer com uma recusa do rollback.</summary>
    public static RollbackOutcome Classify(Problem problem) => problem switch
    {
        _ when IdempotentWrites.LeftResultUnknown(problem) => RollbackOutcome.Unknown,
        { StatusCode: HttpStatusCode.Conflict, Type: "/problems/current-version-changed" } => RollbackOutcome.CurrentVersionChanged,
        { StatusCode: HttpStatusCode.Conflict, Type: "/problems/draft-has-pending-changes" } => RollbackOutcome.DraftHasPendingChanges,
        _ => RollbackOutcome.Refused,
    };
}

public enum RollbackOutcome
{
    /// <summary>Nada mudou, e reenviar o mesmo pedido daria a mesma recusa (inclusive "já em vigor").</summary>
    Refused,

    /// <summary>A nova versão pode ter sido criada ou não; só reenviar com a mesma chave responde.</summary>
    Unknown,

    /// <summary>Outra publicação entrou no ar depois da versão que o membro viu.</summary>
    CurrentVersionChanged,

    /// <summary>O rascunho ganhou mudanças não publicadas, que o rollback substituiria.</summary>
    DraftHasPendingChanges,
}
