using System.Net;
using ToggleForge.Console.Api;

namespace ToggleForge.Console.Drafts;

/// <summary>
/// Uma tentativa de publicar uma revisão do rascunho. A chave de idempotência nasce com a tentativa e vale para
/// todo reenvio dela: conferir um resultado desconhecido é reenviar o mesmo pedido, e a API devolve a resposta
/// original em vez de criar outra versão (ADR 0004).
/// </summary>
public sealed record PublicationAttempt(PublicationRequest Request, string IdempotencyKey)
{
    public static PublicationAttempt Of(long draftRevision, string? summary) =>
        new(new PublicationRequest(draftRevision, string.IsNullOrWhiteSpace(summary) ? null : summary.Trim()), IdempotentWrites.NewKey());

    /// <summary>O que fazer com uma recusa da publicação.</summary>
    public static PublicationOutcome Classify(Problem problem) => problem switch
    {
        _ when IdempotentWrites.LeftResultUnknown(problem) => PublicationOutcome.Unknown,
        { StatusCode: HttpStatusCode.Conflict, Type: "/problems/draft-changed" } => PublicationOutcome.DraftChanged,
        { StatusCode: HttpStatusCode.Conflict, Type: "/problems/nothing-to-publish" } => PublicationOutcome.NothingToPublish,
        _ => PublicationOutcome.Refused,
    };
}

public enum PublicationOutcome
{
    /// <summary>Nada foi publicado, e reenviar o mesmo pedido daria a mesma recusa.</summary>
    Refused,

    /// <summary>A versão pode ter sido criada ou não; só reenviar com a mesma chave responde.</summary>
    Unknown,

    /// <summary>O rascunho mudou depois da revisão: as mudanças precisam ser revistas de novo.</summary>
    DraftChanged,

    /// <summary>O rascunho já é igual à publicação atual.</summary>
    NothingToPublish,
}
